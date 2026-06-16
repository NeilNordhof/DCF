import './index.css';
import React from 'react';
import ReactDOM from 'react-dom/client';
import { createBrowserRouter, RouterProvider } from 'react-router-dom';
import App, { AuthenticatedLayout } from './App';
import { AdminRoute } from './components/AdminRoute';
import { ProtectedRoute } from './components/ProtectedRoute';
import { DevAuthProvider } from './context/DevAuthContext';
import { UserProvider } from './context/UserContext';
import { Admin } from './pages/Admin';
import { DraftRoom } from './pages/DraftRoom';
import { Home } from './pages/Home';
import { LeagueCreate } from './pages/LeagueCreate';
import { LeagueDetail } from './pages/LeagueDetail';
import { Leagues } from './pages/Leagues';
import { Onboarding } from './pages/Onboarding';
import { Profile } from './pages/Profile';
import { SeasonDetail } from './pages/SeasonDetail';
import { Unsubscribe } from './pages/Unsubscribe';

const router = createBrowserRouter([
  {
    element: <App />,
    children: [
      { path: '/', element: <Home /> },
      { path: '/unsubscribe', element: <Unsubscribe /> },
      { path: '/onboarding', element: <ProtectedRoute><Onboarding /></ProtectedRoute> },
      { path: '/leagues', element: <ProtectedRoute><AuthenticatedLayout><Leagues /></AuthenticatedLayout></ProtectedRoute> },
      { path: '/leagues/create', element: <ProtectedRoute><AuthenticatedLayout><LeagueCreate /></AuthenticatedLayout></ProtectedRoute> },
      { path: '/leagues/:id', element: <ProtectedRoute><AuthenticatedLayout><LeagueDetail /></AuthenticatedLayout></ProtectedRoute> },
      { path: '/leagues/:id/draft', element: <ProtectedRoute><DraftRoom /></ProtectedRoute> },
      { path: '/admin', element: <AdminRoute><AuthenticatedLayout><Admin /></AuthenticatedLayout></AdminRoute> },
      { path: '/admin/seasons/:id', element: <AdminRoute><AuthenticatedLayout><SeasonDetail /></AuthenticatedLayout></AdminRoute> },
      { path: '/profile', element: <ProtectedRoute><AuthenticatedLayout><Profile /></AuthenticatedLayout></ProtectedRoute> },
    ],
  },
]);

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <UserProvider>
      <DevAuthProvider>
        <RouterProvider router={router} />
      </DevAuthProvider>
    </UserProvider>
  </React.StrictMode>
);
