import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  buildAudioWebSocketUrl,
  openAudioWebSocket,
  WEBSOCKET_AUTH_SUBPROTOCOL,
  type AudioWebSocketStatus
} from "./audioWebSocket";
import { clearAccessToken, setAccessToken } from "../auth/accessTokenStore";

class MockWebSocket {
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;

  readonly OPEN = MockWebSocket.OPEN;
  readonly CLOSING = MockWebSocket.CLOSING;
  readonly CLOSED = MockWebSocket.CLOSED;

  readyState = MockWebSocket.OPEN;
  sent: ArrayBuffer[] = [];
  protocols: string[];

  onopen: (() => void) | null = null;
  onmessage: ((event: MessageEvent) => void) | null = null;
  onerror: (() => void) | null = null;
  onclose: (() => void) | null = null;

  constructor(
    public readonly url: string,
    protocols: string[]
  ) {
    this.protocols = protocols;
  }

  send(data: ArrayBuffer) {
    this.sent.push(data);
  }

  close() {
    this.readyState = MockWebSocket.CLOSED;
    this.onclose?.();
  }
}

describe("audioWebSocket", () => {
  beforeEach(() => {
    vi.stubGlobal("window", {
      location: {
        protocol: "https:",
        host: "localhost"
      }
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    clearAccessToken();
  });

  it("builds a WebSocket URL without credentials in the query string", () => {
    const url = buildAudioWebSocketUrl(
      "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    );

    expect(url).toMatch(
      /^(ws|wss):\/\/.*\/ws\/conversations\/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\/audio$/
    );
    expect(new URL(url).search).toBe("");
  });

  it("requires an authenticated in-memory access token", () => {
    const onError = vi.fn();

    expect(() => openAudioWebSocket("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", {
      onError
    })).toThrow("Authentication is required for audio streaming.");

    expect(onError).toHaveBeenCalledOnce();
  });

  it("sends the token as a WebSocket subprotocol instead of a query parameter", () => {
    setAccessToken("eyJhbGciOiJIUzI1NiJ9.test.signature");

    const original = globalThis.WebSocket;
    const sockets: MockWebSocket[] = [];
    class CapturingWebSocket extends MockWebSocket {
      constructor(url: string, protocols: string[]) {
        super(url, protocols);
        sockets.push(this);
      }
    }

    vi.stubGlobal("WebSocket", CapturingWebSocket);

    openAudioWebSocket("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    expect(sockets).toHaveLength(1);
    expect(sockets[0].url).not.toContain("test.signature");
    expect(sockets[0].url).not.toContain("?");
    expect(sockets[0].protocols).toEqual([
      WEBSOCKET_AUTH_SUBPROTOCOL,
      "eyJhbGciOiJIUzI1NiJ9.test.signature"
    ]);

    vi.stubGlobal("WebSocket", original);
  });

  it("reports WebSocket lifecycle and forwards binary audio", () => {
    setAccessToken("test-token-with-more-than-32-characters-123456");

    const statuses: AudioWebSocketStatus[] = [];
    const original = globalThis.WebSocket;
    const sockets: MockWebSocket[] = [];
    class CapturingWebSocket extends MockWebSocket {
      constructor(url: string, protocols: string[]) {
        super(url, protocols);
        sockets.push(this);
      }
    }

    vi.stubGlobal("WebSocket", CapturingWebSocket);

    const client = openAudioWebSocket(
      "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      { onStatus: (status) => statuses.push(status) }
    );

    expect(statuses).toContain("connecting");
    expect(sockets).toHaveLength(1);

    client.sendAudioChunk(new Uint8Array([1, 2, 3]).buffer);
    expect(sockets[0].sent).toHaveLength(1);

    sockets[0].onclose?.();
    expect(statuses).toContain("closed");

    vi.stubGlobal("WebSocket", original);
  });

  it("parses transcript updates and ignores malformed messages", () => {
    setAccessToken("test-token-with-more-than-32-characters-123456");

    const transcripts: unknown[] = [];
    const original = globalThis.WebSocket;
    const sockets: MockWebSocket[] = [];
    class CapturingWebSocket extends MockWebSocket {
      constructor(url: string, protocols: string[]) {
        super(url, protocols);
        sockets.push(this);
      }
    }

    vi.stubGlobal("WebSocket", CapturingWebSocket);

    openAudioWebSocket(
      "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      { onTranscript: (update) => transcripts.push(update) }
    );

    sockets[0].onmessage?.({
      data: JSON.stringify({
        type: "partial_transcript",
        text: "hello",
        confidence: 0.91
      })
    } as MessageEvent);

    sockets[0].onmessage?.({
      data: JSON.stringify({ type: "unexpected", text: "<bad>" })
    } as MessageEvent);

    expect(transcripts).toEqual([
      { type: "partial_transcript", text: "hello", confidence: 0.91 }
    ]);

    vi.stubGlobal("WebSocket", original);
  });
});
