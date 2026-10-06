import { describe, expect, it, vi } from "vitest";
import { copyText } from "./copyText";

describe("copyText", () => {
  it("uses the Clipboard API when available", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);

    vi.stubGlobal("navigator", { clipboard: { writeText } });

    await expect(copyText("Yes, please.")).resolves.toBeUndefined();
    expect(writeText).toHaveBeenCalledWith("Yes, please.");
  });

  it("rejects empty text before accessing the clipboard", async () => {
    await expect(copyText("")).rejects.toThrow("Nothing to copy.");
  });
});
