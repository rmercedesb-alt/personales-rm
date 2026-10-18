import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5176,            // V6 (v5 usa 5175)
    host: true,            // escucha en todas las interfaces (LAN y Tailscale)
    allowedHosts: true,    // acepta el nombre/IP de Tailscale (100.x.y.z o magicDNS)
    proxy: {
      '/api': {
        target: 'http://localhost:5005',   // V6 API (v5 usa 5003)
        changeOrigin: true
      }
    }
  }
})
