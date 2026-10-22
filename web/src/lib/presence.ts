import type { Member } from './snapshot';

export interface OnlineMember {
  userId: string;
  displayName: string;
  game: string;
  playingWorldId: string | null;
}

// Who has SaveHarbor open, from the channel's presence state (key = user id, newest meta last). Presence is sent by
// other apps, so only keys of current members count and names come from the group, not from the presence payload.
export function onlineMembers(state: Record<string, unknown[]>, members: Member[]): OnlineMember[] {
  const byId = new Map(members.map((member) => [member.userId, member]));
  const online: OnlineMember[] = [];
  for (const [key, metas] of Object.entries(state)) {
    const member = byId.get(key);
    const meta = Array.isArray(metas) ? metas[metas.length - 1] : undefined;
    if (!member || typeof meta !== 'object' || meta === null) {
      continue;
    }

    const values = meta as Record<string, unknown>;
    online.push({
      userId: member.userId,
      displayName: member.displayName,
      game: cut(values.game, 32),
      playingWorldId: cut(values.playing_world_id, 128) || null
    });
  }

  return online.sort((a, b) => a.displayName.localeCompare(b.displayName));
}

function cut(value: unknown, maxLength: number): string {
  return typeof value === 'string' ? value.trim().slice(0, maxLength) : '';
}
