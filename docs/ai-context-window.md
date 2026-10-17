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


## Persisted long-conversation summary

After at least eight older messages fall outside the verbatim recent window, the application may summarize exactly eight not-yet-summarized messages. The remaining recent messages stay verbatim. Each summary record is keyed by conversation ID and stores the last represented message cursor (CreatedAt and Id), update time, and a monotonically increasing revision.

- A refresh consumes only messages belonging to the already owner-authorized conversation, between the last committed summary cursor and the oldest message in the verbatim recent window.
- Summary generation receives the current instruction as a relevance hint, the previous committed summary, and at most eight older turns. Fields are individually bounded before escaping; the generated system/user prompt has a 48,000-character ceiling. The model output must be a non-empty JSON summary no longer than 8,000 characters; the provider response body is capped at 64 KiB.
- At most two model attempts are made, with a 15-second per-attempt timeout cap. No transcript, summary, API key, or raw provider error body is written to logs.
- A summary is committed only after successful generation and validation. Optimistic revision checks stop concurrent refreshes from overwriting a newer summary; a failed or stale refresh uses the last committed summary. A failed refresh never mutates the conversation transcript.
- Summary rows cascade with conversation deletion. Summary/message lookup and writes also verify the owning user in the repository query.
- When assembling the final AI prompt, oldest verbatim history turns are removed first if needed; the older summary is omitted only after those turns are removed. The active instruction and latest input are never truncated to make room for history.
- Summary generation is a separate idempotent provider operation. It is bounded to two attempts and may be repeated after a failed commit; the saved cursor ensures successfully committed batches are not summarized again.

Additional low-cardinality metrics:

- conversa.ai.summary.requests and conversa.ai.summary.duration record outcome and latency including retries.
- conversa.ai.summary.prompt.characters and conversa.ai.summary.input.turns record bounded input size without recording transcript text.
- conversa.ai.context.summary_included indicates whether the stored summary survived final prompt budgeting.

## Prompt versioning

See [AI prompt versioning and regression fixtures](ai-prompt-versioning.md) for the required version bump policy and the synthetic regression suite.
