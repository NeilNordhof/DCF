import { useAuth0 } from '@auth0/auth0-react';
import { Link } from 'react-router-dom';

export function Home() {
  const { isAuthenticated, loginWithRedirect, isLoading } = useAuth0();

  if (isLoading) return <div>Loading...</div>;

  if (!isAuthenticated) {
    return (
      <div>
        <h1>DCF Fantasy</h1>
        <p>Fantasy leagues for Drum Corps International fans.</p>
        <button onClick={() => loginWithRedirect()}>Sign In</button>
      </div>
    );
  }

  return (
    <div>
      <h1>DCF Fantasy</h1>
      <nav>
        <Link to="/leagues">My Leagues</Link> |{' '}
        <Link to="/profile">Profile</Link>
      </nav>
    </div>
  );
}
