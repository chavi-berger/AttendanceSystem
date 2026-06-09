interface LoadingSpinnerProps {
  size?: number;
  label?: string;
}

export function LoadingSpinner({ size = 20, label }: LoadingSpinnerProps) {
  return (
    <span className="spinner-wrap" role="status" aria-live="polite">
      <span
        className="spinner"
        style={{ width: size, height: size, borderWidth: Math.max(2, size / 8) }}
      />
      {label && <span className="spinner-label">{label}</span>}
    </span>
  );
}

export default LoadingSpinner;
