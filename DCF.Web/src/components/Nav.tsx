import { Link, useLocation } from 'react-router-dom';
import { useUser } from '../context/UserContext';

export function Nav() {
  const { user } = useUser();
  const location = useLocation();
  const isAdmin = location.pathname.startsWith('/admin');

  const initials = user?.displayName
    ? user.displayName.split(' ').map((w: string) => w[0]).join('').slice(0, 2).toUpperCase()
    : '?';

  const linkStyle = (prefix: string): React.CSSProperties => ({
    fontSize: 11,
    color: location.pathname.startsWith(prefix) ? 'var(--accent)' : 'var(--text-muted)',
    textDecoration: 'none',
    fontWeight: 600,
    letterSpacing: '0.5px',
    paddingBottom: 2,
    borderBottom: location.pathname.startsWith(prefix) ? '2px solid var(--accent)' : '2px solid transparent',
  });

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
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flex: 1 }}>
        <Link to="/leagues" style={{ color: 'var(--accent)', fontWeight: 700, fontSize: 13, letterSpacing: '0.5px', textDecoration: 'none' }}>
          DCF FANTASY
        </Link>
        {isAdmin && (
          <span style={{
            fontSize: 8,
            padding: '2px 6px',
            background: '#374151',
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
      <div style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
        <Link to="/leagues" style={linkStyle('/leagues')}>LEAGUES</Link>
        <Link to="/profile" style={linkStyle('/profile')}>PROFILE</Link>
        <div style={{
          width: 28,
          height: 28,
          borderRadius: '50%',
          background: 'var(--accent)',
          color: '#0d0f14',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: 11,
          fontWeight: 700,
          flexShrink: 0,
        }}>
          {initials}
        </div>
      </div>
    </nav>
  );
}
