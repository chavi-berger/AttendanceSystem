import type { ActiveEmployeeDto } from '../../types/attendance';
import { formatZurichTime } from '../../utils/dateUtils';

interface ActiveEmployeesListProps {
  employees: ActiveEmployeeDto[];
  onForceClockOut: (employee: ActiveEmployeeDto) => void;
}

export function ActiveEmployeesList({ employees, onForceClockOut }: ActiveEmployeesListProps) {
  if (employees.length === 0) {
    return <p className="empty-state">No employees are currently clocked in.</p>;
  }

  return (
    <table className="data-table">
      <thead>
        <tr>
          <th>Employee</th>
          <th>Badge</th>
          <th>Clock In (Zurich)</th>
          <th>Duration so far</th>
          <th />
        </tr>
      </thead>
      <tbody>
        {employees.map((e) => (
          <tr key={e.logId}>
            <td>{e.employeeName}</td>
            <td>{e.badgeNumber}</td>
            <td>{formatZurichTime(e.clockInUtc)}</td>
            <td>{e.durationSoFar}</td>
            <td>
              <button className="btn btn-danger btn-sm" onClick={() => onForceClockOut(e)}>
                Force clock-out
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export default ActiveEmployeesList;
