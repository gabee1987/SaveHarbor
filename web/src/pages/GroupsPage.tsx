import { useEffect, useState } from 'react';
import { CloudError, loadGroups, type GroupSummary } from '../lib/api';
import { navigate, routeHash } from '../lib/router';

export function GroupsPage() {
  const [groups, setGroups] = useState<GroupSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    loadGroups()
      .then((next) => {
        if (cancelled) {
          return;
        }

        // One group: nothing to choose.
        const only = next.length === 1 ? next[0] : undefined;
        if (only) {
          navigate({ page: 'group', groupId: only.id, game: 'windrose' }, true);
        } else {
          setGroups(next);
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
  }, []);

  if (error) {
    return (
      <section className="panel">
        <p className="error" role="alert">{error}</p>
      </section>
    );
  }

  if (!groups) {
    return <p className="muted">Loading your groups…</p>;
  }

  if (groups.length === 0) {
    return (
      <section className="panel">
        <h1>No group yet</h1>
        <p className="muted">Join or create a group in the SaveHarbor app on your PC; it shows up here afterwards.</p>
      </section>
    );
  }

  return (
    <section>
      <h1>Your groups</h1>
      <ul className="cards">
        {groups.map((group) => (
          <li key={group.id}>
            <a className="card link-card" href={routeHash({ page: 'group', groupId: group.id, game: 'windrose' })}>
              <span className="card-title">{group.name}</span>
              <span className="muted">
                {group.memberCount === 1 ? '1 member' : `${group.memberCount} members`}
                {group.isOwner ? ' · you own it' : ''}
              </span>
            </a>
          </li>
        ))}
      </ul>
    </section>
  );
}
