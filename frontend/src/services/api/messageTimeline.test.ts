import { describe, expect, it } from "vitest";
import { groupMessagesIntoTurns } from "./messageTimeline";
import type { ConversationMessage } from "../api/messageService";

function message(
  id: string,
  role: ConversationMessage["role"],
  createdAt: string
): ConversationMessage {
  return {
    id,
    conversationId: "conversation-1",
    role,
    originalText: id,
    translation: null,
    explanation: null,
    suggestedAnswer: null,
    suggestedAnswerTranslation: null,
    responseType: null,
    questionDetected: false,
    questionDirectedAtUser: false,
    providerName: null,
    createdAt
  };
}

describe("groupMessagesIntoTurns", () => {
  it("groups consecutive speaker and user messages", () => {
    const turns = groupMessagesIntoTurns([
      message("speaker-1", "speaker", "2026-10-06T10:00:00Z"),
      message("speaker-2", "speaker", "2026-10-06T10:00:04Z"),
      message("user-1", "user", "2026-10-06T10:00:10Z"),
      message("user-2", "user", "2026-10-06T10:00:14Z")
    ]);

    expect(turns).toHaveLength(2);
    expect(turns[0].messages.map((item) => item.id)).toEqual([
      "speaker-1",
      "speaker-2"
    ]);
    expect(turns[0].startedAt).toBe("2026-10-06T10:00:00Z");
    expect(turns[0].endedAt).toBe("2026-10-06T10:00:04Z");
    expect(turns[1].messages.map((item) => item.id)).toEqual([
      "user-1",
      "user-2"
    ]);
  });

  it("does not merge different roles or standalone assistant messages", () => {
    const turns = groupMessagesIntoTurns([
      message("speaker-1", "speaker", "2026-10-06T10:00:00Z"),
      message("assistant-1", "assistant", "2026-10-06T10:00:05Z"),
      message("assistant-2", "assistant", "2026-10-06T10:00:06Z"),
      message("speaker-2", "speaker", "2026-10-06T10:00:10Z")
    ]);

    expect(turns.map((turn) => ({
      role: turn.role,
      ids: turn.messages.map((item) => item.id)
    }))).toEqual([
      { role: "speaker", ids: ["speaker-1"] },
      { role: "assistant", ids: ["assistant-1"] },
      { role: "assistant", ids: ["assistant-2"] },
      { role: "speaker", ids: ["speaker-2"] }
    ]);
  });
});
