export interface ClockInResult {
  logId: string;
  employeeId: string;
  clockInUtc: string; // ISO 8601 with timezone offset
  timeSource: string;
  employeeName: string;
}

export interface ClockOutResult {
  logId: string;
  clockInUtc: string;
  clockOutUtc: string;
  durationFormatted: string; // "7h 43m"
}

export interface AttendanceStatus {
  isClockedIn: boolean;
  clockInTime?: string; // matches backend AttendanceStatusDto.ClockInTime
  durationSoFar?: string;
  logId?: string;
  currentZurichTime?: string; // current Zurich time from the backend (external time service)
}

export interface AttendanceRecord {
  id: string;
  clockInUtc: string;
  clockOutUtc?: string;
  durationFormatted: string;
  isOpen: boolean;
  isManualCorrection: boolean;
  isAutoTimeout: boolean;
  clockInSource?: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface ActiveEmployeeDto {
  employeeId: string;
  employeeName: string;
  badgeNumber: string;
  clockInUtc: string;
  durationSoFar: string;
  logId: string;
}
