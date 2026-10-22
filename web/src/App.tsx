import type { Session } from '@supabase/supabase-js';
import { useEffect, useState } from 'react';
import { signOut } from './lib/api';
import { useRoute } from './lib/router';
import { supabase } from './lib/supabase';
import { GroupPage } from './pages/GroupPage';
import { GroupsPage } from './pages/GroupsPage';
import { SignInPage } from './pages/SignInPage';
import { WorldPage } from './pages/WorldPage';

export function App() {
  const [session, setSession] = useState<Session | null | undefined>(undefined);

  useEffect(() => {
    if (!supabase) {
      return;
    }

    void supabase.auth.getSession().then(({ data }) => setSession(data.session));
    const { data } = supabase.auth.onAuthStateChange((_event, next) => setSession(next));
    return () => data.subscription.unsubscribe();
  }, []);

  if (!supabase) {
    return (
      <Shell>
        <section className="panel">
          <h1>Not set up</h1>
          <p className="muted">This copy of the page was built without a SaveHarbor Cloud project.</p>
        </section>
      </Shell>
    );
  }

  if (session === undefined) {
    return <Shell>{null}</Shell>;
  }

  if (!session) {
    return (
      <Shell>
        <SignInPage />
      </Shell>
    );
  }

  return (
    <Shell onSignOut={() => void signOut()}>
      <Routes userId={session.user.id} />
    </Shell>
  );
}

function Routes({ userId }: { userId: string }) {
  const route = useRoute();
  const game = route.page === 'groups' ? 'windrose' : route.game;
  // The colours of the game being looked at, like the desktop app's theme.
  useEffect(() => {
    document.documentElement.dataset.game = game;
  }, [game]);

  switch (route.page) {
    case 'groups':
      return <GroupsPage />;
    case 'group':
      return <GroupPage groupId={route.groupId} game={route.game} userId={userId} />;
    case 'world':
      return <WorldPage groupId={route.groupId} game={route.game} worldUuid={route.worldUuid} userId={userId} />;
  }
}

function Shell({ children, onSignOut }: { children: React.ReactNode; onSignOut?: () => void }) {
  return (
    <div className="shell">
      <header className="topbar">
        <a className="brand" href="#/">
          SaveHarbor
        </a>
        {onSignOut && (
          <button type="button" className="quiet" onClick={onSignOut}>
            Sign out
          </button>
        )}
      </header>
      <main>{children}</main>
      <footer className="footer muted">Read-only view. Play, upload and download from the SaveHarbor app on your PC.</footer>
    </div>
  );
}
