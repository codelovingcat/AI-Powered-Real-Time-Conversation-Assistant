# Realtime conversation load probes

The CI load probes are repeatable concurrency regressions. They use synthetic identities, conversations, AI/STT providers, and in-memory repositories; they do not call Gemini, Deepgram, Redis, or a production database.

## Scenarios

Run the focused load tests with:

```sh
dotnet test tests/Conversa.Api.Tests/Conversa.Api.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~RealtimeConversationLoadTests"
```

| Probe | Fixed load | Captured evidence | CI guard rail |
|---|---:|---|---|
| Assistant pipeline | 32 simultaneous conversation inputs | total duration, per-request p50/p95 latency, peak in-flight AI calls, persisted messages, unit-of-work saves, outstanding provider calls, error count | all 32 complete and persist exactly once within 15 seconds |
| Authenticated audio WebSockets | 16 simultaneous connections, one synthetic 32-byte audio chunk per connection | total duration, per-connection p50/p95 latency, peak active STT sessions, opened/disposed counts, disposal call count, chunks processed, active sessions after close, error count | every connection gets partial and final transcripts; all 16 sessions close and each is disposed exactly once within 15 seconds |

The tests print one compact `scenario=...` line to the xUnit output so CI logs retain a repeatable performance snapshot. The thresholds are guard rails against hangs and cleanup regressions; they are **not** production throughput SLOs or claims about Kestrel, real provider latency, PostgreSQL capacity, or a multi-node deployment.

## Existing adjacent controls

- `DistributedRateLimitingTests.Shared_store_enforces_one_limit_across_two_limiter_instances_under_concurrency` sends 20 concurrent leases through two limiters sharing one store and requires exactly five acquisitions.
- `DistributedRateLimitingTests.Redis_store_is_atomic_across_two_connections` has a separate synthetic Redis integration probe, gated by `CONVERSA_RUN_REDIS_INTEGRATION=true`, with a unique partition and cleanup.
- Provider failure and retry behavior is covered by `GeminiAiProviderTests` (P35/#105).

## Interpretation boundaries

These tests intentionally isolate app orchestration and resource lifecycle from external service variability and cost. They do not prove a production database's lock/connection-pool behavior or establish safe real-provider concurrency. A separate test against an explicitly approved disposable PostgreSQL environment is required before claiming a database-load capacity limit.
