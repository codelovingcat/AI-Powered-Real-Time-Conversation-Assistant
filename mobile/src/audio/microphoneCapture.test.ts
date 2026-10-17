import { afterEach, describe, expect, it, vi } from "vitest";
import {
  MicrophoneCaptureController,
  type MicrophoneCaptureDependencies,
  type MicrophonePermissionResult,
  type RecorderPort
} from "./microphoneCapture";

function permission(
  status: string,
  granted: boolean,
  canAskAgain = true
): MicrophonePermissionResult {
  return { status, granted, canAskAgain };
}

function createHarness(initialPermission = permission("granted", true)) {
  let currentPermission = initialPermission;
  const recorder: RecorderPort = {
    uri: "file:///cache/temporary-recording.m4a",
    prepareToRecordAsync: vi.fn(async () => undefined),
    record: vi.fn(),
    stop: vi.fn(async () => undefined)
  };
  const dependencies: MicrophoneCaptureDependencies = {
    recorder,
    getPermission: vi.fn(async () => currentPermission),
    requestPermission: vi.fn(async () => currentPermission),
    configureRecordingMode: vi.fn(async () => undefined),
    deleteTemporaryFile: vi.fn(async () => undefined)
  };
  const controller = new MicrophoneCaptureController(dependencies);

  return {
    controller,
    dependencies,
    recorder,
    setPermission(next: MicrophonePermissionResult) {
      currentPermission = next;
    }
  };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("MicrophoneCaptureController", () => {
  it("checks permission without requesting it or starting audio", async () => {
    const harness = createHarness(permission("undetermined", false));

    await harness.controller.loadPermission();

    expect(harness.dependencies.getPermission).toHaveBeenCalledTimes(1);
    expect(harness.dependencies.requestPermission).not.toHaveBeenCalled();
    expect(harness.recorder.prepareToRecordAsync).not.toHaveBeenCalled();
    expect(harness.recorder.record).not.toHaveBeenCalled();
    expect(harness.controller.state.permission).toBe("undetermined");
  });

  it("requests permission only on the explicit permission action and does not auto-record", async () => {
    const harness = createHarness(permission("granted", true));
    harness.setPermission(permission("granted", true));

    const granted = await harness.controller.requestPermission();

    expect(granted).toBe(true);
    expect(harness.dependencies.requestPermission).toHaveBeenCalledTimes(1);
    expect(harness.recorder.prepareToRecordAsync).not.toHaveBeenCalled();
    expect(harness.recorder.record).not.toHaveBeenCalled();
    expect(harness.controller.state.phase).toBe("ready");
  });

  it("does not record without permission", async () => {
    const harness = createHarness(permission("denied", false));

    const started = await harness.controller.start();

    expect(started).toBe(false);
    expect(harness.dependencies.configureRecordingMode).not.toHaveBeenCalled();
    expect(harness.recorder.prepareToRecordAsync).not.toHaveBeenCalled();
    expect(harness.recorder.record).not.toHaveBeenCalled();
  });

  it("starts only after explicit start and deletes the temporary file on stop", async () => {
    const harness = createHarness(permission("granted", true));

    expect(await harness.controller.start()).toBe(true);
    expect(harness.dependencies.configureRecordingMode).toHaveBeenCalledTimes(1);
    expect(harness.recorder.prepareToRecordAsync).toHaveBeenCalledTimes(1);
    expect(harness.recorder.record).toHaveBeenCalledTimes(1);

    await harness.controller.stop();

    expect(harness.recorder.stop).toHaveBeenCalledTimes(1);
    expect(harness.dependencies.deleteTemporaryFile).toHaveBeenCalledWith(
      "file:///cache/temporary-recording.m4a"
    );
    expect(harness.controller.state.phase).toBe("ready");
  });

  it("stops and discards audio when the app backgrounds", async () => {
    const harness = createHarness(permission("granted", true));
    await harness.controller.start();

    await harness.controller.onAppStateChange("background");

    expect(harness.recorder.stop).toHaveBeenCalledTimes(1);
    expect(harness.dependencies.deleteTemporaryFile).toHaveBeenCalledWith(
      "file:///cache/temporary-recording.m4a"
    );
    expect(harness.controller.state.phase).toBe("ready");
  });

  it("stops recording if permission is revoked while the app is in settings", async () => {
    const harness = createHarness(permission("granted", true));
    await harness.controller.start();
    harness.setPermission(permission("denied", false, false));

    await harness.controller.onAppStateChange("active");

    expect(harness.recorder.stop).toHaveBeenCalledTimes(1);
    expect(harness.dependencies.deleteTemporaryFile).toHaveBeenCalledWith(
      "file:///cache/temporary-recording.m4a"
    );
    expect(harness.controller.state.permission).toBe("denied");
    expect(harness.controller.state.phase).not.toBe("recording");
  });

  it("rechecks permission after native recorder preparation before capturing", async () => {
    const harness = createHarness(permission("granted", true));
    vi.mocked(harness.recorder.prepareToRecordAsync).mockImplementationOnce(async () => {
      harness.setPermission(permission("denied", false));
    });

    expect(await harness.controller.start()).toBe(false);
    expect(harness.recorder.prepareToRecordAsync).toHaveBeenCalledTimes(1);
    expect(harness.recorder.record).not.toHaveBeenCalled();
    expect(harness.recorder.stop).toHaveBeenCalledTimes(1);
    expect(harness.dependencies.deleteTemporaryFile).toHaveBeenCalledWith(
      "file:///cache/temporary-recording.m4a"
    );
  });
});
