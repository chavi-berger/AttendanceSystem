import { create } from 'zustand';
import { authApi } from '../api/authApi';
import type { Employee } from '../types/auth';

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  employee: Employee | null;
  isAuthenticated: boolean;

  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  refreshAccessToken: () => Promise<void>;
}

// Tokens live in memory only (Zustand state) — NOT localStorage — to limit XSS exposure.
// A full page refresh clears them, so the user must log in again. This is intentional.
export const useAuthStore = create<AuthState>((set, get) => ({
  accessToken: null,
  refreshToken: null,
  employee: null,
  isAuthenticated: false,

  login: async (email, password) => {
    const { data } = await authApi.login(email, password);
    set({
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      employee: data.employee,
      isAuthenticated: true,
    });
  },

  logout: async () => {
    try {
      if (get().accessToken) await authApi.logout();
    } catch {
      // Best-effort server-side logout; clear local state regardless.
    }
    set({ accessToken: null, refreshToken: null, employee: null, isAuthenticated: false });
  },

  refreshAccessToken: async () => {
    const current = get().refreshToken;
    if (!current) throw new Error('No refresh token available');
    const { data } = await authApi.refresh(current);
    set({ accessToken: data.accessToken, refreshToken: data.refreshToken });
  },
}));
