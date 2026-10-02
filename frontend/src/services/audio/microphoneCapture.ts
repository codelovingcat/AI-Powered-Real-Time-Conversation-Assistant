export const MICROPHONE_AUDIO_FORMAT = {
  encoding: "linear16",
  sampleRateHertz: 16000,
  channels: 1,
  contentType: "audio/raw"
} as const;

export type MicrophoneCaptureErrorCode =
  | "unsupported"
  | "permission_denied"
  | "device_unavailable"
  | "capture_failed";

export class MicrophoneCaptureError extends Error {
  constructor(
    message: string,
    public readonly code: MicrophoneCaptureErrorCode
  ) {
    super(message);
    this.name = "MicrophoneCaptureError";
  }
}

export interface MicrophoneCaptureSession {
  readonly format: typeof MICROPHONE_AUDIO_FORMAT;
  stop(): Promise<void>;
}

export type AudioChunkHandler = (chunk: ArrayBuffer) => void;

interface AudioContextWindow extends Window {
  webkitAudioContext?: typeof AudioContext;
}

export async function startMicrophoneCapture(
  onChunk: AudioChunkHandler
): Promise<MicrophoneCaptureSession> {
  if (!hasMicrophoneSupport()) {
    throw new MicrophoneCaptureError(
      "Microphone capture is not supported by this browser or context.",
      "unsupported"
    );
  }

  let stream: MediaStream;

  try {
    stream = await navigator.mediaDevices.getUserMedia({
      audio: {
        channelCount: 1,
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true
      },
      video: false
    });
  } catch (error) {
    throw toMicrophoneCaptureError(error);
  }

  let context: AudioContext | null = null;
  let source: MediaStreamAudioSourceNode | null = null;
  let processor: ScriptProcessorNode | null = null;
  let silentOutput: GainNode | null = null;
  let stopped = false;

  try {
    const AudioContextConstructor =
      window.AudioContext ??
      (window as AudioContextWindow).webkitAudioContext;

    if (!AudioContextConstructor) {
      throw new MicrophoneCaptureError(
        "Audio capture is not supported by this browser.",
        "unsupported"
      );
    }

    context = new AudioContextConstructor({ sampleRate: 16000 });
    await context.resume();

    source = context.createMediaStreamSource(stream);
    processor = context.createScriptProcessor(4096, 1, 1);
    silentOutput = context.createGain();
    silentOutput.gain.value = 0;

    processor.onaudioprocess = (event) => {
      if (stopped) {
        return;
      }

      const input = event.inputBuffer.getChannelData(0);
      const pcm = float32ToPcm16(input, context?.sampleRate ?? 16000);

      if (pcm.byteLength > 0) {
        onChunk(pcm);
      }
    };

    source.connect(processor);
    processor.connect(silentOutput);
    silentOutput.connect(context.destination);

    return {
      format: MICROPHONE_AUDIO_FORMAT,
      async stop() {
        if (stopped) {
          return;
        }

        stopped = true;

        if (processor) {
          processor.onaudioprocess = null;
          processor.disconnect();
        }

        if (source) {
          source.disconnect();
        }

        if (silentOutput) {
          silentOutput.disconnect();
        }

        stream.getTracks().forEach((track) => track.stop());

        if (context && context.state !== "closed") {
          await context.close();
        }
      }
    };
  } catch (error) {
    stream.getTracks().forEach((track) => track.stop());

    if (context && context.state !== "closed") {
      await context.close().catch(() => undefined);
    }

    if (error instanceof MicrophoneCaptureError) {
      throw error;
    }

    throw new MicrophoneCaptureError(
      "Microphone audio capture could not be started.",
      "capture_failed"
    );
  }
}

export function float32ToPcm16(
  input: Float32Array,
  inputSampleRate: number,
  targetSampleRate = MICROPHONE_AUDIO_FORMAT.sampleRateHertz
): ArrayBuffer {
  if (input.length === 0) {
    return new ArrayBuffer(0);
  }

  if (
    !Number.isFinite(inputSampleRate) ||
    inputSampleRate <= 0 ||
    !Number.isFinite(targetSampleRate) ||
    targetSampleRate <= 0
  ) {
    throw new RangeError("Sample rates must be positive finite numbers.");
  }

  const outputLength = Math.max(
    1,
    Math.ceil((input.length * targetSampleRate) / inputSampleRate)
  );
  const output = new Int16Array(outputLength);
  const ratio = inputSampleRate / targetSampleRate;

  for (let outputIndex = 0; outputIndex < output.length; outputIndex += 1) {
    const sourcePosition = outputIndex * ratio;
    const sourceIndex = Math.floor(sourcePosition);
    const nextIndex = Math.min(sourceIndex + 1, input.length - 1);
    const interpolation = sourcePosition - sourceIndex;
    const sample =
      input[sourceIndex] +
      (input[nextIndex] - input[sourceIndex]) * interpolation;
    const clamped = Math.max(-1, Math.min(1, sample));

    output[outputIndex] =
      clamped < 0 ? Math.round(clamped * 0x8000) : Math.round(clamped * 0x7fff);
  }

  const bytes = new Uint8Array(output.byteLength);
  bytes.set(new Uint8Array(output.buffer));
  return bytes.buffer;
}

function hasMicrophoneSupport(): boolean {
  return (
    typeof navigator !== "undefined" &&
    !!navigator.mediaDevices?.getUserMedia &&
    typeof window !== "undefined" &&
    !!(
      window.AudioContext ||
      (window as AudioContextWindow).webkitAudioContext
    )
  );
}

function toMicrophoneCaptureError(error: unknown): MicrophoneCaptureError {
  if (error instanceof MicrophoneCaptureError) {
    return error;
  }

  if (error instanceof DOMException) {
    if (
      error.name === "NotAllowedError" ||
      error.name === "PermissionDeniedError" ||
      error.name === "SecurityError"
    ) {
      return new MicrophoneCaptureError(
        "Microphone permission was denied. Allow microphone access and try again.",
        "permission_denied"
      );
    }

    if (
      error.name === "NotFoundError" ||
      error.name === "DevicesNotFoundError"
    ) {
      return new MicrophoneCaptureError(
        "No microphone device is available.",
        "device_unavailable"
      );
    }
  }

  return new MicrophoneCaptureError(
    "Microphone access could not be started.",
    "capture_failed"
  );
}
