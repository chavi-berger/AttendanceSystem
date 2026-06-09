import { AppLayout } from '../components/common/AppLayout';
import { AdminDashboard } from '../components/AdminDashboard/AdminDashboard';

export function AdminPage() {
  return (
    <AppLayout>
      <AdminDashboard />
    </AppLayout>
  );
}

export default AdminPage;
