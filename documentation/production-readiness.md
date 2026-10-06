# Production Readiness

P20 establishes the operational baseline for deploying Conversa.

## Configuration and secrets

Outside Development and Testing, the API requires these deployment-provided settings:

- ConnectionStrings__DefaultConnection
- Authentication__Issuer
- Authentication__Audience
- Authentication__SigningKey
- Gemini__ApiKey
- Deepgram__ApiKey
- RateLimiting__RedisConnectionString
- Cors__AllowedOrigins

Never commit these values to the repository. appsettings.Production.json contains logging defaults only. Local overrides belong in ignored appsettings.*.local.json files or environment variables.

The container build context excludes local environment files, local settings, secret files and private key material through .dockerignore. Keep provider credentials and database connection strings out of Docker build arguments, image layers and logs.

## Health endpoints

- /health is a liveness check and does not require the database.
- /health/ready checks database connectivity and returns 503 when the database is unavailable.

Keep health probes unauthenticated so the deployment platform can use them.

## Database migrations

Production schema changes remain an explicit deployment step. The existing GitHub Actions Neon migration workflow is kept separate from application startup, so an API instance does not implicitly modify production schema.

## WebSockets and multiple instances

The audio WebSocket is connection-local state. A load balancer must support WebSockets and connection affinity or sticky sessions when more than one API instance serves the same logical conversation.

Do not put WebSocket connection state in process-wide static storage.

## Rate limiting

Rate limiting uses a shared Valkey/Redis store outside Development and Testing. Production and staging must provide `RateLimiting__RedisConnectionString`; the API fails closed with HTTP 503 when the shared store is unavailable.

Authenticated partitions use validated JWT identity claims, not client-supplied headers, and the stored partition key is SHA-256 hashed. This keeps rate-limit state distributed without storing raw user identifiers.

## Production configuration validation

At startup, non-development/non-testing environments reject missing required values, weak authentication signing keys, embedded credentials in provider URLs, insecure CORS origins and known local database defaults. Validation errors name configuration keys only; secret values are never included.

## Container deployment

container.yml builds and publishes the API image to GitHub Container Registry after the main branch build and test suite passes. The immutable SHA-tagged image can be consumed by a deployment platform.

The workflow does not deploy automatically to a hosting provider because provider credentials, networking, database access and environment-specific rollout policy should remain outside source control.
