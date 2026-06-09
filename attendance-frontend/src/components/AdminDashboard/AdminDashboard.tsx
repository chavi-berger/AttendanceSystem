import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { attendanceApi } from '../../api/attendanceApi';
import type { ActiveEmployeeDto } from '../../types/attendance';
import { useToastStore } from '../../store/attendanceStore';
import { getErrorMessage } from '../../utils/errorUtils';
import { LoadingSpinner } from '../common/LoadingSpinner';
import { ErrorMessage } from '../common/ErrorMessage';
import { ConfirmDialog } from '../ClockButton/ConfirmDialog';
import { ActiveEmployeesList } from './ActiveEmployeesList';

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

  const query = useQuery({
    queryKey: ['attendance', 'active'],
    queryFn: () => attendanceApi.getActiveEmployees().then((r) => r.data),
    refetchInterval: 30_000,
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

  return (
    <section className="admin">
      <h2>Admin Dashboard</h2>

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

      {query.isLoading && <LoadingSpinner size={24} label="Loading active employees…" />}
      {query.isError && <ErrorMessage message={getErrorMessage(query.error)} />}
      {!query.isLoading && !query.isError && (
        <ActiveEmployeesList employees={employees} onForceClockOut={setTarget} />
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
    </section>
  );
}

export default AdminDashboard;
