import { describe, expect, it } from "vitest";
import {
  getLiveFinalText,
  shouldRenderLivePartial,
  type LiveTimelineSnapshot
} from "./conversationTimelineState";
import { EMPTY_TRANSCRIPT } from "./transcriptState";

const aiResult = {
  id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  conversationId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  role: "speaker" as const,
  originalText: "Where is the station?",
  translation: "İstasyon nerede?",
  explanation: null,
  suggestedAnswer: "It is two blocks away.",
  suggestedAnswerTranslation: "İki blok ötede.",
  responseType: "question" as const,
  questionDetected: true,
  questionDirectedAtUser: true,
  providerName: "gemini",
  createdAt: "2026-10-06T18:00:00Z"
};

describe("conversationTimelineState", () => {
  it("keeps the latest partial as the active turn", () => {
    const snapshot: LiveTimelineSnapshot = {
      transcript: {
        ...EMPTY_TRANSCRIPT,
        partialText: "Where is the"
      },
      latestAiResult: null
    };

    expect(getLiveFinalText(snapshot)).toBeNull();
    expect(shouldRenderLivePartial(snapshot)).toBe(true);
  });

  it("uses the final transcript instead of the partial text", () => {
    const snapshot: LiveTimelineSnapshot = {
      transcript: {
        ...EMPTY_TRANSCRIPT,
        finalTexts: ["Where is the station?"],
        partialText: "Where is the station"
      },
      latestAiResult: null
    };

    expect(getLiveFinalText(snapshot)).toBe("Where is the station?");
    expect(shouldRenderLivePartial(snapshot)).toBe(false);
  });

  it("uses the persisted AI result as the stable live turn once processing completes", () => {
    const snapshot: LiveTimelineSnapshot = {
      transcript: {
        ...EMPTY_TRANSCRIPT,
        finalTexts: ["Where is the station?"]
      },
      latestAiResult: aiResult
    };

    expect(getLiveFinalText(snapshot)).toBe("Where is the station?");
    expect(shouldRenderLivePartial(snapshot)).toBe(false);
  });
});
