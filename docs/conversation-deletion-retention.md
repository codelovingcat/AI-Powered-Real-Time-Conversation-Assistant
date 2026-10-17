# Conversation deletion and data retention

`DELETE /api/conversations/{id}` permanently removes the conversation row from the primary application database. The web app asks for explicit confirmation before issuing the request and explains that the saved messages and generated conversation summary are removed too.

## Data removed

- The conversation and its persisted configuration, including title, instruction, language pair and lifecycle timestamps.
- All saved messages and AI-derived fields attached to those messages (translations, explanations and suggested replies). The Messages foreign key is configured for database-level cascade deletion.
- The generated conversation context summary, whose ConversationId foreign key also cascades when its parent conversation is deleted.

These cascades run in PostgreSQL as part of the parent deletion; the API does not keep a soft-delete row or independently retain derived content. The foreign keys also prevent those related rows from becoming orphaned.

## Authorization and retries

The service looks up a conversation with both its ID and the authenticated user's ID. A missing ID and an ID belonging to another user both result in the same successful, empty DELETE response (HTTP 204). Repeating a delete after the first successful deletion is therefore safe and does not disclose whether a foreign user's conversation exists.

The browser removes the conversation from its local list only after the API confirms success. If the request fails, the confirmation remains open and can be retried; if the first request actually completed but its response was lost, retrying the idempotent DELETE gives the client a consistent result. Deleting the currently selected conversation also clears that selection and unmounts its live capture controls.

## Retention boundary

This operation deletes data from the live application database. It does not promise immediate erasure from database-provider backups or infrastructure-level recovery copies, which follow the hosting provider's configured retention and expiry rules. This application does not claim a fixed backup-retention duration; operators must set and document that policy separately. The deletion flow does not create a transcript archive, tombstone, or soft-delete record in the application database.

Application diagnostics should continue to avoid transcript, translation, summary and other conversation content. This document describes the live-database deletion contract, not a promise that every infrastructure backup has been physically rewritten.
