import { CloudError } from './api';
import { client, project } from './supabase';

// Google sign-in on the page (PKCE): SaveHarbor Cloud sends the person to Google and back to this page with a one-time
// code in the query; the verifier that makes the code usable stays in this browser. The router owns the part after the
// #, so the code is read from the query and taken out of the address before anything else runs.
export type OAuthReturn = { code: string } | { error: string } | null;

export function readOAuthReturn(search: string): OAuthReturn {
  const params = new URLSearchParams(search);
  const code = params.get('code');
  if (code && code.length <= 512) {
    return { code };
  }

  const error = params.get('error');
  if (error) {
    return { error: /^[a-z_]{1,64}$/.test(error) ? error : 'other' };
  }

  return null;
}

// The page's own address without query or route, the exact form on the cloud's redirect allow-list.
export function returnAddress(location: { origin: string; pathname: string }): string {
  return location.origin + location.pathname;
}

const unfinished = 'Google sign-in did not finish. Try again, or use your e-mail address.';

// Whether the cloud has Google sign-in on, from its public auth settings; false when it cannot be asked.
export async function googleSignInAvailable(): Promise<boolean> {
  if (!project) {
    return false;
  }

  try {
    const response = await fetch(`${project.url}/auth/v1/settings`, { headers: { apikey: project.key } });
    const settings: unknown = response.ok ? await response.json() : null;
    return (settings as { external?: { google?: unknown } } | null)?.external?.google === true;
  } catch {
    return false;
  }
}

export async function startGoogleSignIn(): Promise<void> {
  const { error } = await client().auth.signInWithOAuth({
    provider: 'google',
    // select_account: a person with several Google accounts, or on a shared device, picks the right one.
    options: { redirectTo: returnAddress(window.location), queryParams: { prompt: 'select_account' } }
  });
  if (error) {
    throw new CloudError(unfinished);
  }
}

// Finishes a Google sign-in when the page is opened by the redirect; returns what to tell the person, if anything.
export async function finishGoogleSignIn(): Promise<string | null> {
  const back = readOAuthReturn(window.location.search);
  if (!back) {
    return null;
  }

  window.history.replaceState(null, '', window.location.pathname + window.location.hash);
  if ('error' in back) {
    return back.error === 'access_denied' ? 'Google sign-in was cancelled.' : unfinished;
  }

  const { error } = await client().auth.exchangeCodeForSession(back.code);
  return error ? unfinished : null;
}
