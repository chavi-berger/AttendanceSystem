import client from './client';
import type { CreateEmployeeInput, EmployeeListItem } from '../types/employee';
import type { Role } from '../types/auth';

export const employeesApi = {
  list: () => client.get<EmployeeListItem[]>('/api/employees'),
  create: (input: CreateEmployeeInput) => client.post<EmployeeListItem>('/api/employees', input),
  changeRole: (id: string, role: Role) => client.put<EmployeeListItem>(`/api/employees/${id}/role`, { role }),
  deactivate: (id: string) => client.put<EmployeeListItem>(`/api/employees/${id}/deactivate`),
  activate: (id: string) => client.put<EmployeeListItem>(`/api/employees/${id}/activate`),
};
