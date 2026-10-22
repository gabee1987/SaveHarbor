import type { Session } from '@supabase/supabase-js';
import { useEffect, useState } from 'react';
import { signOut } from './lib/api';
import { useRoute } from './lib/router';
import { finishGoogleSignIn } from './lib/oauth';
import { supabase } from './lib/supabase';
import { GroupPage } from './pages/GroupPage';
import { GroupsPage } from './pages/GroupsPage';
import { SignInPage } from './pages/SignInPage';
import { WorldPage } from './pages/WorldPage';

export function App() {
  const [session, setSession] = useState<Session | null | undefined>(undefined);
  const [signInNotice, setSignInNotice] = useState<string | null>(null);

  useEffect(() => {
    if (!supabase) {
      return;
    }

    const auth = supabase.auth;
    const { data } = auth.onAuthStateChange((_event, next) => setSession(next));
    void finishGoogleSignIn()
      .then((notice) => {
        setSignInNotice(notice);
        return auth.getSession();
      })
      .then(({ data: current }) => setSession(current.session));
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
        <SignInPage notice={signInNotice} />
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
      <footer className="footer muted">
        <p>Read-only view. Play, upload and download from the SaveHarbor app on your PC.</p>
        <a href={`${import.meta.env.BASE_URL}about.html`}>About</a> · <a href={`${import.meta.env.BASE_URL}privacy.html`}>Privacy</a> · <a href={`${import.meta.env.BASE_URL}terms.html`}>Terms</a>
      </footer>
    </div>
  );
}
