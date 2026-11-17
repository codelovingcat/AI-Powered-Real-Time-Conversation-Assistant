import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const mockAuth = vi.hoisted(() => ({
  accessToken: "conversa-short-lived-token"
}));

vi.mock("../auth/tokenStore", () => ({
  getAccessToken: vi.fn(async () => mockAuth.accessToken)
}));

class MockWebSocket {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;
  static instances: MockWebSocket[] = [];

  readyState = MockWebSocket.CONNECTING;
  bufferedAmount = 0;
  sent: ArrayBuffer[] = [];
  readonly protocols: string[];

  onopen: (() => void) | null = null;
  onmessage: ((event: MessageEvent) => void) | null = null;
  onerror: (() => void) | null = null;
  onclose: (() => void) | null = null;

  constructor(
    public readonly url: string,
    protocols: string[]
  ) {
    this.protocols = protocols;
    MockWebSocket.instances.push(this);
  }

  send(data: ArrayBuffer) {
    this.sent.push(data);
  }

  close() {
    this.readyState = MockWebSocket.CLOSED;
    this.onclose?.();
  }
}

let audioWebSocket: typeof import("./audioWebSocket");

describe("mobile audio WebSocket", () => {
  beforeEach(async () => {
    vi.resetModules();
    vi.stubEnv("EXPO_PUBLIC_API_BASE_URL", "https://api.conversa.example");
    vi.stubGlobal("WebSocket", MockWebSocket);
    MockWebSocket.instances = [];
    mockAuth.accessToken = "conversa-short-lived-token";
    audioWebSocket = await import("./audioWebSocket");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
  });

  it("uses the authenticated subprotocol and never puts the access token in the URL", async () => {
    const pending = audioWebSocket.openAudioWebSocket(
      "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    );
    const socket = MockWebSocket.instances[0]!;

    expect(socket.url).toBe(
      "wss://api.conversa.example/ws/conversations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/audio"
    );
    expect(socket.url).not.toContain(mockAuth.accessToken);
    expect(socket.protocols).toEqual([
      audioWebSocket.WEBSOCKET_AUTH_SUBPROTOCOL,
      mockAuth.accessToken
    ]);

    socket.readyState = MockWebSocket.OPEN;
    socket.onopen?.();
    const client = await pending;

    expect(client).toBeDefined();
  });

  it("rejects connection when there is no saved access token", async () => {
    mockAuth.accessToken = "";

    await expect(
      audioWebSocket.openAudioWebSocket("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
    ).rejects.toThrow("Your session has expired.");

    expect(MockWebSocket.instances).toHaveLength(0);
  });

  it("consumes partial/final transcripts and returns only provider-neutral error codes", async () => {
    const transcripts: unknown[] = [];
    const failures: unknown[] = [];
    const pending = audioWebSocket.openAudioWebSocket(
      "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      {
        onTranscript: (event) => transcripts.push(event),
        onServerError: (event) => failures.push(event)
      }
    );
    const socket = MockWebSocket.instances[0]!;
    socket.readyState = MockWebSocket.OPEN;
    socket.onopen?.();
    await pending;

    socket.onmessage?.({
      data: JSON.stringify({
        type: "partial_transcript",
        text: "Could you help me?",
        confidence: 0.92
      })
    } as MessageEvent);
    socket.onmessage?.({
      data: JSON.stringify({
        type: "final_transcript",
        text: "Could you help me?",
        confidence: 0.98
      })
    } as MessageEvent);
    socket.onmessage?.({
      data: JSON.stringify({
        type: "error",
        code: "speech_provider_unavailable",
        message: "raw provider details must not be surfaced"
      })
    } as MessageEvent);

    expect(transcripts).toEqual([
      {
        type: "partial_transcript",
        text: "Could you help me?",
        confidence: 0.92
      },
      {
        type: "final_transcript",
        text: "Could you help me?",
        confidence: 0.98
      }
    ]);
    expect(failures).toEqual([{ code: "speech_provider_unavailable" }]);
  });

  it("stops sending when the buffered audio limit is exceeded", async () => {
    const onError = vi.fn();
    const pending = audioWebSocket.openAudioWebSocket(
      "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      { onError }
    );
    const socket = MockWebSocket.instances[0]!;
    socket.readyState = MockWebSocket.OPEN;
    socket.onopen?.();
    const client = await pending;

    socket.bufferedAmount = 512 * 1024 + 1;
    const sent = client.sendAudioChunk(new Uint8Array([1, 2, 3]).buffer);

    expect(sent).toBe(false);
    expect(socket.sent).toHaveLength(0);
    expect(onError).toHaveBeenCalledOnce();
    expect(socket.readyState).toBe(MockWebSocket.CLOSED);
  });
});
