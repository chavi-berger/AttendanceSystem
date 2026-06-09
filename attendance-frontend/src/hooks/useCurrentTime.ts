import { useEffect, useState } from 'react';

/**
 * Drives a live, COSMETIC clock for the UI. It is anchored to a time value fetched from the
 * backend (the external Europe/Zurich time service) and then ticks forward 1 second at a time
 * on the client between polls. Attendance is never recorded from this value.
 *
 * Pass the backend's `currentZurichTime` (ISO string). Whenever a fresh value arrives the
 * interval is cleared and restarted from the new instant. Returns null until a value is known.
 */
export function useCurrentTime(baselineIso?: string) {
  const [now, setNow] = useState<Date | null>(null);

  useEffect(() => {
    if (!baselineIso) return;

    let currentMs = new Date(baselineIso).getTime();
    setNow(new Date(currentMs));

    const id = setInterval(() => {
      currentMs += 1000;
      setNow(new Date(currentMs));
    }, 1000);

    return () => clearInterval(id);
  }, [baselineIso]);

  return now;
}
