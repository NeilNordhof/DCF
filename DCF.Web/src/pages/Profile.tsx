import { useDevAuth } from '../context/DevAuthContext';
import { useUser } from '../context/UserContext';

export function Profile() {
  const { logout } = useDevAuth();
  const { user } = useUser();

  if (!user) return <div>Loading...</div>;

  return (
    <div>
      <h2>Profile</h2>
      <p>Display name: {user.displayName}</p>
      <p>Email: {user.email}</p>
      {user.isAdmin && <p>✓ Admin</p>}
      <button onClick={() => logout()}>
        Sign Out
      </button>
    </div>
  );
}
