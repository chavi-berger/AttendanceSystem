import client from './client';
import type { LoginResponse, RefreshResponse } from '../types/auth';

export const authApi = {
  login: (email: string, password: string) =>
    client.post<LoginResponse>('/api/auth/login', { email, password }),

  refresh: (refreshToken: string) =>
    client.post<RefreshResponse>('/api/auth/refresh', { refreshToken }),

  logout: () => client.post('/api/auth/logout'),
};
