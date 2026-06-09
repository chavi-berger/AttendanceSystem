import type { PropsWithChildren } from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';

export function AppLayout({ children }: PropsWithChildren) {
  const { employee, logout } = useAuth();
  const navigate = useNavigate();
  const isManagerOrAdmin = employee?.role === 'Manager' || employee?.role === 'Admin';
  const isAdmin = employee?.role === 'Admin';

  const handleLogout = async () => {
    await logout();
    navigate('/login', { replace: true });
  };

  const navClass = ({ isActive }: { isActive: boolean }) => `nav-item${isActive ? ' active' : ''}`;

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <span className="sidebar-brand-dot" />
          Attendance
        </div>

        <nav className="sidebar-nav">
          <NavLink to="/" end className={navClass}>
            <span className="nav-item-icon">◫</span> Dashboard
          </NavLink>
          <NavLink to="/history" className={navClass}>
            <span className="nav-item-icon">≣</span> History
          </NavLink>
          {isManagerOrAdmin && (
            <NavLink to="/admin" className={navClass}>
              <span className="nav-item-icon">◎</span> Admin Panel
            </NavLink>
          )}
          {isAdmin && (
            <NavLink to="/admin/users" className={navClass}>
              <span className="nav-item-icon">⚙</span> User Management
            </NavLink>
          )}
        </nav>

        <div className="sidebar-footer">
          {employee && (
            <div className="sidebar-user">
              <div className="sidebar-user-name">{employee.name}</div>
              <div className="sidebar-user-role">{employee.role}</div>
            </div>
          )}
          <button className="btn btn-ghost btn-block btn-sm" onClick={handleLogout}>
            Log out
          </button>
        </div>
      </aside>

      <main className="main">
        <div className="main-inner">{children}</div>
      </main>
    </div>
  );
}

export default AppLayout;
