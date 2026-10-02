import { appConfig } from "../../config/env";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export async function apiGet(path: string, signal?: AbortSignal): Promise<Response> {
  const response = await fetch(resolveApiUrl(path), {
    method: "GET",
    headers: {
      Accept: "application/json"
    },
    credentials: "same-origin",
    signal
  });

  if (!response.ok) {
    throw new ApiError("The API request could not be completed.", response.status);
  }

  return response;
}

function resolveApiUrl(path: string): string {
  if (!appConfig.apiBaseUrl) {
    return path;
  }

  return appConfig.apiBaseUrl + (path.startsWith("/") ? path : "/" + path);
}
