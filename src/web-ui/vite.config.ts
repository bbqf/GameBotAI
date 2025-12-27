import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import dns from 'node:dns';

dns.setDefaultResultOrder('verbatim')

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'VITE_');

  return {
    plugins: [react()],
    define: {
      __API_BASE_URL__: JSON.stringify(env.VITE_API_BASE_URL ?? '')
    },
    server: {
      port: 5173,
      strictPort: false
    }
  };
});