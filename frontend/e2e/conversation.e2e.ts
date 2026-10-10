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
  await page.getByRole("button", { name: "Send to assistant" }).click();

  await expect(
    page.getByText("Deterministic E2E translation.", { exact: true })
  ).toBeVisible();

  await expect(
    page.getByText("This is a deterministic E2E reply.", { exact: true })
  ).toBeVisible();

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
    page.getByText("Could you tell me about yourself?", { exact: true })
  ).toBeVisible({ timeout: 10_000 });

  await expect(
    page.getByText("Deterministic E2E translation.", { exact: true })
  ).toBeVisible({ timeout: 10_000 });

  await expect(
    page.getByText("This is a deterministic E2E reply.", { exact: true })
  ).toBeVisible({ timeout: 10_000 });

  await page.getByRole("button", { name: "Stop streaming" }).click();
});
