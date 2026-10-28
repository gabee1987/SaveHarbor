// The games a group can share. Keys equal GameIdExtensions.ToStorageKey in the desktop app and public.games rows.
export const games = [
  { key: 'windrose', name: 'Windrose' },
  { key: 'dragonwilds', name: 'RuneScape: Dragonwilds' },
  { key: 'enshrouded', name: 'Enshrouded' }
] as const;

export type GameKey = (typeof games)[number]['key'];

export function isGameKey(value: string): value is GameKey {
  return games.some((game) => game.key === value);
}

export function gameName(key: GameKey): string {
  return games.find((game) => game.key === key)?.name ?? key;
}
