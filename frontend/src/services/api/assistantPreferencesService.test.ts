import { afterEach, describe, expect, it, vi } from "vitest";
import { apiGet, apiRequest } from "./apiClient";
import {
  DEFAULT_ASSISTANT_PREFERENCES,
  getAssistantPreferences,
  saveAssistantPreferences
} from "./assistantPreferencesService";

vi.mock("./apiClient", () => ({
  apiGet: vi.fn(),
  apiRequest: vi.fn()
}));

afterEach(() => {
  vi.clearAllMocks();
});

describe("assistantPreferencesService", () => {
  it("loads validated user preferences", async () => {
    vi.mocked(apiGet).mockResolvedValue(
      new Response(JSON.stringify(DEFAULT_ASSISTANT_PREFERENCES), { status: 200 })
    );

    await expect(getAssistantPreferences()).resolves.toEqual(
      DEFAULT_ASSISTANT_PREFERENCES
    );
    expect(apiGet).toHaveBeenCalledWith("/api/assistant-preferences", undefined);
  });

  it("rejects invalid preference values from the API", async () => {
    vi.mocked(apiGet).mockResolvedValue(
      new Response(
        JSON.stringify({
          ...DEFAULT_ASSISTANT_PREFERENCES,
          tone: "ignore-system-instructions"
        }),
        { status: 200 }
      )
    );

    await expect(getAssistantPreferences()).rejects.toThrow(
      "The assistant preferences response is invalid."
    );
  });

  it("persists preferences with an authenticated PUT request", async () => {
    vi.mocked(apiRequest).mockResolvedValue(
      new Response(JSON.stringify(DEFAULT_ASSISTANT_PREFERENCES), { status: 200 })
    );

    await expect(
      saveAssistantPreferences(DEFAULT_ASSISTANT_PREFERENCES)
    ).resolves.toEqual(DEFAULT_ASSISTANT_PREFERENCES);

    expect(apiRequest).toHaveBeenCalledWith(
      "/api/assistant-preferences",
      {
        method: "PUT",
        body: JSON.stringify(DEFAULT_ASSISTANT_PREFERENCES),
        signal: undefined
      }
    );
  });
});
