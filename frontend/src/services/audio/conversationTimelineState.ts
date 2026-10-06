import type { ConversationMessage } from "../api/messageService";
import type { TranscriptSnapshot } from "./transcriptState";

export type LiveConnectionState =
  | "idle"
  | "connecting"
  | "connected"
  | "reconnecting"
  | "disconnected"
  | "error";

export interface LiveTimelineSnapshot {
  transcript: TranscriptSnapshot;
  latestAiResult: ConversationMessage | null;
}

export function getLiveFinalText(snapshot: LiveTimelineSnapshot): string | null {
  const fromAiResult = snapshot.latestAiResult?.originalText.trim();
  if (fromAiResult) {
    return fromAiResult;
  }

  const transcriptFinal = snapshot.transcript.finalTexts.at(-1)?.trim();
  return transcriptFinal || null;
}

export function shouldRenderLivePartial(snapshot: LiveTimelineSnapshot): boolean {
  return (
    !getLiveFinalText(snapshot) &&
    snapshot.transcript.partialText.trim().length > 0
  );
}
