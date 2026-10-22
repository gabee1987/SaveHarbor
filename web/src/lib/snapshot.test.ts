import { describe, expect, it } from 'vitest';
import { mapSnapshot, mapVersions, serverNow } from './snapshot';

const userA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const userB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const worldUuid = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';

// The shape get_group_snapshot returns, with placeholder data.
function snapshot(overrides: Record<string, unknown> = {}) {
  return {
    server_time: '2026-10-22T18:00:00.000Z',
    members: [
      { user_id: userA, display_name: 'TEST_USER_A', role: 'owner' },
      { user_id: userB, display_name: 'TEST_USER_B', role: 'member' }
    ],
    worlds: [
      {
        id: worldUuid,
        world_id: 'TEST_WORLD_ID',
        world_name: 'TEST_WORLD',
        latest_version_number: 3,
        latest: {
          version_number: 3,
          uploader_name: 'TEST_USER_B',
          uploader_device: 'TEST_DEVICE',
          archive_size_bytes: 12345,
          based_on_version_number: 2,
          archive_sha256: '0'.repeat(64),
          created_at: '2026-10-22T17:30:00.000Z'
        },
        lock: {
          world_uuid: worldUuid,
          user_id: userB,
          player_name: 'TEST_USER_B',
          device_label: 'TEST_DEVICE',
          started_at: '2026-10-22T17:45:00.000Z',
          expires_at: '2026-10-22T18:25:00.000Z'
        }
      }
    ],
    ...overrides
  };
}

describe('mapSnapshot', () => {
  it('maps members, worlds, the latest version and the lock', () => {
    const mapped = mapSnapshot(snapshot(), Date.parse('2026-10-22T18:00:05.000Z'));

    expect(mapped?.members).toEqual([
      { userId: userA, displayName: 'TEST_USER_A', role: 'owner' },
      { userId: userB, displayName: 'TEST_USER_B', role: 'member' }
    ]);
    const world = mapped?.worlds[0];
    expect(world?.name).toBe('TEST_WORLD');
    expect(world?.worldId).toBe('TEST_WORLD_ID');
    expect(world?.latest).toMatchObject({ number: 3, uploaderName: 'TEST_USER_B', sizeBytes: 12345, basedOnNumber: 2 });
    expect(world?.lock?.playerName).toBe('TEST_USER_B');
    expect(world?.lock?.expiresAt.toISOString()).toBe('2026-10-22T18:25:00.000Z');
  });

  it('keeps the server clock offset, so lock times are compared on server time', () => {
    const mapped = mapSnapshot(snapshot(), Date.parse('2026-10-22T18:00:05.000Z'));

    expect(mapped?.clockOffsetMs).toBe(-5000);
    expect(serverNow(mapped!, Date.parse('2026-10-22T18:01:05.000Z')).toISOString()).toBe('2026-10-22T18:01:00.000Z');
  });

  it('returns null for something that is not a snapshot', () => {
    expect(mapSnapshot(null, 0)).toBeNull();
    expect(mapSnapshot([], 0)).toBeNull();
    expect(mapSnapshot({ worlds: [] }, 0)).toBeNull();
    expect(mapSnapshot({ server_time: 'not a date' }, 0)).toBeNull();
  });

  it('drops entries of the wrong shape instead of failing', () => {
    const mapped = mapSnapshot(
      snapshot({
        members: [{ user_id: 'not-a-uuid', display_name: 'X' }, 'text', { user_id: userA, display_name: 'TEST_USER_A' }],
        worlds: [{ id: 12345 }, { id: worldUuid, world_id: 'TEST_WORLD_ID', latest: { version_number: 'one' }, lock: { user_id: userB } }]
      }),
      0
    );

    expect(mapped?.members.map((member) => member.userId)).toEqual([userA]);
    expect(mapped?.worlds).toHaveLength(1);
    expect(mapped?.worlds[0]?.name).toBe('TEST_WORLD_ID');
    expect(mapped?.worlds[0]?.latest).toBeNull();
    expect(mapped?.worlds[0]?.lock).toBeNull();
  });

  it('cuts texts to the lengths the database allows', () => {
    const mapped = mapSnapshot(snapshot({ members: [{ user_id: userA, display_name: `  ${'x'.repeat(50)}  `, role: 'admin' }] }), 0);

    expect(mapped?.members[0]?.displayName).toHaveLength(32);
    expect(mapped?.members[0]?.role).toBe('member');
  });

  it('treats an empty world list as no worlds', () => {
    expect(mapSnapshot(snapshot({ worlds: null }), 0)?.worlds).toEqual([]);
  });
});

describe('mapVersions', () => {
  it('maps history rows and drops invalid ones', () => {
    const rows = [
      { version_number: 2, uploader_name: 'TEST_USER_A', uploader_device: '', archive_size_bytes: 10, based_on_version_number: 1, created_at: '2026-10-21T10:00:00Z' },
      { version_number: 0, created_at: '2026-10-21T10:00:00Z' },
      { version_number: 1, uploader_name: '', created_at: 'yesterday' }
    ];

    const versions = mapVersions(rows);

    expect(versions).toHaveLength(1);
    expect(versions[0]).toMatchObject({ number: 2, uploaderName: 'TEST_USER_A', uploaderDevice: '', basedOnNumber: 1 });
    expect(mapVersions(null)).toEqual([]);
  });
});
