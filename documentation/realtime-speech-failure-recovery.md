# Realtime speech failure and recovery

This document describes the failure contract for the authenticated audio WebSocket at
`/ws/conversations/{conversationId}/audio`. Provider diagnostics and raw provider
payloads are deliberately not part of the client contract.

## Client-visible states

| Condition | Server/client result | Recovery |
| --- | --- | --- |
| No STT provider is registered | `stt_provider_not_configured` | Configure a speech provider, then reconnect |
| Provider session cannot be opened | `speech_provider_unavailable` | Keep the conversation; retry the microphone |
| Provider connection drops | `speech_provider_unavailable` | Stop capture, reconnect the WebSocket, then resume streaming |
| Provider sends malformed JSON or an invalid transcript shape | `speech_invalid_provider_response` | Discard the invalid event; restart the live stream |
| Provider reports a transcription failure | `speech_transcription_failed` | Preserve already delivered transcript text and retry |
| Client cancels or the connection lifetime expires | `speech_cancelled` / policy close | Release microphone and socket; reconnect only after an explicit user action |
| Audio message is not binary or exceeds the configured limit | WebSocket protocol close | Correct the client audio transport before retrying |

## Resource and buffering rules

- Incoming client messages are bounded by `AudioWebSocket:MaxMessageBytes` (256 KiB by default).
- The provider event channel is bounded to 100 transcript updates and applies backpressure instead of silently dropping final events.
- Provider text frames are limited to 256 KiB before parsing.
- The receive task and transcript-pump task are coordinated. If the provider stream ends first, the client receive loop is cancelled rather than left waiting on an open socket.
- Provider session disposal has a bounded close wait and aborts the upstream socket if graceful shutdown fails.
- The mobile sender monitors `WebSocket.bufferedAmount`; when the outbound buffer exceeds 512 KiB it stops streaming instead of accumulating audio without bound.

## Privacy

- Provider exception text, raw provider events, access tokens, audio bytes and transcript contents are not copied into telemetry or user-facing errors.
- Client errors use stable provider-neutral codes and fixed messages.
- The mobile app sends the short-lived Conversa access token as the `conversa.auth` WebSocket subprotocol, never as a query parameter.
- Audio is streamed in memory as mono Linear16 PCM at 16 kHz. The mobile recorder's file output is disabled; no local audio file is intentionally created.

## Manual verification

Automated tests cover parser error events, malformed transcript shapes, provider-open failure,
partial transcript followed by provider failure, and WebSocket cleanup. A real provider
smoke test still requires a configured `Deepgram__ApiKey` and a device/native mobile build.
