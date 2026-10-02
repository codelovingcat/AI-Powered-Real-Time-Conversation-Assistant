import { describe, expect, it } from "vitest";
import {
  applyTranscriptUpdate,
  EMPTY_TRANSCRIPT,
  type TranscriptSnapshot
} from "./transcriptState";

const partial = {
  type: "partial_transcript" as const,
  text: "Where is the",
  confidence: 0.61
};

const final = {
  type: "final_transcript" as const,
  text: "Where is the station?",
  confidence: 0.94
};

describe("transcriptState", () => {
  it("replaces partial transcript text while speech is in progress", () => {
    const first = applyTranscriptUpdate(EMPTY_TRANSCRIPT, partial);

    expect(first.partialText).toBe("Where is the");

    const second = applyTranscriptUpdate(first, {
      ...partial,
      text: "Where is the station"
    });

    expect(second.finalTexts).toEqual([]);
    expect(second.partialText).toBe("Where is the station");
  });

  it("moves final transcript into the stable history and clears partial text", () => {
    const current: TranscriptSnapshot = {
      finalTexts: [],
      partialText: "Where is the station",
      confidence: 0.7
    };

    const next = applyTranscriptUpdate(current, final);

    expect(next.finalTexts).toEqual(["Where is the station?"]);
    expect(next.partialText).toBe("");
    expect(next.confidence).toBe(0.94);
  });

  it("does not duplicate the same consecutive final transcript", () => {
    const first = applyTranscriptUpdate(EMPTY_TRANSCRIPT, final);
    const second = applyTranscriptUpdate(first, final);

    expect(second.finalTexts).toEqual(["Where is the station?"]);
  });

  it("ignores empty transcript updates", () => {
    const current: TranscriptSnapshot = {
      finalTexts: ["Hello"],
      partialText: "there",
      confidence: 0.5
    };

    expect(
      applyTranscriptUpdate(current, {
        type: "partial_transcript",
        text: "   ",
        confidence: 0.2
      })
    ).toEqual(current);
  });

  it("clamps out-of-range confidence values", () => {
    expect(
      applyTranscriptUpdate(EMPTY_TRANSCRIPT, {
        type: "partial_transcript",
        text: "Hello",
        confidence: 3
      }).confidence
    ).toBe(1);
  });
});
