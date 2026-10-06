import { createHmac, randomUUID } from "node:crypto";
import { expect, test as base } from "@playwright/test";
import type { Page } from "@playwright/test";

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

type E2EFixtures = {
  authenticatedPage: Page;
};

export const test = base.extend<E2EFixtures>({
  authenticatedPage: async ({ page }, use) => {
    await page.goto("/");
    await expect(
      page.getByRole("heading", { name: "Sign in" })
    ).toBeVisible();

    await page.getByLabel("Access token").fill(createTestAccessToken());
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(
      page.getByRole("heading", { name: "Conversa" })
    ).toBeVisible();

    await use(page);
  }
});

export { expect };
