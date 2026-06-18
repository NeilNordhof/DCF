import { useDevAuth } from '../context/DevAuthContext';
import { useUser } from '../context/UserContext';
import { useState } from 'react';
import { api } from '../api/client';

export function Profile() {
  const { logout } = useDevAuth();
  const { user, setUser } = useUser();
  const [ saving, setSaving] = useState(false);
  const [ error, setError] = useState<string | null>(null);
  
  if (!user) return <div>Loading...</div>;

  const handleToggle = async (checked: boolean) => {
    setSaving(true);
    setError(null);
    setUser({ ...user, emailNotificationsEnabled: checked });
    
    try {
      await api.updateNotificationPreferences(checked);
    } catch {
      setError('Failed to update notification preferences');
      setUser({ ...user, emailNotificationsEnabled: !checked });
    } finally {
      setSaving(false);
    }
  };

  return (
    <div>
      <h2>Profile</h2>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
        <div style={{
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          borderRadius: '8px',
          padding: 20,
          maxWidth: '480px',
          width: '100%'
        }}>
          <div style={{ fontSize: 12, color: 'var(--text-heading)', textTransform: 'uppercase', letterSpacing: '0.5px', marginBottom: '12px' }}>
            Account
          </div>
          <p>Display name: {user.displayName}</p>
          <p>Email: {user.email}</p>        
          {user.isAdmin && <p>✓ Admin</p>}
        </div>
        <div style={{
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          borderRadius: '8px',
          padding: 20,
          maxWidth: '480px',
          width: '100%'
        }}>
          <div style={{ fontSize: 12, color: 'var(--text-heading)', textTransform: 'uppercase', letterSpacing: '0.5px', marginBottom: '12px' }}>
            Notification Preferences
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12 }}>
            <div>
              <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-heading)', marginBottom: 4 }}>
                Email Notifications
              </div>
              <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
                Receive email reminders about upcoming drafts.
              </div>
            </div>
            <input
              type="checkbox"
              className="toggle"
              checked={user.emailNotificationsEnabled}
              onChange={(e) => handleToggle(e.target.checked)}
              disabled={saving}
            />
          </div>
          {error && <p style={{ color: 'var(--error)' }}>{error}</p>}
        </div>
        <button style={{ 
          fontSize: 11, 
          fontWeight: 800, 
          padding: '6px 14px', 
          borderRadius: 5,
          background: 'var(--accent)', 
          color: 'var(--bg)', 
          textDecoration: 'none',
          letterSpacing: '0.5px', 
          maxWidth: '480px',
          width: '100%'
        }} 
          onClick={() => logout()}>
          Sign Out
        </button>
      </div>
    </div>
  );
}
