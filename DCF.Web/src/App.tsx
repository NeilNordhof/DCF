import { useAuth0 } from '@auth0/auth0-react';
import { useEffect } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { setTokenGetter } from './api/client';
import { AdminRoute } from './components/AdminRoute';
import { ProtectedRoute } from './components/ProtectedRoute';
import { Admin } from './pages/Admin';
import { DraftRoom } from './pages/DraftRoom';
import { Home } from './pages/Home';
import { LeagueCreate } from './pages/LeagueCreate';
import { LeagueDetail } from './pages/LeagueDetail';
import { Leagues } from './pages/Leagues';
import { Profile } from './pages/Profile';

export default function App() {
  const { getAccessTokenSilently } = useAuth0();

  useEffect(() => {
    setTokenGetter(() =>
      getAccessTokenSilently({
        authorizationParams: { audience: import.meta.env.VITE_AUTH0_AUDIENCE },
      })
    );
  }, [getAccessTokenSilently]);

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/leagues" element={<ProtectedRoute><Leagues /></ProtectedRoute>} />
        <Route path="/leagues/create" element={<ProtectedRoute><LeagueCreate /></ProtectedRoute>} />
        <Route path="/leagues/:id" element={<ProtectedRoute><LeagueDetail /></ProtectedRoute>} />
        <Route path="/leagues/:id/draft" element={<ProtectedRoute><DraftRoom /></ProtectedRoute>} />
        <Route path="/admin" element={<AdminRoute><Admin /></AdminRoute>} />
        <Route path="/profile" element={<ProtectedRoute><Profile /></ProtectedRoute>} />
      </Routes>
    </BrowserRouter>
  );
}
