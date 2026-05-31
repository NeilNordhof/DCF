import { useEffect } from 'react';
import { Outlet } from 'react-router-dom';
import { api, setTokenGetter } from './api/client';
import { Nav } from './components/Nav';
import { useDevAuth } from './context/DevAuthContext';
import { useUser } from './context/UserContext';

export function AuthenticatedLayout({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100svh' }}>
      <Nav />
      <div style={{ flex: 1, maxWidth: 1200, width: '100%', margin: '0 auto', padding: '24px 20px', boxSizing: 'border-box' }}>
        {children}
      </div>
    </div>
  );
}

export default function App() {
  const { getAccessTokenSilently, isAuthenticated } = useDevAuth();
  const { setUser } = useUser();

  setTokenGetter(() => getAccessTokenSilently());

  useEffect(() => {
    if (!isAuthenticated) return;

    api.getUser().then((profile) => {
      if (profile) {
        setUser(profile);
      }
    }).catch((err) => {
      console.error('Failed to load user profile:', err);
    });
  }, [isAuthenticated, setUser]);

  return <Outlet />;
}
