# Browser E2E

P32 adds a real browser-level test boundary for the web client.

## Local flow

Start PostgreSQL for the test database, then run the API in the Testing environment with:

    ASPNETCORE_ENVIRONMENT=Testing
    ASPNETCORE_URLS=http://127.0.0.1:5080
    ConnectionStrings__DefaultConnection=Host=127.0.0.1;Port=5432;Database=conversa_e2e;Username=conversa;Password=test-password
    Authentication__Issuer=Conversa.E2E
    Authentication__Audience=Conversa.E2E
    Authentication__SigningKey=<base64-encoded-32-byte-test-key>
    Cors__AllowedOrigins=http://127.0.0.1:5173

The API applies EF Core migrations automatically in Testing and uses deterministic in-process AI and speech-to-text providers. Chromium uses a fake microphone device; the test provider ignores audio bytes and emits synthetic partial/final transcripts. The E2E flow makes no Gemini or Deepgram request and sends no microphone audio to an external service.

In another terminal:

    cd frontend
    npm install
    npx playwright install chromium
    E2E_AUTH_SIGNING_KEY=<same-base64-key> npm run e2e

The authentication fixture generates a short-lived test JWT in the test process and signs in through the normal /api/auth/session endpoint. The test then creates a conversation, sends English text through the assistant API via the real web application, verifies the deterministic result, and reloads the page to verify persisted history.

## CI security

CI creates a fresh 32-byte signing key for every run and passes it only through the job environment. The PostgreSQL credentials are dedicated test values. No production provider key, production database credential, or production user data is used.

The deterministic AI provider and automatic migration-on-start behavior are enabled only when ASPNETCORE_ENVIRONMENT=Testing.
