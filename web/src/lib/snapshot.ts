// What get_group_snapshot and world_versions return, read defensively: a field of the wrong type drops that entry
// instead of breaking the page, and every text is cut to the length the database allows for it.

export interface Member {
  userId: string;
  displayName: string;
  role: 'owner' | 'member';
}

export interface Version {
  number: number;
  uploaderName: string;
  uploaderDevice: string;
  createdAt: Date;
  sizeBytes: number;
  basedOnNumber: number;
}

export interface Lock {
  userId: string;
  playerName: string;
  deviceLabel: string;
  startedAt: Date;
  expiresAt: Date;
}

export interface World {
  id: string;
  // The game's own id of the world; presence names the world a friend is playing by it.
  worldId: string;
  name: string;
  latest: Version | null;
  lock: Lock | null;
}

export interface GroupSnapshot {
  serverTime: Date;
  // Server clock minus this device's clock when the snapshot arrived, so lock times are compared on server time.
  clockOffsetMs: number;
  members: Member[];
  worlds: World[];
}

type Json = Record<string, unknown>;

export function mapSnapshot(raw: unknown, receivedAtMs: number): GroupSnapshot | null {
  const serverTime = isObject(raw) ? date(raw, 'server_time') : null;
  if (!isObject(raw) || !serverTime) {
    return null;
  }

  return {
    serverTime,
    clockOffsetMs: serverTime.getTime() - receivedAtMs,
    members: list(raw.members, mapMember),
    worlds: list(raw.worlds, mapWorld)
  };
}

// The server's clock now, estimated from this device's clock and the offset seen with the snapshot.
export function serverNow(snapshot: GroupSnapshot, localNowMs: number): Date {
  return new Date(localNowMs + snapshot.clockOffsetMs);
}

export function mapVersion(raw: unknown): Version | null {
  if (!isObject(raw)) {
    return null;
  }

  const number = integer(raw, 'version_number');
  const createdAt = date(raw, 'created_at');
  if (number === null || number <= 0 || !createdAt) {
    return null;
  }

  return {
    number,
    uploaderName: text(raw, 'uploader_name', 32) || 'Someone',
    uploaderDevice: text(raw, 'uploader_device', 64),
    createdAt,
    sizeBytes: Math.max(0, integer(raw, 'archive_size_bytes') ?? 0),
    basedOnNumber: Math.max(0, integer(raw, 'based_on_version_number') ?? 0)
  };
}

export function mapVersions(raw: unknown): Version[] {
  return list(raw, mapVersion);
}

function mapMember(raw: unknown): Member | null {
  if (!isObject(raw) || !isUuid(raw.user_id)) {
    return null;
  }

  return {
    userId: raw.user_id,
    displayName: text(raw, 'display_name', 32) || 'Member',
    role: raw.role === 'owner' ? 'owner' : 'member'
  };
}

function mapWorld(raw: unknown): World | null {
  if (!isObject(raw) || !isUuid(raw.id)) {
    return null;
  }

  return {
    id: raw.id,
    worldId: text(raw, 'world_id', 128),
    name: text(raw, 'world_name', 120) || text(raw, 'world_id', 128) || 'World',
    latest: mapVersion(raw.latest),
    lock: mapLock(raw.lock)
  };
}

function mapLock(raw: unknown): Lock | null {
  if (!isObject(raw) || !isUuid(raw.user_id)) {
    return null;
  }

  const startedAt = date(raw, 'started_at');
  const expiresAt = date(raw, 'expires_at');
  if (!startedAt || !expiresAt) {
    return null;
  }

  return {
    userId: raw.user_id,
    playerName: text(raw, 'player_name', 32) || 'A friend',
    deviceLabel: text(raw, 'device_label', 64),
    startedAt,
    expiresAt
  };
}

function list<T>(raw: unknown, map: (item: unknown) => T | null): T[] {
  return Array.isArray(raw) ? raw.map(map).filter((item): item is T => item !== null) : [];
}

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isUuid(value: unknown): value is string {
  return typeof value === 'string' && uuid.test(value);
}

function text(raw: Json, key: string, maxLength: number): string {
  const value = raw[key];
  return typeof value === 'string' ? value.trim().slice(0, maxLength) : '';
}

function integer(raw: Json, key: string): number | null {
  const value = raw[key];
  return typeof value === 'number' && Number.isSafeInteger(value) ? value : null;
}

function date(raw: Json, key: string): Date | null {
  const value = raw[key];
  if (typeof value !== 'string') {
    return null;
  }

  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}
