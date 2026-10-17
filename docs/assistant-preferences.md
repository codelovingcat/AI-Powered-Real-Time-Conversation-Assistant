# Assistant preferences

Conversa stores assistant preferences under the authenticated user's ID. The API accepts only fixed option values; preferences are not arbitrary prompt text and cannot override security rules or the active conversation instruction.

## Defaults

Users without a saved preference record receive these defaults:

| Setting | Default | Supported values |
| --- | --- | --- |
| Response length | `balanced` | `concise`, `balanced`, `detailed` |
| Tone | `contextual` | `casual`, `contextual`, `formal` |
| Explanation detail | `balanced` | `minimal`, `balanced`, `detailed` |
| Suggested replies | `true` | `true`, `false` |

The defaults preserve the current general experience for existing users. Reading preferences does not write a database row; the first successful update creates one. All settings survive sign-out and session restoration because they are stored in PostgreSQL, not only in browser state.

## API

- `GET /api/assistant-preferences` returns the authenticated user's saved settings or the defaults above.
- `PUT /api/assistant-preferences` replaces all settings. Every field is required. Unknown enum values or missing fields are rejected with HTTP 400.

Every request requires authentication. The server derives the user ID from the validated identity and never accepts a user ID from the request body. The database uses that ID as the preference row's primary key and constrains the allowed enum values.

## Application behaviour

Preferences flow through the common assistant application service, so they apply to both text submissions and live speech transcription. The prompt uses fixed instructions mapped from validated options; user-provided free-form strings are never added to privileged prompt instructions. The server also suppresses unsolicited reply suggestions when that preference is off and removes explanations for the `minimal` explanation level. An explicit request to formulate something to say still returns the requested wording.

The `20261017190000_AddUserAssistantPreferences` migration creates the preference table. Apply migrations through the repository's existing migration workflow before deploying the endpoint to an environment whose schema is not automatically migrated.
