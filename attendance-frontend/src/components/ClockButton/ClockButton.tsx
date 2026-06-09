import { useState } from 'react';
import { useAttendance } from '../../hooks/useAttendance';
import { useNetworkStatus } from '../../hooks/useNetworkStatus';
import { useCurrentTime } from '../../hooks/useCurrentTime';
import { formatElapsed, formatZurichClock, formatZurichDate, formatZurichTime } from '../../utils/dateUtils';
import { LoadingSpinner } from '../common/LoadingSpinner';
import { ConfirmDialog } from './ConfirmDialog';

export function ClockButton() {
  const { status, isLoading, clockIn, clockOut, isClockingIn, isClockingOut } = useAttendance();
  const { isOnline } = useNetworkStatus();
  // Live cosmetic clock, anchored to the backend's current Zurich time and ticking every second.
  const now = useCurrentTime(status?.currentZurichTime);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const isClockedIn = status?.isClockedIn ?? false;
  const pending = isClockingIn || isClockingOut;
  const disabled = !isOnline || pending || isLoading;

  const handleConfirm = async () => {
    try {
      if (isClockedIn) await clockOut(undefined);
      else await clockIn(undefined);
    } catch {
      // Errors are surfaced as toasts by useAttendance; just close the dialog.
    } finally {
      setConfirmOpen(false);
    }
  };

  if (isLoading) {
    return (
      <div className="clock-wrap">
        <LoadingSpinner size={28} label="Loading status…" />
      </div>
    );
  }

  return (
    <div className="clock-wrap">
      <button
        className={`clock-circle${isClockedIn ? ' is-in' : ''}`}
        disabled={disabled}
        onClick={() => setConfirmOpen(true)}
      >
        {pending ? (
          <LoadingSpinner size={22} />
        ) : isClockedIn ? (
          <>
            <span className="clock-circle-label">Clock Out</span>
            <span className="clock-circle-duration">{now ? formatElapsed(status?.clockInTime, now) : status?.durationSoFar ?? '—'}</span>
            <span className="clock-circle-sub">since {formatZurichTime(status?.clockInTime)}</span>
          </>
        ) : (
          <>
            <span className="clock-circle-label">Clock In</span>
            <span className="clock-circle-sub">{now ? formatZurichClock(now) : 'Fetching…'}</span>
          </>
        )}
      </button>

      <div className="clock-now-line">
        Zurich time:{' '}
        <b>{now ? `${formatZurichDate(now.toISOString())} · ${formatZurichClock(now)}` : 'Fetching…'}</b>
      </div>

      {!isOnline && <span className="clock-offline-hint">Clock actions are disabled while offline.</span>}

      <ConfirmDialog
        open={confirmOpen}
        loading={pending}
        title={isClockedIn ? 'Confirm Clock Out' : 'Confirm Clock In'}
        message={
          isClockedIn
            ? `You are about to clock out. Duration: ${status?.durationSoFar ?? '—'}. Confirm?`
            : 'You are about to clock in. The official time will be fetched from the Europe/Zurich time server.'
        }
        confirmLabel={isClockedIn ? 'Clock Out' : 'Clock In'}
        onConfirm={handleConfirm}
        onCancel={() => setConfirmOpen(false)}
      />
    </div>
  );
}

export default ClockButton;
