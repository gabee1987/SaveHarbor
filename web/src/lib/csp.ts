// The Content-Security-Policy of the built page. Only https projects are allowed, plus the local Supabase stack over
// http on this computer for development. No inline scripts or styles: React sets no style attributes here.
export function contentSecurityPolicy(supabaseUrl: string): string {
  const project = new URL(supabaseUrl);
  const local = project.protocol === 'http:' && (project.hostname === '127.0.0.1' || project.hostname === 'localhost');
  if (project.protocol !== 'https:' && !local) {
    throw new Error('VITE_SUPABASE_URL must be an https address.');
  }

  const socket = `${local ? 'ws' : 'wss'}://${project.host}`;
  return [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self'",
    "img-src 'self' data:",
    `connect-src ${project.origin} ${socket}`,
    "base-uri 'self'",
    "form-action 'none'",
    "object-src 'none'"
  ].join('; ');
}
