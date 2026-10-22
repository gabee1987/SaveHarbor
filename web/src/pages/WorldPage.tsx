import { useEffect, useState } from 'react';
import { CheckedRow, LiveBadge } from '../components/GroupParts';
import { LockLine } from '../components/WorldCard';
import { CloudError, loadHistory } from '../lib/api';
import { formatClock, formatSize } from '../lib/format';
import { gameName, type GameKey } from '../lib/games';
import { routeHash } from '../lib/router';
import { serverNow, type Version } from '../lib/snapshot';
import { useGroup, useNow } from '../lib/useGroup';

interface Props {
  groupId: string;
  game: GameKey;
  worldUuid: string;
  userId: string;
}

export function WorldPage({ groupId, game, worldUuid, userId }: Props) {
  const data = useGroup(groupId, game);
  const localNow = useNow();
  const now = data.snapshot ? serverNow(data.snapshot, localNow) : new Date(localNow);
  const world = data.snapshot?.worlds.find((item) => item.id === worldUuid) ?? null;
  const [history, setHistory] = useState<Version[] | null>(null);
  const [historyError, setHistoryError] = useState<string | null>(null);

  // Again after every refresh of the snapshot: a new version arrives as a change of the world row.
  useEffect(() => {
    let cancelled = false;
    loadHistory(worldUuid)
      .then((next) => {
        if (!cancelled) {
          setHistory(next);
          setHistoryError(null);
        }
      })
      .catch((reason: unknown) => {
        if (!cancelled) {
          setHistoryError(reason instanceof CloudError ? reason.message : 'Something went wrong. Try again.');
        }
      });
    return () => {
      cancelled = true;
    };
  }, [worldUuid, data.revision]);

  const back = routeHash({ page: 'group', groupId, game });
  if (data.snapshot && !world) {
    return (
      <section className="panel">
        <p className="muted">This world is no longer shared in the group.</p>
        <a href={back}>Back to {gameName(game)}</a>
      </section>
    );
  }

  return (
    <section>
      <a className="back" href={back}>
        ‹ {gameName(game)}
      </a>
      <div className="page-head">
        <h1>{world?.name ?? 'World'}</h1>
        <LiveBadge state={data.live} />
      </div>
      <CheckedRow checkedAt={data.checkedAt} loading={data.loading} onRefresh={data.refresh} />
      {data.error && <p className="error" role="alert">{data.error}</p>}
      {world && (
        <div className="panel">
          <h2>Right now</h2>
          <LockLine world={world} now={now} userId={userId} />
        </div>
      )}
      <div className="panel">
        <h2>Versions</h2>
        {historyError && <p className="error" role="alert">{historyError}</p>}
        {history && history.length === 0 && <p className="muted">Nothing uploaded yet.</p>}
        <ol className="versions">
          {history?.map((version) => (
            <li key={version.number} className={version.number === world?.latest?.number ? 'latest' : undefined}>
              <span className="version-number">{version.number}</span>
              <span>
                {version.uploaderName}
                {version.uploaderDevice ? ` on ${version.uploaderDevice}` : ''}
                <span className="muted small block">
                  {formatClock(version.createdAt, now)} · {formatSize(version.sizeBytes)}
                  {version.basedOnNumber > 0 && version.basedOnNumber !== version.number - 1 ? ` · started from version ${version.basedOnNumber}` : ''}
                </span>
              </span>
            </li>
          ))}
        </ol>
        {history && history.length === 20 && <p className="hint">The last 20 versions are shown.</p>}
      </div>
    </section>
  );
}
