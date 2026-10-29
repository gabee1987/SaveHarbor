import { describe, expect, it } from 'vitest';
import { parseRoute, routeHash, type Route } from './router';

const group = '11111111-1111-4111-8111-111111111111';
const world = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';

describe('parseRoute', () => {
  it.each<[string, Route]>([
    ['', { page: 'groups' }],
    ['#/', { page: 'groups' }],
    [`#/g/${group}`, { page: 'group', groupId: group, game: 'windrose' }],
    [`#/g/${group}/dragonwilds`, { page: 'group', groupId: group, game: 'dragonwilds' }],
    [`#/g/${group}/enshrouded`, { page: 'group', groupId: group, game: 'enshrouded' }],
    [`#/g/${group}/windrose/${world}`, { page: 'world', groupId: group, game: 'windrose', worldUuid: world }]
  ])('%s', (hash, route) => {
    expect(parseRoute(hash)).toEqual(route);
  });

  it.each([
    '#/g/not-a-uuid',
    `#/g/${group}/unknown-game`,
    `#/g/${group}/windrose/${world}/extra`,
    `#/x/${group}`,
    "#/g/1' or '1'='1"
  ])('opens the group list for %s', (hash) => {
    expect(parseRoute(hash)).toEqual({ page: 'groups' });
  });

  it('stays on the group for a world id that is not a uuid', () => {
    expect(parseRoute(`#/g/${group}/windrose/TEST_WORLD_ID`)).toEqual({ page: 'group', groupId: group, game: 'windrose' });
  });
});

describe('routeHash', () => {
  it('round-trips every route', () => {
    const routes: Route[] = [
      { page: 'groups' },
      { page: 'group', groupId: group, game: 'dragonwilds' },
      { page: 'group', groupId: group, game: 'enshrouded' },
      { page: 'world', groupId: group, game: 'windrose', worldUuid: world }
    ];

    for (const route of routes) {
      expect(parseRoute(routeHash(route))).toEqual(route);
    }
  });
});
