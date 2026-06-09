import type { AttendanceRecord } from '../../types/attendance';
import { statusLabel } from '../../utils/dateUtils';

interface StatusBadgeProps {
  record: AttendanceRecord;
}

const CLASS_BY_LABEL: Record<string, string> = {
  Active: 'badge badge-active',
  Completed: 'badge badge-completed',
  'Auto-closed': 'badge badge-auto',
  Corrected: 'badge badge-corrected',
};

export function StatusBadge({ record }: StatusBadgeProps) {
  const label = statusLabel(record);
  return <span className={CLASS_BY_LABEL[label] ?? 'badge'}>{label}</span>;
}

export default StatusBadge;
