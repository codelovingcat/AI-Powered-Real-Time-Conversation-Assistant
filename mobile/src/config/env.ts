const configuredApiBaseUrl = process.env.EXPO_PUBLIC_API_BASE_URL?.trim();

export const appConfig = {
  apiBaseUrl: configuredApiBaseUrl
    ? configuredApiBaseUrl.replace(/\/$/, "")
    : ""
} as const;
