/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// In production Nginx serves the build and proxies /api; the dev server mirrors that proxy.
export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: {
      '/api': { target: 'http://localhost:8080', changeOrigin: true }
    }
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts']
  }
})
