import { AppHeader } from '../components/common/AppHeader';
import { ClockButton } from '../components/ClockButton/ClockButton';
import { AttendanceHistory } from '../components/AttendanceHistory/AttendanceHistory';

export function EmployeePage() {
  return (
    <div className="page">
      <AppHeader />
      <main className="page-content">
        <ClockButton />
        <AttendanceHistory />
      </main>
    </div>
  );
}

export default EmployeePage;
