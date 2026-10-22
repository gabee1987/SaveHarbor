import { describe, expect, it } from 'vitest';
import { contentSecurityPolicy } from './csp';

describe('contentSecurityPolicy', () => {
  it('allows only the project over https and wss', () => {
    const policy = contentSecurityPolicy('https://TEST_REF.supabase.co');

    expect(policy).toContain('connect-src https://test_ref.supabase.co wss://test_ref.supabase.co');
    expect(policy).toContain("script-src 'self'");
    expect(policy).not.toContain('unsafe');
  });

  it('allows the local stack over http and ws', () => {
    expect(contentSecurityPolicy('http://127.0.0.1:54321')).toContain('connect-src http://127.0.0.1:54321 ws://127.0.0.1:54321');
  });

  it.each(['http://TEST_REF.supabase.co', 'ftp://127.0.0.1', 'not a url'])('refuses %s', (url) => {
    expect(() => contentSecurityPolicy(url)).toThrow();
  });
});
