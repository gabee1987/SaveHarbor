import { useCallback, useEffect, useRef, useState } from 'react';
import { CloudError, loadSnapshot } from './api';
import type { GameKey } from './games';
import { useLiveChannel, type LiveState } from './live';
import { onlineMembers, type OnlineMember } from './presence';
import type { GroupSnapshot } from './snapshot';

export interface GroupData {
  snapshot: GroupSnapshot | null;
  error: string | null;
  loading: boolean;
  checkedAt: Date | null;
  live: LiveState;
  online: OnlineMember[];
  // Grows with every finished refresh, so a page can reload what depends on the snapshot.
  revision: number;
  refresh: () => void;
}

const debounceMs = 500;
const pollMs = 60_000;

// One game of a group: loaded on open, again shortly after a friend's change arrives, every minute while the live
// channel is down, and when the page becomes visible again.
export function useGroup(groupId: string, game: GameKey): GroupData {
  const [snapshot, setSnapshot] = useState<GroupSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [checkedAt, setCheckedAt] = useState<Date | null>(null);
  const [revision, setRevision] = useState(0);
  const generation = useRef(0);
  const debounce = useRef<number | undefined>(undefined);

  const refresh = useCallback(() => {
    const current = ++generation.current;
    setLoading(true);
    loadSnapshot(groupId, game)
      .then((next) => {
        if (current === generation.current) {
          setSnapshot(next);
          setError(null);
          setCheckedAt(new Date());
          setRevision((value) => value + 1);
        }
      })
      .catch((reason: unknown) => {
        if (current === generation.current) {
          setError(reason instanceof CloudError ? reason.message : 'Something went wrong. Try again.');
        }
      })
      .finally(() => {
        if (current === generation.current) {
          setLoading(false);
        }
      });
  }, [groupId, game]);

  const onChange = useCallback(() => {
    window.clearTimeout(debounce.current);
    debounce.current = window.setTimeout(refresh, debounceMs);
  }, [refresh]);

  const { state: live, presence } = useLiveChannel(groupId, onChange);

  useEffect(() => {
    setSnapshot(null);
    refresh();
    return () => {
      generation.current++;
      window.clearTimeout(debounce.current);
    };
  }, [refresh]);

  // Back to live after a gap: changes made meanwhile were not delivered.
  const wasLive = useRef(false);
  useEffect(() => {
    if (live === 'live' && !wasLive.current && checkedAt) {
      refresh();
    }

    wasLive.current = live === 'live';
    if (live !== 'polling') {
      return;
    }

    const timer = window.setInterval(refresh, pollMs);
    return () => window.clearInterval(timer);
    // checkedAt is left out on purpose: only a change of the connection state matters here.
  }, [live, refresh]);

  useEffect(() => {
    const onVisible = () => {
      if (document.visibilityState === 'visible') {
        refresh();
      }
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => document.removeEventListener('visibilitychange', onVisible);
  }, [refresh]);

  const online = snapshot ? onlineMembers(presence, snapshot.members) : [];
  return { snapshot, error, loading, checkedAt, live, online, revision, refresh };
}

// Re-renders every interval, so relative times and lock expiry stay current without a refresh.
export function useNow(intervalMs = 30_000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(timer);
  }, [intervalMs]);
  return now;
}
