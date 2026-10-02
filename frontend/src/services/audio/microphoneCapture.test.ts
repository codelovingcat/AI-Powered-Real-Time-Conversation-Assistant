import { describe, expect, it } from "vitest";
import {
  float32ToPcm16,
  MICROPHONE_AUDIO_FORMAT,
  MicrophoneCaptureError,
  type MicrophoneCaptureErrorCode
} from "./microphoneCapture";

describe("microphoneCapture", () => {
  it("declares the PCM format required by the streaming speech pipeline", () => {
    expect(MICROPHONE_AUDIO_FORMAT).toEqual({
      encoding: "linear16",
      sampleRateHertz: 16000,
      channels: 1,
      contentType: "audio/raw"
    });
  });

  it("converts normalized samples to signed 16-bit PCM", () => {
    const pcm = new Int16Array(
      float32ToPcm16(new Float32Array([-1, 0, 1]), 16000)
    );

    expect(Array.from(pcm)).toEqual([-32768, 0, 32767]);
  });

  it("resamples audio to the target sample rate", () => {
    const pcm = new Int16Array(
      float32ToPcm16(
        new Float32Array([1, 1, 1, 1]),
        32000,
        16000
      )
    );

    expect(pcm.length).toBe(2);
    expect(Array.from(pcm)).toEqual([32767, 32767]);
  });

  it("clamps samples outside the normalized range", () => {
    const pcm = new Int16Array(
      float32ToPcm16(new Float32Array([-2, 2]), 16000)
    );

    expect(Array.from(pcm)).toEqual([-32768, 32767]);
  });

  it("rejects invalid sample rates", () => {
    expect(() =>
      float32ToPcm16(new Float32Array([0]), 0)
    ).toThrow("Sample rates must be positive finite numbers.");
  });

  it("preserves microphone error categories", () => {
    const error = new MicrophoneCaptureError(
      "Permission denied.",
      "permission_denied"
    );

    const code: MicrophoneCaptureErrorCode = error.code;

    expect(code).toBe("permission_denied");
    expect(error).toBeInstanceOf(Error);
  });
});
