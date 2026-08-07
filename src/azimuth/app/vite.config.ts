import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const SIDECAR = process.env.AZIMUTH_PORT || '8787';

export default defineConfig({
  plugins: [react()],
  // Relative base so the built bundle works from file:// (Tauri) as well as http.
  base: './',
  build: { outDir: 'dist', emptyOutDir: true, sourcemap: false, target: 'es2022' },
  server: {
    port: 5273,
    strictPort: true,
    proxy: { '/api': `http://127.0.0.1:${SIDECAR}` },
  },
});
