import { describe, expect, it } from 'vitest';
import { onlineMembers } from './presence';
import type { Member } from './snapshot';

const userA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const userB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const stranger = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';

const members: Member[] = [
  { userId: userA, displayName: 'TEST_USER_A', role: 'owner' },
  { userId: userB, displayName: 'TEST_USER_B', role: 'member' }
];

describe('onlineMembers', () => {
  it('lists members with their newest presence, sorted by name', () => {
    const online = onlineMembers(
      {
        [userB]: [
          { display_name: 'old', game: 'windrose', playing_world_id: null, presence_ref: '1' },
          { display_name: 'new', game: 'dragonwilds', playing_world_id: 'TEST_WORLD_ID', presence_ref: '2' }
        ],
        [userA]: [{ display_name: 'TEST_USER_A', game: 'windrose' }]
      },
      members
    );

    expect(online).toEqual([
      { userId: userA, displayName: 'TEST_USER_A', game: 'windrose', playingWorldId: null },
      { userId: userB, displayName: 'TEST_USER_B', game: 'dragonwilds', playingWorldId: 'TEST_WORLD_ID' }
    ]);
  });

  it('ignores keys that are not members and takes names from the group, not from presence', () => {
    const online = onlineMembers(
      {
        [stranger]: [{ display_name: 'TEST_STRANGER' }],
        [userB]: [{ display_name: 'Pretending to be someone else' }]
      },
      members
    );

    expect(online.map((member) => member.displayName)).toEqual(['TEST_USER_B']);
  });

  it('skips empty or malformed presence', () => {
    expect(onlineMembers({ [userA]: [], [userB]: ['text' as unknown] }, members)).toEqual([]);
  });

  it('cuts the world id to its database length', () => {
    const online = onlineMembers({ [userA]: [{ playing_world_id: 'x'.repeat(300) }] }, members);

    expect(online[0]?.playingWorldId).toHaveLength(128);
  });
});
