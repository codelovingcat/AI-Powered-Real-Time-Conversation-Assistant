# AI context-window guard

Conversa builds one trusted system instruction and one user prompt for each AI operation. The configured `Gemini:MaxPromptCharacters` property now bounds their combined character count **after the prompt's XML escaping and historical-field truncation, before JSON serialization**.

## Policy

- Default: `80000` characters. Valid configuration: 1–250000.
- The active conversation instruction and latest user/speaker input are retained intact.
- Historical turns are trimmed one at a time from the oldest end. The retained turns therefore form the newest suffix of the selected history and remain in their original chronological order.
- The final escaped prompt pair is measured before the HTTP request is built. This avoids estimating size from raw text and missing characters introduced by escaping.
- If the active instruction plus latest input still exceed the budget after all history is dropped, the request is rejected with a safe provider-neutral error before any outbound HTTP call. It is never sent over budget and the input is not silently truncated.
- Existing conversation ownership remains unchanged: the assistant first resolves the conversation using the authenticated user ID and loads history for that conversation ID only.

Set the environment variable `Gemini__MaxPromptCharacters` to override the default.

## Observability and privacy

The provider records these low-cardinality measurements:

- `conversa.ai.prompt.characters`: actual combined prompt character count after bounding.
- `conversa.ai.context.turns.included`: historical turns included.
- `conversa.ai.context.turns.dropped`: oldest turns removed to fit.
- `conversa.ai.context.budget_rejections`: requests whose active instruction plus latest input cannot fit.
- Existing `conversa.ai.duration`: total provider-operation latency, including prompt preparation and retry time.

Measurements and activity tags contain counts only; the active instruction, latest input, historical text, and rendered prompts are not logged or attached to telemetry. This is a deterministic **character budget**, not a tokenizer and not a claim about the model's exact token count. A tokenizer-specific context limit can be considered separately if the provider/model contract requires exact token accounting.

Focused tests:

```sh
dotnet test tests/Conversa.Api.Tests/Conversa.Api.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~GeminiAiProviderTests"
```


## Long-conversation summary

When a conversation has at least eight unsummarized messages older than the 12-turn verbatim window, the assistant asks the configured AI provider for a concise cumulative summary. It sends at most 20 chronological messages per summary refresh, capped field sizes, and a small output budget. The summary is stored on its owning conversation together with the inclusive message checkpoint and a concurrency revision; it is not exposed in conversation DTOs.

The main assistant response is persisted before summary refresh starts. A summary/provider/database failure therefore leaves the last successful summary/checkpoint and all messages intact. A checkpoint moves forward only after a non-empty, size-validated summary is generated. Simultaneous summary writers use the revision as an optimistic concurrency token; a losing request keeps the already-persisted response and retries from the winning checkpoint on a later turn.

The prompt composes the stored summary with the most recent turns. The summary is treated as untrusted historical data, never as instructions, and the active conversation instruction remains a separate trusted field on every request. The summary, transcript, and active instruction are excluded from logs and telemetry.

Focused summary tests:

```sh
dotnet test tests/Conversa.Api.Tests/Conversa.Api.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~GeminiConversationSummarizerTests"
```
