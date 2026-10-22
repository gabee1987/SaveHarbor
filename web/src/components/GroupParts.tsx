import { useEffect, useState } from 'react';
import { CloudError, loadGroups, loadInviteCode, type GroupSummary } from '../lib/api';
import { formatClock } from '../lib/format';
import { games, type GameKey } from '../lib/games';
import type { LiveState } from '../lib/live';
import type { OnlineMember } from '../lib/presence';
import { routeHash } from '../lib/router';

const liveTexts: Record<LiveState, { text: string; title: string }> = {
  live: { text: 'Live', title: "Friends' changes appear as they happen." },
  connecting: { text: 'Connecting', title: 'Joining the live updates of the group.' },
  polling: { text: 'Checking every minute', title: 'Live updates are not connected; the page checks the cloud every minute.' }
};

export function LiveBadge({ state }: { state: LiveState }) {
  const { text, title } = liveTexts[state];
  return (
    <span className={`live live-${state}`} title={title}>
      <span className="dot" aria-hidden="true" />
      {text}
    </span>
  );
}

export function GameTabs({ groupId, game }: { groupId: string; game: GameKey }) {
  return (
    <nav className="tabs" aria-label="Game">
      {games.map((item) => (
        <a key={item.key} href={routeHash({ page: 'group', groupId, game: item.key })} aria-current={item.key === game ? 'page' : undefined}>
          {item.name}
        </a>
      ))}
    </nav>
  );
}

export function OnlineLine({ online, worldName }: { online: OnlineMember[]; worldName: (worldId: string) => string | null }) {
  if (online.length === 0) {
    return <p className="muted">Nobody else has SaveHarbor open right now.</p>;
  }

  return (
    <p className="online">
      <span className="dot" aria-hidden="true" />
      {online
        .map((member) => {
          const playing = member.playingWorldId ? worldName(member.playingWorldId) : null;
          return playing ? `${member.displayName} (playing ${playing})` : member.displayName;
        })
        .join(', ')}{' '}
      <span className="muted">{online.length === 1 ? 'has SaveHarbor open' : 'have SaveHarbor open'}</span>
    </p>
  );
}

export function CheckedRow({ checkedAt, loading, onRefresh }: { checkedAt: Date | null; loading: boolean; onRefresh: () => void }) {
  return (
    <div className="checked">
      <span className="muted">{checkedAt ? `Checked ${formatClock(checkedAt, new Date())}` : 'Checking…'}</span>
      <button type="button" className="quiet" onClick={onRefresh} disabled={loading}>
        {loading ? 'Refreshing…' : 'Refresh'}
      </button>
    </div>
  );
}

// The group's name and whether this member owns it; group_summaries has one row per group of the member.
export function useGroupSummary(groupId: string): { group: GroupSummary | null; error: string | null } {
  const [group, setGroup] = useState<GroupSummary | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let cancelled = false;
    loadGroups()
      .then((groups) => {
        if (!cancelled) {
          const found = groups.find((item) => item.id === groupId) ?? null;
          setGroup(found);
          setError(found ? null : 'You are not a member of this group any more.');
        }
      })
      .catch((reason: unknown) => {
        if (!cancelled) {
          setError(reason instanceof CloudError ? reason.message : 'Something went wrong. Try again.');
        }
      });
    return () => {
      cancelled = true;
    };
  }, [groupId]);
  return { group, error };
}

// Owner only. The code is fetched when asked for, not with the page, and never kept after leaving it.
export function InviteCard({ groupId }: { groupId: string }) {
  const [code, setCode] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function show() {
    try {
      setCode(await loadInviteCode(groupId));
      setMessage(null);
    } catch (reason) {
      setMessage(reason instanceof CloudError ? reason.message : 'Something went wrong. Try again.');
    }
  }

  async function copy(text: string, what: string) {
    try {
      await navigator.clipboard.writeText(text);
      setMessage(`${what} copied.`);
    } catch {
      setMessage('Copying is not allowed here; select the text and copy it instead.');
    }
  }

  return (
    <section className="panel">
      <h2>Invite a friend</h2>
      <p className="muted">Friends join with this code in the SaveHarbor app. Share it only with people you play with.</p>
      {code ? (
        <>
          <p className="invite-code">{code}</p>
          <div className="actions">
            <button type="button" onClick={() => void copy(code, 'Code')}>
              Copy code
            </button>
            <button type="button" onClick={() => void copy(`saveharbor://join/${code}`, 'Join link')}>
              Copy join link
            </button>
          </div>
        </>
      ) : (
        <button type="button" onClick={() => void show()}>
          Show invite code
        </button>
      )}
      {message && (
        <p className="muted" aria-live="polite">
          {message}
        </p>
      )}
    </section>
  );
}
