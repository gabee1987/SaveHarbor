import type { Lock } from './snapshot';

export type LockState = 'free' | 'playing' | 'yours' | 'expired';

export interface LockView {
  state: LockState;
  text: string;
  detail: string | null;
}

// What a world's session lock means right now. `now` is the server's clock (see serverNow), so a phone with a wrong
// clock still shows an expired lock as free, exactly like the desktop app decides it.
export function lockText(lock: Lock | null, now: Date, ownUserId: string | null, formatTime: (date: Date) => string): LockView {
  if (!lock) {
    return { state: 'free', text: 'Free', detail: null };
  }

  if (lock.expiresAt.getTime() <= now.getTime()) {
    return {
      state: 'expired',
      text: 'Free',
      detail: `${lock.playerName}'s last session did not end properly; its lock ran out at ${formatTime(lock.expiresAt)}.`
    };
  }

  const held = `Held until ${formatTime(lock.expiresAt)}; extended while the game runs.`;
  if (lock.userId === ownUserId) {
    return {
      state: 'yours',
      text: `You are playing since ${formatTime(lock.startedAt)}`,
      detail: lock.deviceLabel ? `On ${lock.deviceLabel}. ${held}` : held
    };
  }

  return { state: 'playing', text: `${lock.playerName} is playing since ${formatTime(lock.startedAt)}`, detail: held };
}
