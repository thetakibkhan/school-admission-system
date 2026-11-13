import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// Dev server proxies the API so the browser never needs CORS in development.
// The container build serves the static bundle from nginx and talks to the API
// through VITE_API_BASE_URL (see .env.example and the report).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/classes": "http://localhost:8080",
      "/holds": "http://localhost:8080",
      "/me": "http://localhost:8080",
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./vitest.setup.ts"],
    css: false,
  },
});
