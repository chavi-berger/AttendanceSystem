import { useToastStore } from '../../store/attendanceStore';

export function Toaster() {
  const toasts = useToastStore((s) => s.toasts);
  const dismiss = useToastStore((s) => s.dismiss);

  return (
    <div className="toaster" aria-live="polite">
      {toasts.map((t) => (
        <div key={t.id} className={`toast toast-${t.type}`} role="status" onClick={() => dismiss(t.id)}>
          {t.message}
        </div>
      ))}
    </div>
  );
}

export default Toaster;
