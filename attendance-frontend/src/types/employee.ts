import type { Role } from './auth';

export interface EmployeeListItem {
  id: string;
  fullName: string;
  email: string;
  badgeNumber: string;
  role: Role;
  isActive: boolean;
}

export interface CreateEmployeeInput {
  fullName: string;
  email: string;
  badgeNumber: string;
  password: string;
  role: Role;
}
