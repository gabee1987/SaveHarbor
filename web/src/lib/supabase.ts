import { createClient, type SupabaseClient } from '@supabase/supabase-js';

// The publishable key and the project address are public; the database's row-level security decides what each
// signed-in member may read. The session stays in this browser's localStorage (supabase-js default).
const url = import.meta.env.VITE_SUPABASE_URL;
const key = import.meta.env.VITE_SUPABASE_PUBLISHABLE_KEY;

export const project = url && key ? { url: url.replace(/\/+$/, ''), key } : null;

export const supabase: SupabaseClient | null =
  url && key
    ? createClient(url, key, {
        auth: {
          persistSession: true,
          autoRefreshToken: true,
          // A Google sign-in comes back with a one-time code in the query (PKCE), never with tokens in the address. The
          // page exchanges it itself (lib/oauth.ts) because the router owns the part after the #.
          flowType: 'pkce',
          detectSessionInUrl: false
        }
      })
    : null;

export function client(): SupabaseClient {
  if (!supabase) {
    throw new Error('SaveHarbor Cloud is not configured for this page.');
  }

  return supabase;
}
