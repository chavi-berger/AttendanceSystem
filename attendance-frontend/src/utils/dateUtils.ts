import type { AttendanceRecord } from '../types/attendance';

const ZURICH_TZ = 'Europe/Zurich';

/** Wall-clock time in Europe/Zurich (e.g. "09:14"), independent of the browser timezone. */
export function formatZurichTime(iso: string | undefined): string {
  if (!iso) return '—';
  return new Intl.DateTimeFormat('en-GB', {
    timeZone: ZURICH_TZ,
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(iso));
}

/** Date in Europe/Zurich (e.g. "15 Jan 2025"). */
export function formatZurichDate(iso: string | undefined): string {
  if (!iso) return '—';
  return new Intl.DateTimeFormat('en-GB', {
    timeZone: ZURICH_TZ,
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  }).format(new Date(iso));
}

/** Date + time in Europe/Zurich (e.g. "15 Jan 2025, 09:14"). */
export function formatZurichDateTime(iso: string | undefined): string {
  if (!iso) return '—';
  return `${formatZurichDate(iso)}, ${formatZurichTime(iso)}`;
}

/** Live Zurich wall-clock time of a Date, e.g. "14:09:37". */
export function formatZurichClock(now: Date): string {
  return new Intl.DateTimeFormat('en-GB', {
    timeZone: ZURICH_TZ,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(now);
}

/**
 * Live, client-side elapsed duration since a clock-in instant, formatted "Hh Mm Ss".
 * Cosmetic only — the authoritative duration always comes from the backend.
 */
export function formatElapsed(fromIso: string | undefined, now: Date): string {
  if (!fromIso) return '0h 0m 0s';
  const ms = Math.max(0, now.getTime() - new Date(fromIso).getTime());
  const totalSeconds = Math.floor(ms / 1000);
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = totalSeconds % 60;
  return `${h}h ${m}m ${s}s`;
}

/** Builds a CSV string from attendance records (client-side export of loaded data). */
export function recordsToCsv(records: AttendanceRecord[]): string {
  const header = ['Date', 'Clock In (Zurich)', 'Clock Out (Zurich)', 'Duration', 'Status'];
  const rows = records.map((r) => [
    formatZurichDate(r.clockInUtc),
    formatZurichTime(r.clockInUtc),
    r.clockOutUtc ? formatZurichTime(r.clockOutUtc) : '',
    r.durationFormatted,
    statusLabel(r),
  ]);
  return [header, ...rows]
    .map((cols) => cols.map((c) => `"${String(c).replace(/"/g, '""')}"`).join(','))
    .join('\n');
}

/** Human status label for a record. */
export function statusLabel(r: AttendanceRecord): string {
  if (r.isAutoTimeout) return 'Auto-closed';
  if (r.isManualCorrection) return 'Corrected';
  if (r.isOpen) return 'Active';
  return 'Completed';
}

/** Triggers a client-side file download of the given text content. */
export function downloadTextFile(filename: string, content: string, mime = 'text/csv;charset=utf-8;') {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
