import { describe, expect, it } from 'vitest';
import { lockText } from './lockText';
import type { Lock } from './snapshot';

const userA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const userB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const clock = (date: Date) => date.toISOString().slice(11, 16);

const lock: Lock = {
  userId: userB,
  playerName: 'TEST_USER_B',
  deviceLabel: 'TEST_DEVICE',
  startedAt: new Date('2026-10-22T20:12:00Z'),
  expiresAt: new Date('2026-10-22T20:52:00Z')
};

describe('lockText', () => {
  it('says free without a lock', () => {
    expect(lockText(null, new Date('2026-10-22T20:30:00Z'), userA, clock)).toEqual({ state: 'free', text: 'Free', detail: null });
  });

  it("names the friend who plays and until when the lock holds", () => {
    const view = lockText(lock, new Date('2026-10-22T20:30:00Z'), userA, clock);

    expect(view.state).toBe('playing');
    expect(view.text).toBe('TEST_USER_B is playing since 20:12');
    expect(view.detail).toContain('Held until 20:52');
  });

  it('says you when the lock is your own, with the device', () => {
    const view = lockText(lock, new Date('2026-10-22T20:30:00Z'), userB, clock);

    expect(view.state).toBe('yours');
    expect(view.text).toBe('You are playing since 20:12');
    expect(view.detail).toMatch(/^On TEST_DEVICE\. Held until 20:52/);
  });

  it('shows an expired lock as free, on the server clock', () => {
    const view = lockText(lock, new Date('2026-10-22T20:52:00Z'), userA, clock);

    expect(view.state).toBe('expired');
    expect(view.text).toBe('Free');
    expect(view.detail).toContain('ran out at 20:52');
  });

  it('leaves the device out when the lock has none', () => {
    const view = lockText({ ...lock, deviceLabel: '' }, new Date('2026-10-22T20:30:00Z'), userB, clock);

    expect(view.detail).toMatch(/^Held until/);
  });
});
