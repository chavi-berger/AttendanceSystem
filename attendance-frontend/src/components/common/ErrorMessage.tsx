interface ErrorMessageProps {
  message?: string | null;
}

export function ErrorMessage({ message }: ErrorMessageProps) {
  if (!message) return null;
  return (
    <div className="error-message" role="alert">
      {message}
    </div>
  );
}

export default ErrorMessage;
