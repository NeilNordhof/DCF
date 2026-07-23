import { useEffect, useState } from 'react';
import type { CSSProperties } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useUser } from '../context/UserContext';

export function Nav() {
  const { user } = useUser();
  const { logout, loginWithRedirect } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const isAdmin = location.pathname.startsWith('/admin');
  const [menuOpen, setMenuOpen] = useState(false);

  const initials = user?.displayName
    ? user.displayName.split(' ').filter(Boolean).map((w: string) => w[0]).join('').slice(0, 2).toUpperCase()
    : '?';

  const linkStyle = (prefix: string): CSSProperties => ({
    fontSize: 11,
    color: location.pathname.startsWith(prefix) ? 'var(--accent)' : 'var(--text-muted)',
    textDecoration: 'none',
    fontWeight: 600,
    letterSpacing: '0.5px',
    paddingBottom: 2,
    borderBottom: location.pathname.startsWith(prefix) ? '2px solid var(--accent)' : '2px solid transparent',
  });

  useEffect(() => {
    if (!menuOpen) {
      return;
    }

    function handleOutsideClick() {
      setMenuOpen(false);
    }

    document.addEventListener('click', handleOutsideClick);

    return () => document.removeEventListener('click', handleOutsideClick);
  }, [menuOpen]);

  function closeMenu() {
    setMenuOpen(false);
  }

  return (
    <nav style={{
      background: 'var(--surface)',
      borderBottom: '1px solid var(--border)',
      height: 44,
      display: 'flex',
      alignItems: 'center',
      padding: '0 20px',
      gap: 20,
      flexShrink: 0,
      position: 'relative',
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 20, flex: 1 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <Link to="/leagues" style={{ color: 'var(--accent)', fontWeight: 700, fontSize: 13, letterSpacing: '0.5px', textDecoration: 'none' }}>
            DCF - Drum Corps Fantasy
          </Link>
          {isAdmin && (
            <span style={{
              fontSize: 8,
              padding: '2px 6px',
              background: 'var(--surface-elevated)',
              color: 'var(--text-muted)',
              borderRadius: 4,
              fontWeight: 700,
              letterSpacing: '0.5px',
              textTransform: 'uppercase',
            }}>
              ADMIN
            </span>
          )}
        </div>
        <Link to="/leagues" className="nav-links" style={linkStyle('/leagues')}>LEAGUES</Link>
        <Link to="/dci" className="nav-links" style={linkStyle('/dci')}>DCI</Link>
      </div>
      <div className="nav-links" style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
        {user ? (
          <>
            {user.isAdmin && (
              <Link to="/admin" style={linkStyle('/admin')}>ADMIN</Link>
            )}
            <Link to="/profile" style={linkStyle('/profile')}>PROFILE</Link>
            <button
              onClick={() => { logout(); navigate('/'); }}
              title="Switch user"
              style={{
                width: 28,
                height: 28,
                borderRadius: '50%',
                background: 'var(--accent)',
                color: 'var(--bg)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: 11,
                fontWeight: 700,
                flexShrink: 0,
                border: 'none',
                cursor: 'pointer',
              }}
            >
              {initials}
            </button>
          </>
        ) : (
          <button
            onClick={() => loginWithRedirect()}
            style={{
              background: 'var(--accent)',
              color: 'var(--bg)',
              border: 'none',
              borderRadius: 4,
              padding: '6px 14px',
              fontSize: 11,
              fontWeight: 700,
              letterSpacing: '0.5px',
              cursor: 'pointer',
            }}
          >
            LOG IN
          </button>
        )}
      </div>
      <button
        className="nav-hamburger"
        onClick={e => { e.stopPropagation(); setMenuOpen(m => !m); }}
        aria-label="Toggle menu"
        style={{
          background: 'none',
          border: 'none',
          cursor: 'pointer',
          padding: 4,
          flexDirection: 'column',
          justifyContent: 'space-between',
          width: 20,
          height: 16,
          flexShrink: 0,
        }}
      >
        <div style={{ width: '100%', height: 2, background: 'var(--text-heading)', borderRadius: 1 }} />
        <div style={{ width: '100%', height: 2, background: 'var(--text-heading)', borderRadius: 1 }} />
        <div style={{ width: '100%', height: 2, background: 'var(--text-heading)', borderRadius: 1 }} />
      </button>
      <div
        className={`nav-mobile-menu${menuOpen ? ' open' : ''}`}
        onClick={e => e.stopPropagation()}
      >
        <Link to="/leagues" onClick={closeMenu}>LEAGUES</Link>
        <Link to="/dci" onClick={closeMenu}>DCI</Link>
        {user ? (
          <>
            {user.isAdmin && (
              <Link to="/admin" onClick={closeMenu}>ADMIN</Link>
            )}
            <Link to="/profile" onClick={closeMenu}>PROFILE</Link>
            <button onClick={() => { logout(); navigate('/'); closeMenu(); }}>Logout</button>
          </>
        ) : (
          <button onClick={() => { loginWithRedirect(); closeMenu(); }}>Log In</button>
        )}
      </div>
    </nav>
  );
}
