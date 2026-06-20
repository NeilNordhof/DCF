import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { DEV_USERS } from '../context/DevAuthContext';
import { useUser } from '../context/UserContext';

type Panel = 'signin' | 'loading' | 'onboarding';

export function Home() {
  const { isAuthenticated, user, loginWithRedirect, devLogin } = useAuth();
  const { setUser } = useUser();
  const navigate = useNavigate();
  const [panel, setPanel] = useState<Panel>('signin');
  const [displayName, setDisplayName] = useState('');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (panel === 'signin' && !devLogin) {
      loginWithRedirect();
    }
  }, [panel, devLogin, loginWithRedirect]);

  useEffect(() => {
    if (!isAuthenticated) return;

    setPanel('loading');

    api.getUser().then((profile) => {
      if (profile) {
        setUser(profile);
        navigate('/leagues');
      } else {
        setDisplayName(user?.name ?? '');
        setPanel('onboarding');
      }
    }).catch(() => {
      setPanel('signin');
    });
  }, [isAuthenticated, user, navigate, setUser]);

  async function handleOnboard(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setSubmitError(null);

    try {
      const profile = await api.upsertUser(displayName, user?.email ?? '');
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
            <div style={{ fontSize: 44, fontWeight: 900, color: 'var(--accent)', letterSpacing: '-0.5px', lineHeight: 1 }}>DCF</div>
            <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-faint)', letterSpacing: '1px', textTransform: 'uppercase', marginTop: 2 }}>Fantasy platform for<br/>marching music's major league</div>
          </div>
          <div>
            <h1 style={{ fontSize: 19, fontWeight: 800, color: 'var(--text-heading)', lineHeight: 1.35, marginBottom: 10 }}>
              Draft corps.<br />Score points.<br />Win the season.
            </h1>
            <p style={{ fontSize: 11, color: 'var(--text)', lineHeight: 1.65 }}>
              The fantasy league built for Drum Corps International fans. Join a league with your friends and drafft captions from your favourite corps. Track real DCI scores and compete all season long to see who has the best fantasy corps.
            </p>
          </div>
          
        </div>

        {/* Right — dynamic panel */}
        <div style={{
          flex: '0 0 340px',
          background: 'var(--surface-2)',
          borderLeft: '1px solid var(--border)',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          minHeight: 480,
        }}>
          {panel === 'signin' && (
            <div style={{ padding: 32, display: 'flex', flexDirection: 'column', gap: 24 }}>
              {devLogin ? (
                <>
                  <div>
                    <div style={{ fontSize: 16, fontWeight: 700, color: 'var(--text-heading)', marginBottom: 6 }}>
                      Dev login
                    </div>
                    <div style={{ fontSize: 11, color: 'var(--text)' }}>
                      Choose a test user to continue.
                    </div>
                  </div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                    {DEV_USERS.map(u => (
                      <button
                        key={u.sub}
                        onClick={() => devLogin(u.sub)}
                        style={{
                          background: 'var(--surface)',
                          border: '1px solid var(--border)',
                          borderRadius: 6,
                          padding: '12px 16px',
                          cursor: 'pointer',
                          display: 'flex',
                          alignItems: 'center',
                          gap: 12,
                          textAlign: 'left',
                        }}
                      >
                        <div style={{
                          width: 32,
                          height: 32,
                          borderRadius: '50%',
                          background: 'var(--accent)',
                          color: 'var(--bg)',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          fontSize: 12,
                          fontWeight: 700,
                          flexShrink: 0,
                        }}>
                          {u.displayName[0]}
                        </div>
                        <div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-heading)' }}>{u.displayName}</div>
                          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{u.email}</div>
                        </div>
                      </button>
                    ))}
                  </div>
                </>
              ) : (
                <div id="auth0-lock-container" style={{ width: '100%' }} />
              )}
            </div>
          )}

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
