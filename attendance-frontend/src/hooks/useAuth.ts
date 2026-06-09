import { useAuthStore } from '../store/authStore';

/** Thin convenience wrapper over the auth store for components. */
export function useAuth() {
  const accessToken = useAuthStore((s) => s.accessToken);
  const employee = useAuthStore((s) => s.employee);
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const login = useAuthStore((s) => s.login);
  const logout = useAuthStore((s) => s.logout);

  return { accessToken, employee, isAuthenticated, login, logout };
}
