import { AppHeader } from '../components/common/AppHeader';
import { AdminDashboard } from '../components/AdminDashboard/AdminDashboard';

export function AdminPage() {
  return (
    <div className="page">
      <AppHeader />
      <main className="page-content">
        <AdminDashboard />
      </main>
    </div>
  );
}

export default AdminPage;
