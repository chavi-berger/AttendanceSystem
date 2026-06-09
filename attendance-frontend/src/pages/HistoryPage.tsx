import { AppLayout } from '../components/common/AppLayout';
import { AttendanceHistory } from '../components/AttendanceHistory/AttendanceHistory';

export function HistoryPage() {
  return (
    <AppLayout>
      <h1 className="page-title">My History</h1>
      <AttendanceHistory />
    </AppLayout>
  );
}

export default HistoryPage;
