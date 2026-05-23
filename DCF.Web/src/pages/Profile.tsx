import { useAuth0 } from '@auth0/auth0-react';
import { useUser } from '../context/UserContext';

export function Profile() {
  const { logout } = useAuth0();
  const { user } = useUser();

  if (!user) return <div>Loading...</div>;

  return (
    <div>
      <h2>Profile</h2>
      <p>Display name: {user.displayName}</p>
      <p>Email: {user.email}</p>
      {user.isAdmin && <p>✓ Admin</p>}
      <button onClick={() => logout({ logoutParams: { returnTo: window.location.origin } })}>
        Sign Out
      </button>
    </div>
  );
}
