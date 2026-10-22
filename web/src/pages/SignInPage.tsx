import { useState, type FormEvent } from 'react';
import { CloudError, requestCode, verifyCode } from '../lib/api';

// The same e-mail code sign-in as the desktop app.
export function SignInPage() {
  const [pending] = useState(readPendingSignIn);
  const [step, setStep] = useState<'email' | 'code'>(pending ? 'code' : 'email');
  const [email, setEmail] = useState(pending ?? '');
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (reason) {
      setError(reason instanceof CloudError ? reason.message : 'Something went wrong. Try again.');
    } finally {
      setBusy(false);
    }
  }

  function sendCode(event: FormEvent) {
    event.preventDefault();
    void run(async () => {
      await requestCode(email.trim());
      writePendingSignIn(email.trim());
      setCode('');
      setStep('code');
    });
  }

  function checkCode(event: FormEvent) {
    event.preventDefault();
    void run(async () => {
      await verifyCode(email.trim(), code.replace(/\s/g, ''));
      writePendingSignIn(null);
    });
  }

  if (step === 'email') {
    return (
      <section className="panel narrow">
        <h1>Sign in</h1>
        <p className="muted">Use the e-mail address you sign in with in the SaveHarbor app. We send you a code.</p>
        <form onSubmit={sendCode}>
          <label htmlFor="email">E-mail address</label>
          <input id="email" type="email" autoComplete="email" required maxLength={254} value={email} onChange={(event) => setEmail(event.target.value)} />
          {error && <p className="error" role="alert">{error}</p>}
          <button type="submit" className="primary" disabled={busy}>
            {busy ? 'Sending…' : 'Send code'}
          </button>
        </form>
        <p className="hint">New to SaveHarbor? Install the app on your PC and join your friends' group there first.</p>
      </section>
    );
  }

  return (
    <section className="panel narrow">
      <h1>Enter the code</h1>
      <p className="muted" aria-live="polite">
        If this address has a SaveHarbor account, a code is on its way. It can take a minute.
      </p>
      <form onSubmit={checkCode}>
        <label htmlFor="code">Code from the e-mail</label>
        <input
          id="code"
          inputMode="numeric"
          autoComplete="one-time-code"
          required
          pattern="[0-9 ]{6,12}"
          maxLength={12}
          value={code}
          onChange={(event) => setCode(event.target.value)}
        />
        {error && <p className="error" role="alert">{error}</p>}
        <button type="submit" className="primary" disabled={busy}>
          {busy ? 'Checking…' : 'Sign in'}
        </button>
      </form>
      <button
        type="button"
        className="link"
        onClick={() => {
          writePendingSignIn(null);
          setStep('email');
        }}
        disabled={busy}
      >
        Use another address or send a new code
      </button>
    </section>
  );
}

// A phone reloads the tab when the person comes back from the mail app, so the code step survives a reload. Only the
// address is kept, only in this tab (sessionStorage), and only while a code can still be valid; never the code.
const pendingKey = 'saveharbor.pendingSignIn';
const pendingForMs = 10 * 60_000;

function readPendingSignIn(): string | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingKey) ?? 'null');
    if (typeof value === 'object' && value !== null && 'email' in value && 'at' in value) {
      const { email, at } = value as { email: unknown; at: unknown };
      if (typeof email === 'string' && typeof at === 'number' && Date.now() - at < pendingForMs) {
        return email;
      }
    }

    sessionStorage.removeItem(pendingKey);
  } catch {
    // Storage blocked or unreadable: start from the address step.
  }

  return null;
}

function writePendingSignIn(email: string | null): void {
  try {
    if (email) {
      sessionStorage.setItem(pendingKey, JSON.stringify({ email, at: Date.now() }));
    } else {
      sessionStorage.removeItem(pendingKey);
    }
  } catch {
    // Without storage the page still works; it only forgets the step on a reload.
  }
}
