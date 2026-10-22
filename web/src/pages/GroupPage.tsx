import { CheckedRow, GameTabs, InviteCard, LiveBadge, OnlineLine, useGroupSummary } from '../components/GroupParts';
import { WorldCard } from '../components/WorldCard';
import { gameName, type GameKey } from '../lib/games';
import { routeHash } from '../lib/router';
import { serverNow } from '../lib/snapshot';
import { useGroup, useNow } from '../lib/useGroup';

export function GroupPage({ groupId, game, userId }: { groupId: string; game: GameKey; userId: string }) {
  const { group, error: groupError } = useGroupSummary(groupId);
  const data = useGroup(groupId, game);
  const localNow = useNow();
  const now = data.snapshot ? serverNow(data.snapshot, localNow) : new Date(localNow);
  const worlds = data.snapshot?.worlds ?? [];
  const others = data.online.filter((member) => member.userId !== userId);
  const worldName = (worldId: string) => worlds.find((world) => world.worldId === worldId)?.name ?? null;

  if (groupError) {
    return (
      <section className="panel">
        <p className="error" role="alert">{groupError}</p>
        <a href="#/">Back to your groups</a>
      </section>
    );
  }

  return (
    <section>
      <div className="page-head">
        <h1>{group?.name ?? 'Group'}</h1>
        <LiveBadge state={data.live} />
      </div>
      <GameTabs groupId={groupId} game={game} />
      <OnlineLine online={others} worldName={worldName} />
      <CheckedRow checkedAt={data.checkedAt} loading={data.loading} onRefresh={data.refresh} />
      {data.error && <p className="error" role="alert">{data.error}</p>}
      {data.snapshot && worlds.length === 0 && <p className="muted">No {gameName(game)} worlds are shared in this group yet.</p>}
      <ul className="cards">
        {worlds.map((world) => (
          <WorldCard
            key={world.id}
            world={world}
            now={now}
            userId={userId}
            href={routeHash({ page: 'world', groupId, game, worldUuid: world.id })}
          />
        ))}
      </ul>
      {group?.isOwner && <InviteCard groupId={groupId} />}
    </section>
  );
}
