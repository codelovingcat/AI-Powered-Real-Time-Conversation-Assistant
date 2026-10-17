export type MicrophonePermissionStatus = "unknown" | "undetermined" | "granted" | "denied";

export interface MicrophonePermissionResult {
  status: string;
  granted: boolean;
  canAskAgain?: boolean;
}

export interface RecorderPort {
  uri: string | null;
  prepareToRecordAsync(): Promise<void>;
  record(): void;
  stop(): Promise<void>;
}

export interface MicrophoneCaptureDependencies {
  recorder: RecorderPort;
  getPermission(): Promise<MicrophonePermissionResult>;
  requestPermission(): Promise<MicrophonePermissionResult>;
  configureRecordingMode(): Promise<void>;
  deleteTemporaryFile(uri: string): Promise<void>;
}

export type CapturePhase =
  | "idle"
  | "requestingPermission"
  | "ready"
  | "recording"
  | "stopping"
  | "error";

export interface MicrophoneCaptureState {
  permission: MicrophonePermissionStatus;
  canAskAgain: boolean;
  phase: CapturePhase;
  message: string | null;
}

const initialState: MicrophoneCaptureState = {
  permission: "unknown",
  canAskAgain: true,
  phase: "idle",
  message: null
};

export class MicrophoneCaptureController {
  private currentState: MicrophoneCaptureState = { ...initialState };
  private readonly listeners = new Set<(state: MicrophoneCaptureState) => void>();
  private isPrepared = false;
  private isRecording = false;
  private stopInFlight: Promise<void> | null = null;

  constructor(private readonly dependencies: MicrophoneCaptureDependencies) {}

  get state(): MicrophoneCaptureState {
    return { ...this.currentState };
  }

  subscribe(listener: (state: MicrophoneCaptureState) => void): () => void {
    this.listeners.add(listener);
    listener(this.state);
    return () => this.listeners.delete(listener);
  }

  async loadPermission(): Promise<void> {
    try {
      const permission = await this.dependencies.getPermission();
      this.applyPermission(permission);
    } catch {
      this.update({
        permission: "unknown",
        phase: "error",
        message: "Microphone permission status could not be checked."
      });
    }
  }

  /**
   * This method must only be called from an explicit user action.
   * It asks for permission but deliberately does not start recording.
   */
  async requestPermission(): Promise<boolean> {
    this.update({ phase: "requestingPermission", message: null });

    try {
      const permission = await this.dependencies.requestPermission();
      this.applyPermission(permission);

      if (!permission.granted) {
        this.update({
          phase: "idle",
          message: permission.canAskAgain === false
            ? "Microphone access is disabled. Enable it in your device settings to record."
            : "Microphone access was not granted. Allow access before starting capture."
        });
        return false;
      }

      this.update({
        phase: "ready",
        message: "Microphone access is ready. Start capture when you are ready."
      });
      return true;
    } catch {
      this.update({
        phase: "error",
        message: "Microphone permission could not be requested. Please try again."
      });
      return false;
    }
  }

  /**
   * Recording only starts after a separate explicit user action and a fresh permission check.
   * The native recorder writes to its temporary cache URI; that file is deleted on every stop.
   */
  async start(): Promise<boolean> {
    if (this.isRecording || this.currentState.phase === "requestingPermission" || this.stopInFlight) {
      return false;
    }

    this.update({ message: null, phase: "idle" });

    let permission: MicrophonePermissionResult;
    try {
      permission = await this.dependencies.getPermission();
    } catch {
      this.update({
        permission: "unknown",
        phase: "error",
        message: "Microphone permission status could not be checked."
      });
      return false;
    }

    this.applyPermission(permission);
    if (!permission.granted) {
      this.update({
        phase: "idle",
        message: permission.canAskAgain === false
          ? "Microphone access is disabled. Enable it in your device settings to record."
          : "Allow microphone access before starting capture."
      });
      return false;
    }

    try {
      await this.dependencies.configureRecordingMode();
      await this.dependencies.recorder.prepareToRecordAsync();
      this.isPrepared = true;

      // Permission may have changed while the recorder was being prepared.
      permission = await this.dependencies.getPermission();
      this.applyPermission(permission);
      if (!permission.granted) {
        await this.cleanupNativeRecorder();
        this.update({
          phase: "idle",
          message: "Microphone access is no longer available. Capture was not started."
        });
        return false;
      }

      this.dependencies.recorder.record();
      this.isRecording = true;
      this.update({
        permission: "granted",
        phase: "recording",
        message: "Microphone capture is active. Audio stays on this device and is discarded when stopped."
      });
      return true;
    } catch {
      let cleanupFailed = false;
      try {
        await this.cleanupNativeRecorder();
      } catch {
        cleanupFailed = true;
      }
      this.update({
        phase: "error",
        message: cleanupFailed
          ? "Microphone capture could not start and temporary audio could not be removed."
          : "Microphone capture could not start. Check device permissions and try again."
      });
      return false;
    }
  }

  async stop(): Promise<void> {
    if (this.stopInFlight) {
      return this.stopInFlight;
    }

    if (!this.isPrepared && !this.isRecording) {
      if (this.currentState.phase === "recording") {
        this.update({ phase: "idle" });
      }
      return;
    }

    this.update({ phase: "stopping", message: null });
    this.stopInFlight = (async () => {
      let cleanupFailed = false;
      try {
        await this.cleanupNativeRecorder();
      } catch {
        cleanupFailed = true;
      } finally {
        this.isRecording = false;
        this.isPrepared = false;
        this.stopInFlight = null;
      }

      this.update({
        phase: cleanupFailed ? "error" : (this.currentState.permission === "granted" ? "ready" : "idle"),
        message: cleanupFailed
          ? "Capture stopped, but temporary audio could not be removed. Please try again."
          : "Capture stopped. Temporary audio was discarded."
      });
    })();

    return this.stopInFlight;
  }

  async onAppStateChange(nextState: string): Promise<void> {
    if (nextState !== "active") {
      await this.stop();
      return;
    }

    // Re-check when the user returns from system settings, where permission may have changed.
    await this.loadPermission();
  }

  private applyPermission(permission: MicrophonePermissionResult): void {
    const normalized: MicrophonePermissionStatus = permission.granted
      ? "granted"
      : permission.status === "undetermined"
        ? "undetermined"
        : "denied";

    this.update({
      permission: normalized,
      canAskAgain: permission.canAskAgain !== false,
      ...(normalized !== "granted" && this.currentState.phase === "recording"
        ? { phase: "idle" as const }
        : {})
    });

    if (!permission.granted && this.isRecording) {
      void this.stop();
    }
  }

  private async cleanupNativeRecorder(): Promise<void> {
    const uriBeforeStop = this.dependencies.recorder.uri;

    try {
      if (this.isPrepared || this.isRecording) {
        try {
          await this.dependencies.recorder.stop();
        } finally {
          const uriAfterStop = this.dependencies.recorder.uri;
          const uri = uriAfterStop ?? uriBeforeStop;
          if (uri) {
            await this.dependencies.deleteTemporaryFile(uri);
          }
        }
      }
    } finally {
      // Even if native stop or file deletion fails, do not leave the controller believing
      // that capture is still active and accidentally permit an overlapping recorder.
      this.isRecording = false;
      this.isPrepared = false;
    }
  }

  private update(patch: Partial<MicrophoneCaptureState>): void {
    this.currentState = { ...this.currentState, ...patch };
    const snapshot = this.state;
    for (const listener of this.listeners) {
      listener(snapshot);
    }
  }
}
