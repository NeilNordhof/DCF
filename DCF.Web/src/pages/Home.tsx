import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth0 } from '@auth0/auth0-react';
import { Auth0LockPasswordless } from 'auth0-lock';
import { api } from '../api/client';
import { useUser } from '../context/UserContext';

type Panel = 'lock' | 'loading' | 'onboarding';

export function Home() {
  const containerRef = useRef<HTMLDivElement>(null);
  const { isAuthenticated, getAccessTokenSilently, user: auth0User } = useAuth0();
  const { setUser } = useUser();
  const navigate = useNavigate();
  const [panel, setPanel] = useState<Panel>('lock');
  const [displayName, setDisplayName] = useState('');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const getTokenRef = useRef(getAccessTokenSilently);
  getTokenRef.current = getAccessTokenSilently;

  useEffect(() => {
    if (!isAuthenticated) return;

    setPanel('loading');

    api.getUser().then((profile) => {
      if (profile) {
        setUser(profile);
        navigate('/leagues');
      } else {
        setDisplayName(auth0User?.name ?? '');
        setPanel('onboarding');
      }
    }).catch(() => {
      setPanel('lock');
    });
  }, [isAuthenticated, auth0User, navigate, setUser]);

  useEffect(() => {
    if (!containerRef.current) return;

    const lock = new Auth0LockPasswordless(
      import.meta.env.VITE_AUTH0_CLIENT_ID as string,
      import.meta.env.VITE_AUTH0_DOMAIN as string,
      {
        allowedConnections: ['google-oauth2', 'email'],
        passwordlessMethod: 'code',
        container: 'lock-container',
        auth: {
          params: {
            audience: import.meta.env.VITE_AUTH0_AUDIENCE as string,
            scope: 'openid profile email',
          },
        },
        theme: { primaryColor: '#c084fc' },
        languageDictionary: { title: '' },
        closable: false,
      }
    );

    lock.on('authenticated', async () => {
      try {
        await getTokenRef.current();
      } catch {
        // Silent auth failed — isAuthenticated won't update.
      }
    });

    lock.show();

    return () => { lock.hide(); };
  }, []);

  async function handleOnboard(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setSubmitError(null);

    try {
      const profile = await api.upsertUser(displayName, auth0User?.email ?? '');
      setUser(profile);
      navigate('/leagues');
    } catch {
      setSubmitError('Failed to create profile. Please try again.');
      setSubmitting(false);
    }
  }

  return (
    <div style={{
      minHeight: '100svh',
      background: 'var(--bg)',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      padding: 20,
      position: 'relative',
    }}>
      {/* Radial purple glow */}
      <div style={{
        position: 'fixed',
        top: '40%',
        left: '50%',
        transform: 'translate(-50%, -50%)',
        width: 700,
        height: 700,
        background: 'radial-gradient(circle, rgba(192,132,252,0.07) 0%, transparent 70%)',
        pointerEvents: 'none',
      }} />

      {/* Minimal nav */}
      <nav style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        height: 44,
        display: 'flex',
        alignItems: 'center',
        padding: '0 20px',
        zIndex: 10,
      }}>
        <span style={{ color: 'var(--accent)', fontWeight: 700, fontSize: 13, letterSpacing: '0.5px' }}>
          DCF FANTASY
        </span>
      </nav>

      {/* Split card */}
      <div style={{
        display: 'flex',
        width: '100%',
        maxWidth: 780,
        border: '1px solid var(--border)',
        borderRadius: 8,
        overflow: 'hidden',
        position: 'relative',
        zIndex: 1,
        minHeight: 480,
      }}>
        {/* Left — brand panel */}
        <div style={{
          flex: '1 1 340px',
          background: 'linear-gradient(135deg, #1a0e2e, var(--surface))',
          padding: 40,
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          gap: 24,
        }}>
          <div>
            <div style={{ fontSize: 34, fontWeight: 900, color: 'var(--accent)', letterSpacing: '-0.5px', lineHeight: 1 }}>DCF</div>
            <div style={{ fontSize: 9, fontWeight: 700, color: 'var(--text-faint)', letterSpacing: '1px', textTransform: 'uppercase', marginTop: 2 }}>FANTASY</div>
          </div>
          <div>
            <h1 style={{ fontSize: 19, fontWeight: 800, color: 'var(--text-heading)', lineHeight: 1.35, marginBottom: 10 }}>
              Draft corps.<br />Score points.<br />Win the season.
            </h1>
            <p style={{ fontSize: 11, color: 'var(--text)', lineHeight: 1.65 }}>
              The fantasy league built for Drum Corps International fans. Draft your favourite corps, track real DCI scores, and compete all season long.
            </p>
          </div>
          <ul style={{ listStyle: 'none', padding: 0, margin: 0, display: 'flex', flexDirection: 'column', gap: 10 }}>
            {[
              'Snake draft with your league',
              'Real DCI scores, auto-updated',
              'Private leagues with invite codes',
            ].map(text => (
              <li key={text} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 11, color: 'var(--text)' }}>
                <span style={{ color: 'var(--accent)', fontSize: 8 }}>●</span>
                {text}
              </li>
            ))}
          </ul>
        </div>

        {/* Right — dynamic panel */}
        <div style={{
          flex: '0 0 340px',
          background: 'var(--surface-2)',
          borderLeft: '1px solid var(--border)',
          display: 'flex',
          flexDirection: 'column',
          minHeight: 480,
        }}>
          {/* Lock container — always in DOM so the widget initialises once */}
          <div
            id="lock-container"
            ref={containerRef}
            style={{ flex: 1, minHeight: 480, display: panel === 'lock' ? 'block' : 'none' }}
          />

          {panel === 'loading' && (
            <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>Loading...</span>
            </div>
          )}

          {panel === 'onboarding' && (
            <div style={{ flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'center', padding: 32, gap: 24 }}>
              <div>
                <div style={{ fontSize: 16, fontWeight: 700, color: 'var(--text-heading)', marginBottom: 6 }}>
                  Welcome to DCF Fantasy
                </div>
                <div style={{ fontSize: 11, color: 'var(--text)' }}>
                  Choose a display name to get started.
                </div>
              </div>
              <form onSubmit={handleOnboard} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  <label style={{ fontSize: 10, fontWeight: 700, color: 'var(--text-muted)', letterSpacing: '0.5px', textTransform: 'uppercase' }}>
                    Display Name
                  </label>
                  <input
                    value={displayName}
                    onChange={e => setDisplayName(e.target.value)}
                    required
                    minLength={1}
                    style={{
                      background: 'var(--bg)',
                      border: '1px solid var(--border-input)',
                      borderRadius: 4,
                      color: 'var(--text-heading)',
                      padding: '8px 12px',
                      fontSize: 13,
                      outline: 'none',
                      width: '100%',
                      boxSizing: 'border-box',
                    }}
                  />
                </div>
                {submitError && (
                  <div style={{ fontSize: 11, color: 'var(--red)' }}>{submitError}</div>
                )}
                <button
                  type="submit"
                  disabled={submitting}
                  style={{
                    background: 'var(--accent)',
                    color: '#fff',
                    border: 'none',
                    borderRadius: 4,
                    padding: '10px 0',
                    fontSize: 13,
                    fontWeight: 700,
                    cursor: submitting ? 'not-allowed' : 'pointer',
                    opacity: submitting ? 0.7 : 1,
                    width: '100%',
                  }}
                >
                  {submitting ? 'Setting up...' : 'Get started'}
                </button>
              </form>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
