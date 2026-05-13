import { useAuth0 } from '@auth0/auth0-react';
import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { UserProfile } from '../types/api';

export function Profile() {
  const { logout } = useAuth0();
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.upsertUser().then(setProfile).catch(() => setError('Failed to load profile.'));
  }, []);

  if (error) return <div>{error}</div>;
  if (!profile) return <div>Loading...</div>;

  return (
    <div>
      <h2>Profile</h2>
      <p>Display name: {profile.displayName}</p>
      <p>Email: {profile.email}</p>
      {profile.isAdmin && <p>✓ Admin</p>}
      <button onClick={() => logout({ logoutParams: { returnTo: window.location.origin } })}>
        Sign Out
      </button>
    </div>
  );
}
