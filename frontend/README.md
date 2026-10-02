# Conversa Web

React + TypeScript + Vite web client foundation.

## Development

1. Start the backend on http://localhost:5080.
2. From this directory run:

    npm install
    npm run dev

The Vite dev server runs on http://localhost:5173.

During local development, /api, /health, and /ws are proxied to the backend. Set VITE_API_BASE_URL when the frontend must call a different backend origin.

## Build

    npm run typecheck
    npm run build

No API keys, database credentials, or signing secrets belong in the frontend.
