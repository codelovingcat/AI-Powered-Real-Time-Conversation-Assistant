import { getAccessToken } from "../auth/accessTokenStore";
import { appConfig } from "../../config/env";

export const WEBSOCKET_AUTH_SUBPROTOCOL = "conversa.auth";

export type AudioWebSocketStatus =
  | "connecting"
  | "open"
  | "closing"
  | "closed"
  | "error";

export interface TranscriptUpdate {
  type: "partial_transcript" | "final_transcript";
  text: string;
  confidence: number | null;
}

export interface AudioWebSocketHandlers {
  onStatus?: (status: AudioWebSocketStatus) => void;
  onTranscript?: (update: TranscriptUpdate) => void;
  onError?: (error: Error) => void;
}

export interface AudioWebSocketClient {
  sendAudioChunk(chunk: ArrayBuffer): void;
  close(): void;
}

export function openAudioWebSocket(
  conversationId: string,
  handlers: AudioWebSocketHandlers = {}
): AudioWebSocketClient {
  const token = getAccessToken();

  if (!token) {
    const error = new Error("Authentication is required for audio streaming.");
    handlers.onError?.(error);
    throw error;
  }

  const socket = new WebSocket(buildAudioWebSocketUrl(conversationId), [
    WEBSOCKET_AUTH_SUBPROTOCOL,
    token
  ]);

  handlers.onStatus?.("connecting");

  socket.onopen = () => {
    handlers.onStatus?.("open");
  };

  socket.onmessage = (event) => {
    if (typeof event.data !== "string") {
      return;
    }

    const update = parseTranscriptUpdate(event.data);
    if (update) {
      handlers.onTranscript?.(update);
    }
  };

  socket.onerror = () => {
    const error = new Error("The audio WebSocket connection is unavailable.");
    handlers.onStatus?.("error");
    handlers.onError?.(error);
  };

  socket.onclose = () => {
    handlers.onStatus?.("closed");
  };

  return {
    sendAudioChunk(chunk) {
      if (socket.readyState !== WebSocket.OPEN) {
        return;
      }

      socket.send(chunk);
    },
    close() {
      if (
        socket.readyState === WebSocket.CLOSING ||
        socket.readyState === WebSocket.CLOSED
      ) {
        return;
      }

      handlers.onStatus?.("closing");
      socket.close(1000, "audio capture stopped");
    }
  };
}

export function buildAudioWebSocketUrl(conversationId: string): string {
  const path =
    `/ws/conversations/${encodeURIComponent(conversationId)}/audio`;

  if (!appConfig.apiBaseUrl) {
    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    return protocol + "//" + window.location.host + path;
  }

  const url = new URL(
    appConfig.apiBaseUrl + (appConfig.apiBaseUrl.endsWith("/") ? "" : "/")
      + path.slice(1)
  );

  if (url.protocol === "https:") {
    url.protocol = "wss:";
  } else if (url.protocol === "http:") {
    url.protocol = "ws:";
  } else if (url.protocol !== "wss:" && url.protocol !== "ws:") {
    throw new Error("The configured API URL cannot be used for WebSocket streaming.");
  }

  url.search = "";
  return url.toString();
}

function parseTranscriptUpdate(value: string): TranscriptUpdate | null {
  let payload: unknown;

  try {
    payload = JSON.parse(value);
  } catch {
    return null;
  }

  if (typeof payload !== "object" || payload === null) {
    return null;
  }

  const candidate = payload as Record<string, unknown>;

  if (
    (candidate.type !== "partial_transcript" &&
      candidate.type !== "final_transcript") ||
    typeof candidate.text !== "string" ||
    (candidate.confidence !== null &&
      typeof candidate.confidence !== "number")
  ) {
    return null;
  }

  return {
    type: candidate.type,
    text: candidate.text,
    confidence: candidate.confidence
  };
}
