import type { TranscriptUpdate } from "./audioWebSocket";

export interface TranscriptSnapshot {
  finalTexts: string[];
  partialText: string;
  confidence: number | null;
}

export const EMPTY_TRANSCRIPT: TranscriptSnapshot = {
  finalTexts: [],
  partialText: "",
  confidence: null
};

export function applyTranscriptUpdate(
  current: TranscriptSnapshot,
  update: TranscriptUpdate
): TranscriptSnapshot {
  const text = update.text.trim();

  if (!text) {
    return current;
  }

  if (update.type === "partial_transcript") {
    return {
      ...current,
      partialText: text,
      confidence: normalizeConfidence(update.confidence)
    };
  }

  const previousFinal = current.finalTexts.at(-1);
  const finalTexts =
    previousFinal === text
      ? current.finalTexts
      : [...current.finalTexts, text];

  return {
    finalTexts,
    partialText: "",
    confidence: normalizeConfidence(update.confidence)
  };
}

function normalizeConfidence(value: number | null): number | null {
  if (value === null || !Number.isFinite(value)) {
    return null;
  }

  return Math.max(0, Math.min(1, value));
}
