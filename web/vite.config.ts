/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv, type Plugin } from 'vite';
import { contentSecurityPolicy } from './src/lib/csp.ts';

// The built page may talk only to its own origin and the SaveHarbor Cloud project it was built for. The policy goes
// into the HTML at build time only: the dev server injects inline scripts that a strict policy would block.
function securityHeaders(supabaseUrl: string | undefined): Plugin {
  return {
    name: 'saveharbor-security-headers',
    apply: 'build',
    transformIndexHtml() {
      if (!supabaseUrl) {
        throw new Error('Set VITE_SUPABASE_URL (see web/.env.example) before building the page.');
      }

      return [{ tag: 'meta', attrs: { 'http-equiv': 'Content-Security-Policy', content: contentSecurityPolicy(supabaseUrl) }, injectTo: 'head-prepend' }];
    }
  };
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'VITE_');
  return {
    // GitHub Pages serves the repository's page under /SaveHarbor/.
    base: '/SaveHarbor/',
    plugins: [react(), securityHeaders(env.VITE_SUPABASE_URL)],
    build: { target: 'es2022', sourcemap: false },
    test: { environment: 'node', include: ['src/**/*.test.ts'] }
  };
});
