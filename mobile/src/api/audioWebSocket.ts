import { appConfig } from "../config/env";
import { getAccessToken } from "../auth/tokenStore";

export const WEBSOCKET_AUTH_SUBPROTOCOL = "conversa.auth";
const MAX_BUFFERED_AUDIO_BYTES = 512 * 1024;

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

export interface AudioServerError {
  code: string;
}

export interface AudioWebSocketHandlers {
  onStatus?: (status: AudioWebSocketStatus) => void;
  onTranscript?: (update: TranscriptUpdate) => void;
  onServerError?: (error: AudioServerError) => void;
  onError?: (error: Error) => void;
}

export interface AudioWebSocketClient {
  sendAudioChunk(chunk: ArrayBuffer): boolean;
  close(): void;
}

export async function openAudioWebSocket(
  conversationId: string,
  handlers: AudioWebSocketHandlers = {}
): Promise<AudioWebSocketClient> {
  const token = await getAccessToken();

  if (!token) {
    throw new Error("Your session has expired. Sign in again to stream audio.");
  }

  const socket = new WebSocket(buildAudioWebSocketUrl(conversationId), [
    WEBSOCKET_AUTH_SUBPROTOCOL,
    token
  ]);
  let settled = false;

  const client: AudioWebSocketClient = {
    sendAudioChunk(chunk) {
      if (socket.readyState !== WebSocket.OPEN) {
        return false;
      }

      if (socket.bufferedAmount > MAX_BUFFERED_AUDIO_BYTES) {
        const error = new Error(
          "The audio connection is too slow. Streaming was stopped to protect device memory."
        );
        handlers.onError?.(error);
        client.close();
        return false;
      }

      socket.send(chunk);
      return true;
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

  handlers.onStatus?.("connecting");

  return new Promise<AudioWebSocketClient>((resolve, reject) => {
    socket.onopen = () => {
      settled = true;
      handlers.onStatus?.("open");
      resolve(client);
    };

    socket.onmessage = (event) => {
      if (typeof event.data !== "string") {
        return;
      }

      const payload = parseServerMessage(event.data);
      if (!payload) {
        return;
      }

      if ("code" in payload) {
        handlers.onServerError?.(payload);
      } else {
        handlers.onTranscript?.(payload);
      }
    };

    socket.onerror = () => {
      handlers.onStatus?.("error");
      const error = new Error("The audio connection is unavailable.");
      handlers.onError?.(error);

      if (!settled) {
        settled = true;
        reject(error);
      }
    };

    socket.onclose = () => {
      handlers.onStatus?.("closed");

      if (!settled) {
        settled = true;
        reject(new Error("The audio connection closed before it was ready."));
      }
    };
  });
}

export function buildAudioWebSocketUrl(conversationId: string): string {
  if (!appConfig.apiBaseUrl) {
    throw new Error(
      "Set EXPO_PUBLIC_API_BASE_URL to the reachable HTTPS API address before starting mobile streaming."
    );
  }

  const url = new URL(
    "/ws/conversations/" + encodeURIComponent(conversationId) + "/audio",
    appConfig.apiBaseUrl
  );

  if (url.protocol === "https:") {
    url.protocol = "wss:";
  } else if (url.protocol === "http:") {
    url.protocol = "ws:";
  } else if (url.protocol !== "wss:" && url.protocol !== "ws:") {
    throw new Error("The configured API address cannot be used for WebSocket streaming.");
  }

  // The short-lived token is sent as a WebSocket subprotocol, never in the URL.
  url.search = "";
  url.hash = "";
  return url.toString();
}

function parseServerMessage(
  value: string
): TranscriptUpdate | AudioServerError | null {
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

  if (candidate.type === "error") {
    if (
      typeof candidate.code === "string"
    ) {
      // Do not trust or render remote diagnostic text. The UI maps stable codes
      // to local, provider-neutral messages.
      return {
        code: candidate.code
      };
    }
    return null;
  }

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
