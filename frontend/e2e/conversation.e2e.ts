import { expect, test } from "./fixtures/authenticated";

test("processes a conversation through the HTTP application boundary", async ({
  authenticatedPage: page
}) => {
  await page.getByRole("button", { name: "New conversation" }).click();
  await page.getByLabel("Title").fill("E2E conversation");
  await page.getByRole("button", { name: "Create conversation" }).click();

  await expect(
    page.getByRole("heading", { name: "E2E conversation" })
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

  await expect(
    page.getByText("Deterministic E2E translation.", { exact: true })
  ).toBeVisible();
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

  await page.getByRole("button", { name: "Stop streaming" }).click();
});

test("shows a recoverable error after live audio reconnection attempts fail", async ({
  authenticatedPage: page
}) => {
  await page.getByRole("button", { name: "New conversation" }).click();
  await page.getByLabel("Title").fill("Audio recovery E2E");
  await page.getByRole("button", { name: "Create conversation" }).click();

  await expect(
    page.getByRole("heading", { name: "Audio recovery E2E" })
  ).toBeVisible();

  let interceptedSocket = false;
  await page.routeWebSocket(/\/ws\/conversations\/[^/]+\/audio(?:\?.*)?$/u, (socket) => {
    interceptedSocket = true;
    socket.close({ code: 1011, reason: "synthetic E2E disconnect" });
  });

  await page.getByRole("button", { name: "Start microphone" }).click();

  await expect.poll(() => interceptedSocket, { timeout: 5_000 }).toBe(true);
  await expect(
    page.getByText(
      "The audio connection could not be restored. Start the microphone again.",
      { exact: true }
    )
  ).toBeVisible({ timeout: 20_000 });
});
