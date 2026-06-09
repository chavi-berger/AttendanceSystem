import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';

export function AppHeader() {
  const { employee, logout } = useAuth();
  const navigate = useNavigate();
  const isManagerOrAdmin = employee?.role === 'Manager' || employee?.role === 'Admin';

  const handleLogout = async () => {
    await logout();
    navigate('/login', { replace: true });
  };

  return (
    <header className="app-header">
      <div className="app-header-left">
        <Link to="/" className="app-brand">⏱ Attendance</Link>
        {isManagerOrAdmin && <Link to="/admin" className="app-nav-link">Admin</Link>}
      </div>
      <div className="app-header-right">
        {employee && (
          <span className="app-user">
            {employee.name} · <span className="app-role">{employee.role}</span>
          </span>
        )}
        <button className="btn btn-secondary btn-sm" onClick={handleLogout}>
          Log out
        </button>
      </div>
    </header>
  );
}

export default AppHeader;
