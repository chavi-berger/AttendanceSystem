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
      <div className="clock-card">
        <LoadingSpinner size={28} label="Loading status…" />
      </div>
    );
  }

  return (
    <div className="clock-card">
      <div className="clock-now">
        <span className="clock-now-label">Zurich time</span>
        <span className="clock-now-value">
          {now ? `${formatZurichDate(now.toISOString())} · ${formatZurichClock(now)}` : 'Fetching…'}
        </span>
      </div>

      {isClockedIn ? (
        <div className="clock-live">
          <span className="clock-live-label">Clocked in since</span>
          <span className="clock-live-time">{formatZurichTime(status?.clockInTime)} (Zurich)</span>
          <span className="clock-live-duration">
            {now ? formatElapsed(status?.clockInTime, now) : status?.durationSoFar ?? '—'}
          </span>
        </div>
      ) : (
        <p className="clock-idle">You are currently clocked out.</p>
      )}

      <button
        className={`btn btn-clock ${isClockedIn ? 'btn-clock-out' : 'btn-clock-in'}`}
        disabled={disabled}
        onClick={() => setConfirmOpen(true)}
      >
        {pending ? (
          <LoadingSpinner size={20} />
        ) : isClockedIn ? (
          'Clock Out'
        ) : (
          'Clock In'
        )}
      </button>

      {!isOnline && <p className="clock-offline-hint">Clock actions are disabled while offline.</p>}

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
