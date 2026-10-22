import { useSyncExternalStore } from 'react';
import { isGameKey, type GameKey } from './games';

// A hash router: GitHub Pages serves one index.html, so every route lives after the #.
//   #/                                 groups
//   #/g/<group>/<game>                 one game of a group
//   #/g/<group>/<game>/<world uuid>    one world's versions
export type Route =
  | { page: 'groups' }
  | { page: 'group'; groupId: string; game: GameKey }
  | { page: 'world'; groupId: string; game: GameKey; worldUuid: string };

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// Anything that is not a known route opens the group list; ids are checked before they reach a query.
export function parseRoute(hash: string): Route {
  const [first, groupId, game = 'windrose', worldUuid, ...rest] = hash.replace(/^#\/?/, '').split('/').filter(Boolean);
  if (first !== 'g' || !groupId || !uuid.test(groupId) || !isGameKey(game) || rest.length > 0) {
    return { page: 'groups' };
  }

  if (worldUuid === undefined) {
    return { page: 'group', groupId, game };
  }

  return uuid.test(worldUuid) ? { page: 'world', groupId, game, worldUuid } : { page: 'group', groupId, game };
}

export function routeHash(route: Route): string {
  switch (route.page) {
    case 'groups':
      return '#/';
    case 'group':
      return `#/g/${route.groupId}/${route.game}`;
    case 'world':
      return `#/g/${route.groupId}/${route.game}/${route.worldUuid}`;
  }
}

// replace: true keeps the browser's back button from returning to a page that only forwarded.
export function navigate(route: Route, replace = false): void {
  const hash = routeHash(route);
  if (replace) {
    window.history.replaceState(null, '', hash);
    window.dispatchEvent(new HashChangeEvent('hashchange'));
  } else {
    window.location.hash = hash;
  }
}

function subscribe(onChange: () => void): () => void {
  window.addEventListener('hashchange', onChange);
  return () => window.removeEventListener('hashchange', onChange);
}

export function useRoute(): Route {
  const hash = useSyncExternalStore(subscribe, () => window.location.hash);
  return parseRoute(hash);
}
