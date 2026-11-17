export const MICROPHONE_AUDIO_FORMAT = {
  encoding: "linear16",
  sampleRateHertz: 16000,
  channels: 1,
  contentType: "audio/raw"
} as const;

/**
 * Convert native floating-point microphone frames to signed little-endian PCM16.
 * Linear interpolation resamples devices that deliver at 44.1/48 kHz to the
 * 16 kHz mono stream expected by the backend speech session.
 */
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
    const sourceIndex = Math.min(Math.floor(sourcePosition), input.length - 1);
    const nextIndex = Math.min(sourceIndex + 1, input.length - 1);
    const interpolation = sourcePosition - sourceIndex;
    const sourceSample = input[sourceIndex] ?? 0;
    const nextSample = input[nextIndex] ?? sourceSample;
    const sample =
      sourceSample + (nextSample - sourceSample) * interpolation;
    const clamped = Math.max(-1, Math.min(1, sample));

    output[outputIndex] =
      clamped < 0 ? Math.round(clamped * 0x8000) : Math.round(clamped * 0x7fff);
  }

  const view = new DataView(new ArrayBuffer(output.byteLength));
  for (let index = 0; index < output.length; index += 1) {
    view.setInt16(index * 2, output[index] ?? 0, true);
  }

  return view.buffer;
}
