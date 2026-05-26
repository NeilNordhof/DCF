import { useAuth0 } from '@auth0/auth0-react';
import { useEffect } from 'react';
import { Route, Routes, useNavigate } from 'react-router-dom';
import { api, setTokenGetter } from './api/client';
import { AdminRoute } from './components/AdminRoute';
import { Nav } from './components/Nav';
import { ProtectedRoute } from './components/ProtectedRoute';
import { useUser } from './context/UserContext';
import { Admin } from './pages/Admin';
import { DraftRoom } from './pages/DraftRoom';
import { Home } from './pages/Home';
import { LeagueCreate } from './pages/LeagueCreate';
import { LeagueDetail } from './pages/LeagueDetail';
import { Leagues } from './pages/Leagues';
import { Onboarding } from './pages/Onboarding';
import { Profile } from './pages/Profile';
import { SeasonDetail } from './pages/SeasonDetail';

function AuthenticatedLayout({ children }: { children: React.ReactNode }) {
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
  const { getAccessTokenSilently, isAuthenticated } = useAuth0();
  const { setUser } = useUser();
  const navigate = useNavigate();

  useEffect(() => {
    setTokenGetter(() =>
      getAccessTokenSilently({
        authorizationParams: { audience: import.meta.env.VITE_AUTH0_AUDIENCE },
      })
    );
  }, [getAccessTokenSilently]);

  useEffect(() => {
    if (!isAuthenticated) return;

    api.getUser().then((profile) => {
      if (profile) {
        setUser(profile);
      } else {
        navigate('/onboarding');
      }
    }).catch((err) => {
      console.error('Failed to load user profile:', err);
    });
  }, [isAuthenticated, navigate, setUser]);

  return (
    <Routes>
      <Route path="/" element={<Home />} />
      <Route path="/onboarding" element={<ProtectedRoute><Onboarding /></ProtectedRoute>} />
      <Route path="/leagues" element={<ProtectedRoute><AuthenticatedLayout><Leagues /></AuthenticatedLayout></ProtectedRoute>} />
      <Route path="/leagues/create" element={<ProtectedRoute><AuthenticatedLayout><LeagueCreate /></AuthenticatedLayout></ProtectedRoute>} />
      <Route path="/leagues/:id" element={<ProtectedRoute><AuthenticatedLayout><LeagueDetail /></AuthenticatedLayout></ProtectedRoute>} />
      <Route path="/leagues/:id/draft" element={<ProtectedRoute><DraftRoom /></ProtectedRoute>} />
      <Route path="/admin" element={<AdminRoute><AuthenticatedLayout><Admin /></AuthenticatedLayout></AdminRoute>} />
      <Route path="/admin/seasons/:id" element={<AdminRoute><AuthenticatedLayout><SeasonDetail /></AuthenticatedLayout></AdminRoute>} />
      <Route path="/profile" element={<ProtectedRoute><AuthenticatedLayout><Profile /></AuthenticatedLayout></ProtectedRoute>} />
    </Routes>
  );
}
