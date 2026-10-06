# Conversa API Production Deployment

P23 deploys the containerized Conversa API to **Render** as an image-backed web service.

Render supports pulling prebuilt images from GitHub Container Registry (GHCR), including private images, and supports health checks and deploy hooks for image-backed services. A specific tag or digest can be selected at deploy time, which gives us a deterministic rollback path. citeturn597444search0turn165385search1

## Architecture

```text
GitHub main
   |
   v
Container workflow
   |
   v
GHCR
   |
   |  manual Production Deploy workflow
   v
Render Web Service (Frankfurt)
   |
   +--> HTTPS /health
   +--> HTTPS /health/ready
   |
   +--> Neon PostgreSQL
   +--> Gemini
   +--> Deepgram
```

The service runs from the prebuilt GHCR image. Render terminates TLS at its edge and forwards traffic to the container's HTTP port. The container is configured to listen on Render's `PORT`/10000 port. citeturn861132search0turn861132search7

## One-time Render setup

1. Connect the Render integration in ChatGPT and open the target workspace.
2. Create or sync the Blueprint from `render.yaml`.
3. Create a Render container registry credential named `conversa-ghcr-read`.
   - Registry: `ghcr.io`
   - Username: `codelovingcat`
   - Token: GitHub token with `read:packages`
4. Configure the environment variables marked `sync: false`.
5. Set `PRODUCTION_BASE_URL` as the GitHub production environment/repository variable to the service's HTTPS URL, for example `https://conversa-api.onrender.com`.
6. Store the Render deploy hook URL as the GitHub **production environment secret** `RENDER_DEPLOY_HOOK_URL`.

Render recommends environment variables for production secrets and supports marking secret values as unsynchronized in Blueprints. citeturn165385search0

## Production environment variables

Required settings are validated during API startup. Values marked as secrets must be stored in Render/GitHub secret storage and never in source control:

| Variable | Source |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Neon pooled connection string |
| `Gemini__ApiKey` | Gemini secret |
| `Deepgram__ApiKey` | Deepgram secret |
| `Authentication__Issuer` | External identity configuration |
| `Authentication__Audience` | External identity configuration |
| `Authentication__SigningKey` | 256-bit-or-longer signing secret, base64 encoded |
| `RateLimiting__RedisConnectionString` | Render-provided shared Valkey/Redis endpoint |
| `Cors__AllowedOrigins` | Exact browser origin(s), comma-separated |
| `Observability__Enabled` | `true` when telemetry export is desired |
| `Observability__ServiceName` | `conversa-api` |
| `Observability__OtlpEndpoint` | OTLP collector HTTPS endpoint |

Fixed production defaults:

| Variable | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `PORT` | `10000` |
| `ASPNETCORE_HTTP_PORTS` | `10000` |
| `Authentication__AccessTokenLifetimeMinutes` | `15` |
| `Authentication__SessionLifetimeDays` | `7` |

Do not put any secrets in `render.yaml`, GitHub source, workflow logs, or image build arguments. `Observability__OtlpEndpoint` is configuration, not a credential; any exporter authentication headers must be supplied through the telemetry backend/secret mechanism and never committed.

## Database migration

The Docker image contains an EF migration bundle at `/app/efbundle`.

Render runs:

```bash
/app/efbundle --connection "$ConnectionStrings__DefaultConnection"
```

as the service's pre-deploy command. Render documents pre-deploy commands as the place to run database migrations before the new deploy receives traffic. citeturn165385search3turn165385search2

The new `DataProtectionKeys` migration stores ASP.NET Core Data Protection keys in Neon. This is important because the P22 persistent session cookie must remain decryptable after a Render restart or a new instance. Microsoft documents `PersistKeysToDbContext` as the EF Core provider for sharing Data Protection keys across application instances. citeturn229896search1turn229896search12

## Deploying a release

The GitHub Actions workflow is intentionally manual:

**Actions → Production Deploy → Run workflow**

Leave `image_tag` empty to deploy the current commit SHA. To deploy a known release, provide the exact GHCR tag/commit SHA.

The workflow:

1. Logs into GHCR with the workflow's read-only package permission.
2. Verifies the requested image exists.
3. Calls the secret Render deploy hook with that exact image tag.
4. Waits for `/health` and `/health/ready` to return successfully over HTTPS.

This means production deployment is separate from image publishing and never deploys an unverified image implicitly.

## Rollback

Use the same workflow with a previously known-good image tag.

Example:

```text
image_tag = <known-good-git-sha>
```

Render supports deploying a specific image tag or digest to an image-backed service, so the same process can be used to roll back to an existing GHCR image. The old image must remain available in GHCR for the rollback to succeed. citeturn597444search0

## Health checks

The Render health check is:

```text
GET /health/ready
```

It includes the database readiness check and returns HTTP 503 when the service is not ready. Render uses HTTP health checks to determine whether a newly deployed web-service instance is ready to receive traffic. citeturn597444search2

## Why Render

The repository already publishes immutable SHA-tagged images to GHCR. Render's image-backed service model lets us reuse those images without building the same Docker image a second time, while deploy hooks give the GitHub Actions workflow explicit control over which tag reaches production. citeturn597444search0turn165385search1
