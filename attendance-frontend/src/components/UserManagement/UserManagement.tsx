import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { employeesApi } from '../../api/employeesApi';
import type { EmployeeListItem } from '../../types/employee';
import type { Role } from '../../types/auth';
import { useToastStore } from '../../store/attendanceStore';
import { getErrorMessage } from '../../utils/errorUtils';
import { LoadingSpinner } from '../common/LoadingSpinner';
import { ErrorMessage } from '../common/ErrorMessage';
import { ConfirmDialog } from '../ClockButton/ConfirmDialog';

const ROLES: Role[] = ['Employee', 'Manager', 'Admin'];

const roleBadgeClass = (role: Role) =>
  role === 'Admin' ? 'badge badge-role-admin' : role === 'Manager' ? 'badge badge-role-manager' : 'badge badge-role-employee';

export function UserManagement() {
  const showToast = useToastStore((s) => s.showToast);
  const queryClient = useQueryClient();
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['employees', 'list'] });

  const [actionError, setActionError] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);
  const [pendingRole, setPendingRole] = useState<{ emp: EmployeeListItem; role: Role } | null>(null);
  const [pendingStatus, setPendingStatus] = useState<{ emp: EmployeeListItem; activate: boolean } | null>(null);

  const list = useQuery({
    queryKey: ['employees', 'list'],
    queryFn: () => employeesApi.list().then((r) => r.data),
  });

  const roleMut = useMutation({
    mutationFn: ({ id, role }: { id: string; role: Role }) => employeesApi.changeRole(id, role).then((r) => r.data),
    onSuccess: (emp) => { showToast('success', `Role updated for ${emp.fullName}.`); invalidate(); },
    onError: (e) => setActionError(getErrorMessage(e)),
  });

  const statusMut = useMutation({
    mutationFn: ({ id, activate }: { id: string; activate: boolean }) =>
      (activate ? employeesApi.activate(id) : employeesApi.deactivate(id)).then((r) => r.data),
    onSuccess: (emp) => { showToast('success', `${emp.fullName} ${emp.isActive ? 'activated' : 'deactivated'}.`); invalidate(); },
    onError: (e) => setActionError(getErrorMessage(e)),
  });

  const employees = list.data ?? [];

  return (
    <div>
      <h1 className="page-title">User Management</h1>

      <section className="panel">
        <div className="panel-header">
          <h2>Employees</h2>
          <button className="btn btn-primary" onClick={() => setAddOpen(true)}>+ Add Employee</button>
        </div>

        <ErrorMessage message={actionError} />

        {list.isLoading && <LoadingSpinner size={24} label="Loading employees…" />}
        {list.isError && <ErrorMessage message={getErrorMessage(list.error)} />}

        {!list.isLoading && !list.isError && (
          <table className="data-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Badge</th>
                <th>Email</th>
                <th>Role</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {employees.map((emp) => (
                <tr key={emp.id}>
                  <td>{emp.fullName}</td>
                  <td className="badge-num">{emp.badgeNumber}</td>
                  <td className="muted">{emp.email}</td>
                  <td><span className={roleBadgeClass(emp.role)}>{emp.role}</span></td>
                  <td>
                    <span className={emp.isActive ? 'badge badge-in' : 'badge badge-inactive'}>
                      {emp.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td>
                    <div className="row-actions">
                      <select
                        className="inline-select"
                        value={emp.role}
                        onChange={(e) => { setActionError(null); setPendingRole({ emp, role: e.target.value as Role }); }}
                      >
                        {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                      </select>
                      {emp.isActive ? (
                        <button className="btn btn-danger btn-sm" onClick={() => { setActionError(null); setPendingStatus({ emp, activate: false }); }}>
                          Deactivate
                        </button>
                      ) : (
                        <button className="btn btn-ghost btn-sm" onClick={() => { setActionError(null); setPendingStatus({ emp, activate: true }); }}>
                          Activate
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {addOpen && (
        <AddEmployeeModal
          onClose={() => setAddOpen(false)}
          onCreated={() => { setAddOpen(false); invalidate(); }}
        />
      )}

      <ConfirmDialog
        open={pendingRole !== null}
        loading={roleMut.isPending}
        title="Change role"
        message={`Change ${pendingRole?.emp.fullName ?? ''}'s role to ${pendingRole?.role ?? ''}?`}
        confirmLabel="Change role"
        onConfirm={async () => {
          if (pendingRole) { try { await roleMut.mutateAsync({ id: pendingRole.emp.id, role: pendingRole.role }); } catch { /* inline */ } }
          setPendingRole(null);
        }}
        onCancel={() => setPendingRole(null)}
      />

      <ConfirmDialog
        open={pendingStatus !== null}
        loading={statusMut.isPending}
        title={pendingStatus?.activate ? 'Activate employee' : 'Deactivate employee'}
        message={
          pendingStatus?.activate
            ? `Reactivate ${pendingStatus?.emp.fullName ?? ''}?`
            : `Are you sure you want to deactivate ${pendingStatus?.emp.fullName ?? ''}?`
        }
        confirmLabel={pendingStatus?.activate ? 'Activate' : 'Deactivate'}
        onConfirm={async () => {
          if (pendingStatus) { try { await statusMut.mutateAsync({ id: pendingStatus.emp.id, activate: pendingStatus.activate }); } catch { /* inline */ } }
          setPendingStatus(null);
        }}
        onCancel={() => setPendingStatus(null)}
      />
    </div>
  );
}

interface AddEmployeeModalProps {
  onClose: () => void;
  onCreated: () => void;
}

function AddEmployeeModal({ onClose, onCreated }: AddEmployeeModalProps) {
  const showToast = useToastStore((s) => s.showToast);
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [badgeNumber, setBadgeNumber] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState<Role>('Employee');
  const [error, setError] = useState<string | null>(null);

  const createMut = useMutation({
    mutationFn: () => employeesApi.create({ fullName, email, badgeNumber, password, role }).then((r) => r.data),
    onSuccess: (emp) => { showToast('success', `Employee ${emp.fullName} created.`); onCreated(); },
    onError: (e) => setError(getErrorMessage(e)),
  });

  useEffect(() => {
    const handler = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onClose]);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    createMut.mutate();
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" aria-label="Add employee" onClick={(e) => e.stopPropagation()}>
        <h3 className="modal-title">Add Employee</h3>
        <form onSubmit={submit}>
          <div className="field">
            <span className="field-label">Full Name</span>
            <input value={fullName} onChange={(e) => setFullName(e.target.value)} required />
          </div>
          <div className="field">
            <span className="field-label">Email</span>
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
          </div>
          <div className="field">
            <span className="field-label">Badge Number</span>
            <input value={badgeNumber} onChange={(e) => setBadgeNumber(e.target.value)} required />
          </div>
          <div className="field">
            <span className="field-label">Password</span>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          </div>
          <div className="field">
            <span className="field-label">Role</span>
            <select value={role} onChange={(e) => setRole(e.target.value as Role)}>
              {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
            </select>
          </div>

          <ErrorMessage message={error} />

          <div className="modal-actions">
            <button type="button" className="btn btn-ghost" onClick={onClose} disabled={createMut.isPending}>Cancel</button>
            <button type="submit" className="btn btn-primary" disabled={createMut.isPending}>
              {createMut.isPending ? <LoadingSpinner size={16} /> : 'Create Employee'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export default UserManagement;
