# Conversation title and metadata

The web sidebar lets an authenticated owner rename a saved conversation. The title is trimmed and validated by the existing server-side conversation validator; titles are limited to 200 characters and are rendered as React text rather than HTML.

Each saved conversation exposes its createdAt and updatedAt timestamps in the sidebar, formatted for the viewer's local locale. The API's existing conversation summary remains the source of truth for both timestamps.

## Update contract and conflict handling

Title edits use the authenticated PATCH /api/conversations/{id} endpoint with a partial body:

    {
      "title": "Updated conversation title",
      "expectedUpdatedAt": "2026-10-17T19:50:13Z"
    }

The endpoint resolves ownership from the authenticated user. If the timestamp supplied by the client is stale, the API returns HTTP 409 rather than silently overwriting a newer version. The persistence model also marks UpdatedAt as an EF Core concurrency token so a concurrent write between the read and save is surfaced as HTTP 409. The timestamp field is optional for older clients and existing partial updates.

The web client fetches the current summary when the user opens the rename editor, submits the expected timestamp, and updates its title optimistically. A failed request restores the last saved title. When the server reports a conflict, the client reloads the latest summary and asks the user to review and save again. Server error details and provider diagnostics are not shown directly.

## Scope and privacy

The edit route uses the same owner-scoped lookup as other conversation operations. Only the user-facing title and created/updated times are added to the sidebar; provider identifiers, database details, and diagnostics are not exposed as conversation metadata.
