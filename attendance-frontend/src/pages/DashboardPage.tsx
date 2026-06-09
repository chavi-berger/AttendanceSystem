import { AppLayout } from '../components/common/AppLayout';
import { ClockButton } from '../components/ClockButton/ClockButton';

export function DashboardPage() {
  return (
    <AppLayout>
      <h1 className="page-title">Dashboard</h1>
      <div className="panel">
        <ClockButton />
      </div>
    </AppLayout>
  );
}

export default DashboardPage;
