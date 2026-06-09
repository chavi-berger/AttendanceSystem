import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { attendanceApi } from '../../api/attendanceApi';
import { employeesApi } from '../../api/employeesApi';
import type { ActiveEmployeeDto } from '../../types/attendance';
import { useToastStore } from '../../store/attendanceStore';
import { getErrorMessage } from '../../utils/errorUtils';
import { LoadingSpinner } from '../common/LoadingSpinner';
import { ErrorMessage } from '../common/ErrorMessage';
import { ConfirmDialog } from '../ClockButton/ConfirmDialog';
import { ActiveEmployeesList } from './ActiveEmployeesList';
import { AttendanceHistory } from '../AttendanceHistory/AttendanceHistory';

/** Parses a "3h 22m" duration into total minutes. */
function durationToMinutes(d: string): number {
  const h = /(\d+)\s*h/.exec(d);
  const m = /(\d+)\s*m/.exec(d);
  return (h ? Number(h[1]) : 0) * 60 + (m ? Number(m[1]) : 0);
}

function formatMinutes(total: number): string {
  return `${Math.floor(total / 60)}h ${total % 60}m`;
}

export function AdminDashboard() {
  const showToast = useToastStore((s) => s.showToast);
  const queryClient = useQueryClient();
  const [target, setTarget] = useState<ActiveEmployeeDto | null>(null);
  const [selectedEmployeeId, setSelectedEmployeeId] = useState('');

  const query = useQuery({
    queryKey: ['attendance', 'active'],
    queryFn: () => attendanceApi.getActiveEmployees().then((r) => r.data),
    refetchInterval: 30_000,
  });

  const employeesQuery = useQuery({
    queryKey: ['employees', 'list'],
    queryFn: () => employeesApi.list().then((r) => r.data),
  });

  const forceClockOut = useMutation({
    mutationFn: (employeeId: string) => attendanceApi.clockOut(employeeId).then((r) => r.data),
    onSuccess: (result) => {
      showToast('success', `Clocked out employee. Duration: ${result.durationFormatted}`);
      queryClient.invalidateQueries({ queryKey: ['attendance', 'active'] });
    },
    onError: (error) => showToast('error', getErrorMessage(error)),
  });

  const employees = query.data ?? [];
  const clockedInNow = employees.length;
  const avgMinutes =
    clockedInNow === 0
      ? 0
      : Math.round(employees.reduce((sum, e) => sum + durationToMinutes(e.durationSoFar), 0) / clockedInNow);

  const confirmForceClockOut = async () => {
    if (target) {
      try {
        await forceClockOut.mutateAsync(target.employeeId);
      } catch {
        // error surfaced via toast in onError
      }
    }
    setTarget(null);
  };

  const directory = employeesQuery.data ?? [];

  return (
    <div>
      <h1 className="page-title">Admin Panel</h1>

      <div className="cards">
        <div className="card stat">
          <span className="stat-value">{clockedInNow}</span>
          <span className="stat-label">Clocked in now</span>
        </div>
        <div className="card stat">
          <span className="stat-value">{formatMinutes(avgMinutes)}</span>
          <span className="stat-label">Avg duration (active)</span>
        </div>
        <div className="card stat">
          <span className="stat-value">{query.isFetching ? '…' : '30s'}</span>
          <span className="stat-label">Auto-refresh</span>
        </div>
      </div>

      <section className="panel">
        <div className="panel-header"><h2>Active Employees</h2></div>
        {query.isLoading && <LoadingSpinner size={24} label="Loading active employees…" />}
        {query.isError && <ErrorMessage message={getErrorMessage(query.error)} />}
        {!query.isLoading && !query.isError && (
          <ActiveEmployeesList employees={employees} onForceClockOut={setTarget} />
        )}
      </section>

      <section className="panel section-gap">
        <div className="panel-header">
          <h2>Employee History</h2>
          <div className="field" style={{ marginBottom: 0, minWidth: 240 }}>
            <span className="field-label">Select an employee</span>
            <select value={selectedEmployeeId} onChange={(e) => setSelectedEmployeeId(e.target.value)}>
              <option value="">— Choose an employee —</option>
              {directory
                .filter((emp) => emp.isActive)
                .map((emp) => (
                  <option key={emp.id} value={emp.id}>
                    {emp.fullName} ({emp.badgeNumber})
                  </option>
                ))}
            </select>
          </div>
        </div>
        {employeesQuery.isError && <ErrorMessage message={getErrorMessage(employeesQuery.error)} />}
        {!selectedEmployeeId && (
          <p className="empty-state">Select an employee above to view their attendance history.</p>
        )}
      </section>

      {selectedEmployeeId && (
        <div className="section-gap">
          <AttendanceHistory employeeId={selectedEmployeeId} title="Selected Employee History" />
        </div>
      )}

      <ConfirmDialog
        open={target !== null}
        loading={forceClockOut.isPending}
        title="Force clock-out"
        message={`Force clock-out for ${target?.employeeName ?? ''}?`}
        confirmLabel="Force clock-out"
        onConfirm={confirmForceClockOut}
        onCancel={() => setTarget(null)}
      />
    </div>
  );
}

export default AdminDashboard;
