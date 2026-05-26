import { useEffect, useRef } from 'react';
import Auth0Lock from 'auth0-lock';

export function Home() {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!containerRef.current) return;

    const lock = new Auth0Lock(
      import.meta.env.VITE_AUTH0_CLIENT_ID as string,
      import.meta.env.VITE_AUTH0_DOMAIN as string,
      {
        container: 'lock-container',
        auth: {
          redirectUrl: `${window.location.origin}/leagues`,
          responseType: 'code',
          params: { audience: import.meta.env.VITE_AUTH0_AUDIENCE as string },
        },
        theme: { primaryColor: '#c084fc' },
        languageDictionary: { title: '' },
        allowShowPassword: true,
        closable: false,
      }
    );

    lock.show();

    return () => { lock.hide(); };
  }, []);

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

        {/* Right — Auth0 Lock */}
        <div style={{
          flex: '0 0 340px',
          background: 'var(--surface-2)',
          borderLeft: '1px solid var(--border)',
          display: 'flex',
          flexDirection: 'column',
          minHeight: 480,
        }}>
          <div id="lock-container" ref={containerRef} style={{ flex: 1, minHeight: 480 }} />
        </div>
      </div>
    </div>
  );
}
