import type { RealtimeChannel } from '@supabase/supabase-js';
import { useEffect, useRef, useState } from 'react';
import { client } from './supabase';

export type LiveState = 'connecting' | 'live' | 'polling';

// The group's private channel, the same one the desktop app joins: a change to a world row of the group calls
// onChange, and presence says who has SaveHarbor open. The page only listens; it never tracks presence, so opening it
// on a phone does not show anyone as online (or hide what their PC is playing).
export function useLiveChannel(groupId: string, onChange: () => void): { state: LiveState; presence: Record<string, unknown[]> } {
  const [state, setState] = useState<LiveState>('connecting');
  const [presence, setPresence] = useState<Record<string, unknown[]>>({});
  const changed = useRef(onChange);
  changed.current = onChange;

  useEffect(() => {
    const supabase = client();
    let channel: RealtimeChannel | null = null;
    let cancelled = false;
    setState('connecting');
    setPresence({});

    void (async () => {
      // A private channel checks the member's token, so it must be set before joining.
      await supabase.realtime.setAuth();
      if (cancelled) {
        return;
      }

      const filter = `group_id=eq.${groupId}`;
      const joined = supabase
        .channel(`saveharbor:${groupId}`, { config: { private: true } })
        .on('postgres_changes', { event: 'INSERT', schema: 'public', table: 'worlds', filter }, () => changed.current())
        .on('postgres_changes', { event: 'UPDATE', schema: 'public', table: 'worlds', filter }, () => changed.current())
        .on('presence', { event: 'sync' }, () => setPresence({ ...joined.presenceState() }));
      channel = joined;
      joined.subscribe((status) => {
        if (!cancelled) {
          setState(status === 'SUBSCRIBED' ? 'live' : 'polling');
          if (status !== 'SUBSCRIBED') {
            setPresence({});
          }
        }
      });
    })().catch(() => {
      if (!cancelled) {
        setState('polling');
      }
    });

    return () => {
      cancelled = true;
      if (channel) {
        void supabase.removeChannel(channel);
      }
    };
  }, [groupId]);

  return { state, presence };
}
