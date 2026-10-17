# Conversation lifecycle

Each conversation has a persisted lifecycle state:

- `ready`: created but not yet started.
- `active`: the live conversation has started and may stream audio.
- `paused`: live streaming is paused and the browser releases the microphone.
- `completed`: the conversation is finished and read-only for new assistant input.

## API

All actions require authentication and the server derives the owner from the authenticated identity. The conversation ID is looked up together with that user ID, so an ID supplied by a client never grants ownership.

`POST /api/conversations/{id}/lifecycle` accepts one of the fixed action names in JSON:

- `{"action":"start"}`
- `{"action":"pause"}`
- `{"action":"resume"}`
- `{"action":"finish"}`

The response is the updated conversation summary, including `lifecycleState`, `startedAt` and `completedAt` are persisted internally for lifecycle timing; the response exposes the state but not arbitrary server internals. Start/pause/resume/finish transitions are idempotent when already in their target state. Invalid transitions receive HTTP 400; unknown or non-owned conversation IDs return the same not-found behaviour as other conversation operations.

## Audio and writes

The audio WebSocket only accepts conversations in the `active` state. The server checks both authentication and ownership before validating the lifecycle state, and rejects WebSocket requests for ready, paused, or completed conversations. Completed conversations also reject new assistant input. Existing database rows are migrated as `active` so deploying this change does not silently disable older conversations.

The web client prevents duplicate lifecycle actions while a request is in flight. Pause closes the current audio socket and stops the microphone track; resume opens a fresh authorized socket. Finish immediately stops local capture, then persists the completed state. If persistence fails, the UI clearly reports that local capture stopped but finish was not saved, and offers a retry. Unexpected socket disconnections retain the recoverable active state and use the existing bounded reconnect behaviour.
