import { apiGet } from "./apiClient";

export type BackendHealth =
  | { status: "available"; detail: string }
  | { status: "unavailable"; detail: string };

export async function checkBackendHealth(signal?: AbortSignal): Promise<BackendHealth> {
  try {
    const response = await apiGet("/health", signal);
    const detail = (await response.text()).trim();

    return {
      status: "available",
      detail: detail || "Healthy"
    };
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw error;
    }

    return {
      status: "unavailable",
      detail: "Backend connection is unavailable."
    };
  }
}
