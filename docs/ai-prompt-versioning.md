# AI prompt versions and synthetic regression fixtures

Conversa records prompt versions so a stored AI response can be tied to the instruction template that produced it without persisting prompt contents in diagnostics.

## Version identifiers

- `conversa-gemini-v1` identifies the main conversation system/user prompt contract assembled by `GeminiPromptBuilder`.
- `conversa-summary-v1` identifies the bounded older-context summarization prompt.

When changing semantic instructions, field meanings, the structured output contract, or prompt context sections, review the synthetic regression cases and deliberately decide whether the version should increment. Operational-only provider changes do not automatically require a version change.

## Where versions are recorded

- Successful Gemini results carry `AiAssistantResponse.PromptVersion`.
- Assistant messages persist the version in the nullable `Messages.PromptVersion` column, so legacy/non-Gemini messages remain valid.
- Persisted older-context summaries record `ConversationContextSummaries.PromptVersion` alongside their revision and cursor.
- Activity spans and the existing safe provider success log carry only the version identifier. They never attach the rendered prompt, API key, input transcript, summary text, or raw provider body.

## Regression fixtures

`tests/Conversa.Api.Tests/Fixtures/ai-prompt-regression.json` contains synthetic, non-customer scenarios for translation-only behavior, a question directed at the user, a user formulation request, and delimiter-looking text treated as untrusted input.

`PromptRegressionFixturesTests` validates:
- required prompt markers and escaping;
- absence of raw delimiter-injection sequences;
- structured response parsing for every fixture;
- the fixture schema's declared prompt version against the source constant.

Run the focused suite with:

```sh
dotnet test tests/Conversa.Api.Tests/Conversa.Api.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~PromptRegressionFixturesTests|FullyQualifiedName~GeminiAiProviderTests|FullyQualifiedName~GeminiConversationSummarizerTests"
```

The fixtures intentionally do not use live user transcripts or call Gemini. The Neon integration test uses synthetic entities inside a rolled-back transaction to verify prompt-version persistence in both message and summary rows.


## Intent classification (P40)

The assistant also records an `intent` independently of `responseType`: `statement`, `questionDirectedAtUser`, `questionDirectedAtOthers`, `rhetoricalQuestion`, `requestDirectedAtUser`, `requestDirectedAtOthers`, `userFormulationRequest`, or `ambiguous`.

Only a genuine question or request clearly directed at the user can preserve a suggested reply, and only when the trusted conversation instruction permits suggestions. Questions to other people, rhetorical questions, ordinary statements, requests to others, and ambiguous utterances are normalized to translation-only responses. User formulation input stays a separate input mode.

The prompt contract changed, so the assistant prompt version is bumped to `conversa-gemini-v2`. Synthetic fixtures cover all eight intent categories. Existing message rows retain a null intent until reprocessed.
