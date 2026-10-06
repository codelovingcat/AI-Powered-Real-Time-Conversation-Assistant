import { createHmac, randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";

const E2E_USER_ID = "11111111-1111-1111-1111-111111111111";
const E2E_ISSUER = "Conversa.E2E";
const E2E_AUDIENCE = "Conversa.E2E";

function base64Url(value: string | Buffer): string {
  return Buffer.from(value)
    .toString("base64")
    .replaceAll("+", "-")
    .replaceAll("/", "_")
    .replace(/=+$/u, "");
}

function createTestAccessToken(): string {
  const signingKey = process.env.E2E_AUTH_SIGNING_KEY?.trim();

  if (!signingKey) {
    throw new Error("E2E_AUTH_SIGNING_KEY is required.");
  }

  const now = Math.floor(Date.now() / 1000);
  const header = base64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = base64Url(
    JSON.stringify({
      sub: E2E_USER_ID,
      iss: E2E_ISSUER,
      aud: E2E_AUDIENCE,
      jti: randomUUID().replaceAll("-", ""),
      nbf: now - 5,
      exp: now + 300
    })
  );

  const unsignedToken = header + "." + payload;
  const signature = createHmac("sha256", Buffer.from(signingKey, "base64"))
    .update(unsignedToken)
    .digest();

  return unsignedToken + "." + base64Url(signature);
}

test("authenticates and processes a conversation through the HTTP boundary", async ({
  page
}) => {
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();

  await page.getByLabel("Access token").fill(createTestAccessToken());
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("heading", { name: "Conversa" })).toBeVisible();

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
