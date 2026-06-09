import type { PropsWithChildren } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import type { Role } from '../../types/auth';

interface ProtectedRouteProps {
  roles?: Role[];
}

export function ProtectedRoute({ roles, children }: PropsWithChildren<ProtectedRouteProps>) {
  const { isAuthenticated, employee } = useAuth();

  if (!isAuthenticated) return <Navigate to="/login" replace />;

  if (roles && employee && !roles.includes(employee.role)) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}

export default ProtectedRoute;
