import type { AttendanceRecord } from '../../types/attendance';
import { formatZurichDate, formatZurichTime } from '../../utils/dateUtils';
import { StatusBadge } from '../common/StatusBadge';

interface HistoryRowProps {
  record: AttendanceRecord;
}

export function HistoryRow({ record }: HistoryRowProps) {
  return (
    <tr>
      <td>{formatZurichDate(record.clockInUtc)}</td>
      <td>{formatZurichTime(record.clockInUtc)}</td>
      <td>{record.clockOutUtc ? formatZurichTime(record.clockOutUtc) : '—'}</td>
      <td>{record.durationFormatted}</td>
      <td>
        <StatusBadge record={record} />
      </td>
    </tr>
  );
}

export default HistoryRow;
