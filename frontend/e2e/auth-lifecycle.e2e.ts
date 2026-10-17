import { expect, test } from "./fixtures/authenticated";

test("sign-in validates the token field and gives a safe error for rejected credentials", async ({
  page
}) => {
  await page.goto("/");

  await expect(
    page.getByRole("heading", { name: "Sign in" })
  ).toBeVisible();

  const tokenInput = page.getByLabel("Access token");
  const signInButton = page.getByRole("button", { name: "Sign in" });

  await expect(tokenInput).toHaveAttribute("required", "");
  await expect(signInButton).toBeDisabled();

  await tokenInput.fill("not-a-valid-access-token");
  await signInButton.click();

  await expect(page.getByRole("alert")).toHaveText(
    "The access token is invalid or expired."
  );

  await expect(tokenInput).toHaveAttribute("aria-invalid", "true");
  await expect(tokenInput).toHaveAttribute(
    "aria-describedby",
    "access-token-help auth-error"
  );
  await expect(page.getByText(/stack trace|connection string|password=/i)).toHaveCount(0);
});

test("sign out shows confirmation after clearing the session", async ({
  authenticatedPage: page
}) => {
  await page.getByRole("button", { name: "Sign out" }).click();

  await expect(
    page.getByRole("heading", { name: "Sign in" })
  ).toBeVisible();

  await expect(page.getByRole("status")).toHaveText(
    "You have signed out successfully."
  );
  await expect(page.getByLabel("Access token")).toHaveValue("");
});

test("an expired server session returns the user to sign-in with a clear message", async ({
  authenticatedPage: page
}) => {
  await page.route("**/api/auth/refresh", async (route) => {
    await route.fulfill({ status: 401, body: "" });
  });

  await page.route("**/api/conversations", async (route) => {
    if (route.request().method() === "POST") {
      await route.fulfill({ status: 401, body: "" });
      return;
    }

    await route.continue();
  });

  await page.getByRole("button", { name: "New conversation" }).click();
  await page.getByLabel("Title").fill("Expired session E2E");
  await page.getByRole("button", { name: "Create conversation" }).click();

  await expect(
    page.getByRole("heading", { name: "Sign in" })
  ).toBeVisible();

  await expect(page.getByRole("alert")).toHaveText(
    "Your session has expired. Please sign in again."
  );
});
