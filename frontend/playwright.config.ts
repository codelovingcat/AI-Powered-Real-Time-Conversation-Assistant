import { defineConfig, devices } from "@playwright/test";

const webBaseUrl = "http://127.0.0.1:5173";

export default defineConfig({
  testDir: "./e2e",
  testMatch: /.*\.e2e\.ts$/u,
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI
    ? [["github"], ["html", { open: "never" }]]
    : "list",
  use: {
    baseURL: webBaseUrl,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure"
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] }
    }
  ],
  webServer: {
    command: "npm run dev -- --host 127.0.0.1",
    url: webBaseUrl,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000
  }
});
