import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const backendTarget = "http://127.0.0.1:5080";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": {
        target: backendTarget,
        changeOrigin: true
      },
      "/health": {
        target: backendTarget,
        changeOrigin: true
      },
      "/ws": {
        target: backendTarget,
        ws: true,
        changeOrigin: true
      }
    }
  }
});
