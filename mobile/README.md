# Conversa Mobile

React Native/Expo foundation for the Conversa client.

## Scope

P19 establishes:
- React Native + Expo application shell
- Secure access-token storage with Expo SecureStore
- Authentication session restore/sign-in/sign-out
- Shared backend API base URL through EXPO_PUBLIC_API_BASE_URL
- Conversation list and creation using the existing backend contract
- Initial conversation screen

Native microphone/audio streaming is intentionally left for a later mobile audio task.

## Security

- Do not commit access tokens, API keys, or provider credentials.
- EXPO_PUBLIC_* values are bundled into the application and must never contain secrets.
- Access tokens are stored in the platform secure store, not AsyncStorage or source code.
