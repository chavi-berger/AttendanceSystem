import client from './client';
import type {
  ClockInResult,
  ClockOutResult,
  AttendanceStatus,
  AttendanceRecord,
  PagedResult,
  ActiveEmployeeDto,
} from '../types/attendance';

export const attendanceApi = {
  clockIn: (notes?: string) => client.post<ClockInResult>('/api/attendance/clock-in', { notes }),
  clockOut: (employeeId?: string) =>
    client.post<ClockOutResult>('/api/attendance/clock-out', { employeeId }),
  getStatus: () => client.get<AttendanceStatus>('/api/attendance/status'),
  getHistory: (params: { page?: number; pageSize?: number; from?: string; to?: string; employeeId?: string }) =>
    client.get<PagedResult<AttendanceRecord>>('/api/attendance/history', { params }),
  getActiveEmployees: () => client.get<ActiveEmployeeDto[]>('/api/attendance/active'),
};
