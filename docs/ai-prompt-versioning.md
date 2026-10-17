# AI prompt versioning and regression fixtures

Conversa keeps version identifiers separate for the live-assistant prompt and the context-summary prompt:

- `AiPromptVersions.Assistant` identifies the current system instructions, user-prompt composition, safety contract and structured-result expectations used for a live assistant response.
- `AiPromptVersions.ContextSummary` identifies the bounded summarization prompt and its JSON-output contract.

## Change procedure

Any semantic edit to a system prompt, prompt composition, safety instruction, required fields, or output interpretation must bump the corresponding version constant in `src/Conversa.Application/Ai/AiPromptVersions.cs`. Update `tests/Conversa.Api.Tests/Gemini/Fixtures/assistant-regression.json` when the live assistant prompt is intentionally re-baselined. Keep the fixture set synthetic: never copy production transcripts, private prompts, API keys, provider bodies, or other user content into it.

The test suite pins the fixture's assistant-prompt version to the code version. A version bump without a reviewed fixture update fails the regression test. The fixture exercises translation-only behavior, a direct question and suggested answer, a question aimed at someone else, a user formulation request, and hostile text that must remain untrusted input. It validates prompt composition and structured-output parsing without making a live Gemini call.

## Result metadata and diagnostics

- Each live AI result carries its prompt version through `AiAssistantResponse`, the persisted message record, and the message DTO.
- Each stored context summary carries its summary-prompt version. Existing rows remain `null` (unversioned) rather than being assigned a misleading historical version.
- The assistant provider emits `conversa.ai.prompt_version` as a low-cardinality activity tag. The summary provider emits `conversa.ai.summary_prompt_version` and safe logs include only the version and operation timing/status.
- Prompt versions are non-sensitive metadata. Prompts, transcript text, summaries, API keys, and raw provider bodies are not included in logs or telemetry.

## Storage migration

Migration `20261017142000_AddAiPromptVersionMetadata` adds nullable, bounded-length version columns to messages and conversation summaries. Existing records are preserved as unversioned; no transcript or summary data is rewritten.
