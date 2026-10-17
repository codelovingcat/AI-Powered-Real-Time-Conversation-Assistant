# AI prompt versions and synthetic regression fixtures

Conversa records prompt versions so a stored AI response can be tied to the instruction template that produced it without persisting prompt contents in diagnostics.

## Version identifiers

- `conversa-gemini-v1` identifies the original conversation prompt contract.
- `conversa-gemini-v2` adds explicit conversation-intent classification.
- `conversa-gemini-v3` adds naturalness/register guidance and a clearer JSON-only output contract while preserving the structured response fields.
- `conversa-gemini-v4` adds validated, user-selectable response length, tone, explanation detail, and suggested-reply preferences while preserving the structured response fields.
- `conversa-summary-v1` identifies the bounded older-context summarization prompt.

When changing semantic instructions, field meanings, the structured output contract, or prompt context sections, review the synthetic regression cases and deliberately decide whether the version should increment. Operational-only provider changes do not automatically require a version change.

## Where versions are recorded

- Successful Gemini results carry `AiAssistantResponse.PromptVersion`.
- Assistant messages persist the version in the nullable `Messages.PromptVersion` column, so legacy/non-Gemini messages remain valid.
- Persisted older-context summaries record `ConversationContextSummaries.PromptVersion` alongside their revision and cursor.
- Activity spans and the existing safe provider success log carry only the version identifier. They never attach the rendered prompt, API key, input transcript, summary text, or raw provider body.

## Regression fixtures

`tests/Conversa.Api.Tests/Fixtures/ai-prompt-regression.json` contains synthetic, non-customer scenarios for intent classification, untrusted delimiter-looking input, and response-quality examples marked casual, formal, and ambiguous. The quality examples lock down natural target-language phrasing, register preservation, and ambiguity handling without calling a live model.

`PromptRegressionFixturesTests` validates:
- required prompt markers and escaping;
- absence of raw delimiter-injection sequences;
- structured response parsing for every fixture;
- the fixture schema's declared prompt version against the source constant;
- casual/formal/ambiguous quality-profile coverage and expected structured response fields.

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
