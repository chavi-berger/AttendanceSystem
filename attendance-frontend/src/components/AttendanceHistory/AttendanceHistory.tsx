import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { attendanceApi } from '../../api/attendanceApi';
import { downloadTextFile, recordsToCsv } from '../../utils/dateUtils';
import { getErrorMessage } from '../../utils/errorUtils';
import { LoadingSpinner } from '../common/LoadingSpinner';
import { ErrorMessage } from '../common/ErrorMessage';
import { HistoryRow } from './HistoryRow';

interface AttendanceHistoryProps {
  employeeId?: string; // omit => current employee
  title?: string;
}

const PAGE_SIZE = 10;

export function AttendanceHistory({ employeeId, title = 'Attendance History' }: AttendanceHistoryProps) {
  const [page, setPage] = useState(1);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  const query = useQuery({
    queryKey: ['attendance', 'history', { page, from, to, employeeId }],
    queryFn: () =>
      attendanceApi
        .getHistory({
          page,
          pageSize: PAGE_SIZE,
          from: from ? `${from}T00:00:00` : undefined,
          to: to ? `${to}T23:59:59` : undefined,
          employeeId,
        })
        .then((r) => r.data),
  });

  const result = query.data;
  const items = result?.items ?? [];

  const exportCsv = () => {
    if (items.length === 0) return;
    downloadTextFile(`attendance-page-${page}.csv`, recordsToCsv(items));
  };

  return (
    <section className="panel">
      <div className="panel-header">
        <h2>{title}</h2>
        <div className="history-filters">
          <div className="field">
            <span className="field-label">From</span>
            <input type="date" value={from} onChange={(e) => { setFrom(e.target.value); setPage(1); }} />
          </div>
          <div className="field">
            <span className="field-label">To</span>
            <input type="date" value={to} onChange={(e) => { setTo(e.target.value); setPage(1); }} />
          </div>
          <button className="btn btn-ghost" onClick={exportCsv} disabled={items.length === 0}>
            Export CSV
          </button>
        </div>
      </div>

      {query.isLoading && <LoadingSpinner size={24} label="Loading history…" />}
      {query.isError && <ErrorMessage message={getErrorMessage(query.error)} />}

      {!query.isLoading && !query.isError && items.length === 0 && (
        <p className="empty-state">No attendance records found</p>
      )}

      {items.length > 0 && (
        <>
          <table className="data-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Clock In</th>
                <th>Clock Out</th>
                <th>Duration</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {items.map((r) => (
                <HistoryRow key={r.id} record={r} />
              ))}
            </tbody>
          </table>

          <div className="pagination">
            <button
              className="btn btn-ghost btn-sm"
              disabled={!result?.hasPreviousPage}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              ← Previous
            </button>
            <span className="pagination-info">
              Page {result?.page ?? page} of {result?.totalPages ?? 1}
            </span>
            <button
              className="btn btn-ghost btn-sm"
              disabled={!result?.hasNextPage}
              onClick={() => setPage((p) => p + 1)}
            >
              Next →
            </button>
          </div>
        </>
      )}
    </section>
  );
}

export default AttendanceHistory;
