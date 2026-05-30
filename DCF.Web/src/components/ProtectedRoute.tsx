import { Navigate } from 'react-router-dom';
import { useDevAuth } from '../context/DevAuthContext';

export function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useDevAuth();
  if (isLoading) return <div>Loading...</div>;
  if (!isAuthenticated) return <Navigate to="/" replace />;
  return <>{children}</>;
}
