# Production Readiness

P20 establishes the operational baseline for deploying Conversa.

## Configuration and secrets

Production secrets must be supplied by the deployment platform or secret store:

- ConnectionStrings__DefaultConnection
- Authentication__Issuer
- Authentication__Audience
- Authentication__SigningKey
- Gemini__ApiKey
- Deepgram__ApiKey

Never commit these values to the repository. appsettings.Production.json contains logging defaults only.

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

The current ASP.NET Core limiter is process-local. It protects each instance independently but is not a distributed quota. For a multi-instance deployment, enforce the authoritative user/API quota at the edge or replace the limiter with a shared distributed store before relying on the application limiter as a global quota.

This avoids silently treating an in-memory limiter as a cross-instance security boundary.

## Container deployment

container.yml builds and publishes the API image to GitHub Container Registry after the main branch build and test suite passes. The immutable SHA-tagged image can be consumed by a deployment platform.

The workflow does not deploy automatically to a hosting provider because provider credentials, networking, database access and environment-specific rollout policy should remain outside source control.
