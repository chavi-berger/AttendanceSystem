import { useNetworkStatus } from '../../hooks/useNetworkStatus';

export function NetworkStatus() {
  const { isOnline } = useNetworkStatus();
  if (isOnline) return null;
  return (
    <div className="network-banner" role="alert">
      ⚠️ No internet connection. Clock-in/out is unavailable.
    </div>
  );
}

export default NetworkStatus;
