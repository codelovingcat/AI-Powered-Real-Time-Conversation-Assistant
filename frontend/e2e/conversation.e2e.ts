import { expect, test } from "./fixtures/authenticated";

test("requires confirmation before deleting a conversation and removes its saved data from history", async ({
  authenticatedPage: page
}) => {
  const title = "Delete conversation E2E";

  await page.getByRole("button", { name: "New conversation" }).click();
  await page.getByLabel("Title").fill(title);
  await page.getByRole("button", { name: "Create conversation" }).click();
  await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();

  const row = page.locator(".conversation-item").filter({ hasText: title });
  await row.getByRole("button", { name: "Delete " + title }).click();
  const dialog = page.getByRole("alertdialog", { name: "Delete conversation?" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByText(/saved messages and generated conversation summary/)).toBeVisible();

  await dialog.getByRole("button", { name: "Cancel" }).click();
  await expect(dialog).toHaveCount(0);
  await expect(row).toBeVisible();

  await row.getByRole("button", { name: "Delete " + title }).click();
  await expect(dialog).toBeVisible();
  const deleteResponse = page.waitForResponse((response) =>
    response.url().includes("/api/conversations/") && response.request().method() === "DELETE"
  );
  await dialog.getByRole("button", { name: "Delete permanently" }).click();
  expect((await deleteResponse).status()).toBe(204);
  await expect(row).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Select a conversation" })).toBeVisible();
});

test("renames a conversation and displays created and updated timestamps", async ({
  authenticatedPage: page
}) => {
  const originalTitle = "Rename conversation E2E";
  const renamedTitle = "Renamed conversation E2E";

  await page.getByRole("button", { name: "New conversation" }).click();
  await page.getByLabel("Title").fill(originalTitle);
  await page.getByRole("button", { name: "Create conversation" }).click();

  await expect(page.getByRole("heading", { name: originalTitle, exact: true })).toBeVisible();
  await expect(page.getByText(/^Created /).first()).toBeVisible();
  await expect(page.getByText(/^Updated /).first()).toBeVisible();

  await page.getByRole("button", { name: "Rename " + originalTitle }).click();
  await page.getByRole("textbox", { name: "Edit conversation title" }).fill(renamedTitle);
  await page.getByRole("button", { name: "Save title" }).click();

  await expect(page.getByRole("heading", { name: renamedTitle, exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: new RegExp("^" + renamedTitle + " EN → TR") })).toBeVisible();
});

test("processes a conversation through the HTTP application boundary", async ({
  authenticatedPage: page
}) => {
  const conversationTitle = `E2E conversation ${Date.now()}`;

  await page.getByRole("button", { name: "New conversation" }).click();
  await page.getByLabel("Title").fill(conversationTitle);
  await page.getByRole("button", { name: "Create conversation" }).click();

  await expect(
    page.getByRole("heading", { name: conversationTitle, exact: true })
  ).toBeVisible();

  await page.getByLabel("What did they say?").fill("Hello from the browser test.");
  const assistantResponse = page.waitForResponse((response) =>
    response.url().includes("/assistant") &&
    response.request().method() === "POST"
  );
  await page.getByRole("button", { name: "Send to assistant" }).click();
  expect((await assistantResponse).ok()).toBeTruthy();

  await expect(
    page.getByText("Deterministic E2E translation.", { exact: true }).first()
  ).toBeVisible({ timeout: 15_000 });

  await expect(
    page.getByText("This is a deterministic E2E reply.", { exact: true }).first()
  ).toBeVisible({ timeout: 15_000 });

  await page.reload();

  // Reload intentionally clears the active selection. Reopen the saved
  // conversation before asserting that its persisted messages are restored.
  const savedConversation = page.getByRole("button", {
    name: new RegExp("^" + conversationTitle + " EN → TR")
  }).first();
  await savedConversation.click();
  await expect(
    page.getByRole("heading", { name: conversationTitle, exact: true })
  ).toBeVisible();

  await expect(
    page.getByText("Deterministic E2E translation.", { exact: true }).first()
  ).toBeVisible({ timeout: 10_000 });
});

test("streams microphone audio through partial and final transcripts into AI assistance", async ({
  authenticatedPage: page
}) => {
  await page.getByRole("button", { name: "New conversation" }).click();
  await page.getByLabel("Title").fill("Live audio E2E");
  await page.getByRole("button", { name: "Create conversation" }).click();

  await expect(
    page.getByRole("heading", { name: "Live audio E2E" })
  ).toBeVisible();

  await page.getByRole("button", { name: "Start microphone" }).click();

  await expect(
    page.getByText("Could you tell me", { exact: true })
  ).toBeVisible({ timeout: 10_000 });

  await expect(
    page.getByText("Could you tell me about yourself?", { exact: true }).first()
  ).toBeVisible({ timeout: 15_000 });

  await expect(
    page.getByText("Deterministic E2E translation.", { exact: true }).first()
  ).toBeVisible({ timeout: 15_000 });

  await expect(
    page.getByText("This is a deterministic E2E reply.", { exact: true }).first()
  ).toBeVisible({ timeout: 15_000 });

  await page.getByRole("button", { name: "Pause conversation" }).click();
  await expect(page.getByRole("status").getByText("Paused", { exact: true })).toBeVisible();
});

test("starts, pauses, resumes, and finishes a conversation without duplicate actions", async ({
  authenticatedPage: page
}) => {
  await page.getByRole("button", { name: "New conversation" }).click();
  await page.getByLabel("Title").fill("Lifecycle controls E2E");
  await page.getByRole("button", { name: "Create conversation" }).click();

  await expect(
    page.getByRole("heading", { name: "Lifecycle controls E2E" })
  ).toBeVisible();
  const lifecyclePanel = page.locator(".microphone-panel");
  await expect(lifecyclePanel.getByText("Ready to start", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Start microphone" }).click();
  await expect(lifecyclePanel.getByText("Streaming", { exact: true })).toBeVisible({ timeout: 10_000 });

  await page.getByRole("button", { name: "Pause conversation" }).click();
  await expect(lifecyclePanel.getByText("Paused", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Resume conversation" }).click();
  await expect(lifecyclePanel.getByText("Streaming", { exact: true })).toBeVisible({ timeout: 10_000 });

  await page.getByRole("button", { name: "Finish conversation" }).click();
  await expect(lifecyclePanel.getByText("Finished", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Finish conversation" })).toHaveCount(0);
  await expect(page.getByText("This conversation is read-only for new assistant inputs.")).toBeVisible();
});

test("shows a recoverable error after live audio reconnection attempts fail", async ({
  authenticatedPage: page
}) => {
  // Route interception has proved unreliable through the Vite WebSocket proxy.
  // Replace only the page's WebSocket constructor with a deterministic socket
  // that closes before opening, so the component must exhaust its retry policy.
  await page.evaluate(() => {
    type E2EWindow = Window & { __e2eAudioSocketAttempts?: number };
    const testWindow = window as E2EWindow;
    testWindow.__e2eAudioSocketAttempts = 0;

    class FailingWebSocket {
      static readonly CONNECTING = 0;
      static readonly OPEN = 1;
      static readonly CLOSING = 2;
      static readonly CLOSED = 3;

      readonly url: string;
      readonly protocol = "";
      readonly extensions = "";
      readonly bufferedAmount = 0;
      binaryType: BinaryType = "blob";
      readyState = FailingWebSocket.CONNECTING;
      onopen: ((event: Event) => void) | null = null;
      onerror: ((event: Event) => void) | null = null;
      onmessage: ((event: MessageEvent) => void) | null = null;
      onclose: ((event: CloseEvent) => void) | null = null;

      constructor(url: string | URL) {
        this.url = String(url);
        testWindow.__e2eAudioSocketAttempts =
          (testWindow.__e2eAudioSocketAttempts ?? 0) + 1;

        queueMicrotask(() => {
          this.readyState = FailingWebSocket.CLOSED;
          this.onclose?.(
            new CloseEvent("close", {
              code: 1011,
              reason: "synthetic E2E disconnect",
              wasClean: false
            })
          );
        });
      }

      send(_data: unknown): void {}

      close(): void {
        this.readyState = FailingWebSocket.CLOSED;
      }
    }

    Object.defineProperty(window, "WebSocket", {
      configurable: true,
      writable: true,
      value: FailingWebSocket
    });
  });

  await page.getByRole("button", { name: "New conversation" }).click();
  await page.getByLabel("Title").fill("Audio recovery E2E");
  await page.getByRole("button", { name: "Create conversation" }).click();

  await expect(
    page.getByRole("heading", { name: "Audio recovery E2E" })
  ).toBeVisible();

  await page.getByRole("button", { name: "Start microphone" }).click();

  await expect(
    page.getByText(
      "The audio connection could not be restored. Start the microphone again.",
      { exact: true }
    )
  ).toBeVisible({ timeout: 20_000 });

  const socketAttempts = await page.evaluate(
    () => (window as Window & { __e2eAudioSocketAttempts?: number }).__e2eAudioSocketAttempts ?? 0
  );
  expect(socketAttempts).toBeGreaterThan(3);
});
