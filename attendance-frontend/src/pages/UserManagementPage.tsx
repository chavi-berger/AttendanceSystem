import { AppLayout } from '../components/common/AppLayout';
import { UserManagement } from '../components/UserManagement/UserManagement';

export function UserManagementPage() {
  return (
    <AppLayout>
      <UserManagement />
    </AppLayout>
  );
}

export default UserManagementPage;
