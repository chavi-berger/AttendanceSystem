import axios from 'axios';

/** Maps any thrown error (axios or otherwise) to a user-friendly message. */
export function getErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    // No response => network/offline/timeout.
    if (!error.response) {
      return 'No connection. Please check your internet.';
    }

    const status = error.response.status;
    const data = error.response.data as { detail?: string; title?: string } | undefined;

    switch (status) {
      case 400:
        return data?.detail || data?.title || 'The request was invalid.';
      case 401:
        return 'Your session has expired. Please log in again.';
      case 403:
        return 'You are not allowed to perform this action.';
      case 409:
        return data?.detail || 'You are already clocked in.';
      case 422:
        return data?.detail || 'This operation violates an attendance rule.';
      case 503:
        return 'Time service unavailable. Please try again in 30 seconds.';
      default:
        return data?.detail || data?.title || 'Something went wrong. Please try again.';
    }
  }

  if (error instanceof Error) return error.message;
  return 'An unexpected error occurred.';
}

/** Returns the HTTP status code for an axios error, or undefined. */
export function getStatusCode(error: unknown): number | undefined {
  return axios.isAxiosError(error) ? error.response?.status : undefined;
}
