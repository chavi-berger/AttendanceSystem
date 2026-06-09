export type Role = 'Employee' | 'Manager' | 'Admin';

export interface Employee {
  id: string;
  name: string;
  role: Role;
  badgeNumber: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  employee: Employee;
}

export interface RefreshResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}
