import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';

type UnsubscribeStatus = 'loading' | 'success' | 'error';

export function Unsubscribe() {
  const [searchParams] = useSearchParams();
  const [status, setStatus] = useState<UnsubscribeStatus>('loading');

  useEffect(() => {
    const token = searchParams.get('token');

    if (!token) {
      setStatus('error');

      return;
    }

    api.unsubscribe(token)
      .then(() => setStatus('success'))
      .catch(() => setStatus('error'));
  }, [searchParams]);

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

      <div style={{
        maxWidth: 480,
        width: '100%',
        backgroundColor: 'var(--surface)',
        border: '1px solid var(--border)',
        borderRadius: 8,
        padding: 32,
        position: 'relative',
        zIndex: 1,
      }}>
        {status === 'loading' && (
          <div style={{ textAlign: 'center', padding: '16px 0' }}>
            <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>Processing...</span>
          </div>
        )}

        {status === 'success' && (
          <>
            <h2 style={{ fontSize: 18, fontWeight: 700, marginBottom: 12 }}>
              You&apos;ve been unsubscribed
            </h2>

            <p style={{ color: 'var(--text)', marginBottom: 24, lineHeight: 1.6 }}>
              You won&apos;t receive any more email notifications from Drum Corps Fantasy.
            </p>

            <div style={{ display: 'flex', gap: 12 }}>
              <Link
                to="/"
                style={{
                  background: 'var(--accent)',
                  color: '#fff',
                  border: 'none',
                  borderRadius: 4,
                  padding: '8px 16px',
                  fontSize: 12,
                  fontWeight: 700,
                  textDecoration: 'none',
                  display: 'inline-block',
                }}
              >
                Go to Home
              </Link>

              <Link
                to="/profile"
                style={{
                  background: 'var(--surface-2)',
                  color: 'var(--text-heading)',
                  border: '1px solid var(--border)',
                  borderRadius: 4,
                  padding: '8px 16px',
                  fontSize: 12,
                  fontWeight: 600,
                  textDecoration: 'none',
                  display: 'inline-block',
                }}
              >
                Manage Email Preferences
              </Link>
            </div>
          </>
        )}

        {status === 'error' && (
          <>
            <h2 style={{ fontSize: 18, fontWeight: 700, marginBottom: 12 }}>
              Something went wrong
            </h2>

            <p style={{ color: 'var(--text)', marginBottom: 24, lineHeight: 1.6 }}>
              This unsubscribe link may be invalid or has already been used.
            </p>

            <div style={{ display: 'flex', gap: 12 }}>
              <Link
                to="/"
                style={{
                  background: 'var(--accent)',
                  color: '#fff',
                  border: 'none',
                  borderRadius: 4,
                  padding: '8px 16px',
                  fontSize: 12,
                  fontWeight: 700,
                  textDecoration: 'none',
                  display: 'inline-block',
                }}
              >
                Go to Home
              </Link>

              <Link
                to="/profile"
                style={{
                  background: 'var(--surface-2)',
                  color: 'var(--text-heading)',
                  border: '1px solid var(--border)',
                  borderRadius: 4,
                  padding: '8px 16px',
                  fontSize: 12,
                  fontWeight: 600,
                  textDecoration: 'none',
                  display: 'inline-block',
                }}
              >
                Manage Email Preferences
              </Link>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
