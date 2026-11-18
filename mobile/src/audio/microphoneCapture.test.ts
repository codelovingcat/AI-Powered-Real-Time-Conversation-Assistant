import { describe, expect, it } from "vitest";
import {
  float32ToPcm16,
  MICROPHONE_AUDIO_FORMAT
} from "./microphoneCapture";

describe("mobile microphone PCM conversion", () => {
  it("matches the backend raw-audio contract", () => {
    expect(MICROPHONE_AUDIO_FORMAT).toEqual({
      encoding: "linear16",
      sampleRateHertz: 16000,
      channels: 1,
      contentType: "audio/raw"
    });
  });

  it("converts normalized samples to signed 16-bit little-endian PCM", () => {
    const view = new DataView(
      float32ToPcm16(new Float32Array([-1, 0, 1]), 16000)
    );

    expect(view.getInt16(0, true)).toBe(-32768);
    expect(view.getInt16(2, true)).toBe(0);
    expect(view.getInt16(4, true)).toBe(32767);
  });

  it("resamples input frames to 16 kHz", () => {
    const buffer = float32ToPcm16(new Float32Array([1, 1, 1, 1]), 32000);
    const view = new DataView(buffer);

    expect(buffer.byteLength).toBe(4);
    expect(view.getInt16(0, true)).toBe(32767);
    expect(view.getInt16(2, true)).toBe(32767);
  });

  it("clamps samples outside the normalized range", () => {
    const view = new DataView(
      float32ToPcm16(new Float32Array([-2, 2]), 16000)
    );

    expect(view.getInt16(0, true)).toBe(-32768);
    expect(view.getInt16(2, true)).toBe(32767);
  });

  it("handles empty input without allocating frames", () => {
    expect(float32ToPcm16(new Float32Array(), 16000).byteLength).toBe(0);
  });

  it("rejects invalid sample rates", () => {
    expect(() => float32ToPcm16(new Float32Array([0]), 0))
      .toThrow("Sample rates must be positive finite numbers.");
  });
});
