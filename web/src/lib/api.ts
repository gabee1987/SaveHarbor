import type { GameKey } from './games';
import { mapSnapshot, mapVersions, isUuid, type GroupSnapshot, type Version } from './snapshot';
import { client } from './supabase';

export interface GroupSummary {
  id: string;
  name: string;
  memberCount: number;
  isOwner: boolean;
}

// A message the page can show as it is. Server details never reach the screen or the console.
export class CloudError extends Error {}

const errorTexts: Record<string, string> = {
  not_signed_in: 'Your sign-in ran out. Sign in again.',
  not_member: 'You are not a member of this group any more.',
  not_owner: 'Only the group owner can see the invite code.'
};

function fail(error: { message?: string; status?: number } | null, fallback: string): never {
  const code = error?.message ?? '';
  if (error?.status === 429) {
    throw new CloudError('Too many requests. Wait a minute, then try again.');
  }

  throw new CloudError(errorTexts[code] ?? fallback);
}

const unreachable = 'Could not reach SaveHarbor Cloud. Check the connection and try again.';

// No answer, or the service failed: not the person's mistake.
function isOutage(error: { status?: number }): boolean {
  return !error.status || error.status >= 500;
}

// No account is created from the web page, and the answer is the same whether or not the address has one.
export async function requestCode(email: string): Promise<void> {
  const { error } = await client().auth.signInWithOtp({ email, options: { shouldCreateUser: false } });
  if (error && (error.status === 429 || isOutage(error))) {
    fail(error, unreachable);
  }
}

export async function verifyCode(email: string, code: string): Promise<void> {
  const { error } = await client().auth.verifyOtp({ email, token: code, type: 'email' });
  if (error) {
    fail(error.status === 429 ? error : null, isOutage(error) ? unreachable : 'That code did not work. Check it, or ask for a new one.');
  }
}

export async function signOut(): Promise<void> {
  await client().auth.signOut({ scope: 'local' });
}

export async function loadGroups(): Promise<GroupSummary[]> {
  const { data, error } = await client().from('group_summaries').select('id, name, member_count, my_role').order('name');
  if (error || !Array.isArray(data)) {
    fail(error, unreachable);
  }

  return data
    .filter((row) => isUuid(row.id) && typeof row.name === 'string')
    .map((row) => ({
      id: row.id as string,
      name: (row.name as string).slice(0, 60),
      memberCount: typeof row.member_count === 'number' ? row.member_count : 0,
      isOwner: row.my_role === 'owner'
    }));
}

export async function loadSnapshot(groupId: string, game: GameKey): Promise<GroupSnapshot> {
  const { data, error } = await client().rpc('get_group_snapshot', { p_group: groupId, p_game: game });
  const snapshot = error ? null : mapSnapshot(data, Date.now());
  if (!snapshot) {
    fail(error, unreachable);
  }

  return snapshot;
}

// The last 20 versions, newest first; only the columns the page shows.
export async function loadHistory(worldUuid: string): Promise<Version[]> {
  const { data, error } = await client()
    .from('world_versions')
    .select('version_number, uploader_name, uploader_device, archive_size_bytes, based_on_version_number, created_at')
    .eq('world_uuid', worldUuid)
    .order('version_number', { ascending: false })
    .limit(20);
  if (error) {
    fail(error, unreachable);
  }

  return mapVersions(data);
}

export async function loadInviteCode(groupId: string): Promise<string> {
  const { data, error } = await client().rpc('get_invite_code', { p_group: groupId });
  if (error || typeof data !== 'string' || !/^[A-Z2-9]{10}$/.test(data)) {
    fail(error, unreachable);
  }

  return data;
}
