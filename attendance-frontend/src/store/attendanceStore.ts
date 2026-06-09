import { create } from 'zustand';

export type ToastType = 'success' | 'error' | 'info';

export interface Toast {
  id: number;
  type: ToastType;
  message: string;
}

interface ToastState {
  toasts: Toast[];
  showToast: (type: ToastType, message: string, durationMs?: number) => void;
  dismiss: (id: number) => void;
}

let nextId = 1;

/** Lightweight global toast notifications used for clock-in/out feedback. */
export const useToastStore = create<ToastState>((set, get) => ({
  toasts: [],
  showToast: (type, message, durationMs = type === 'error' ? 5000 : 4000) => {
    const id = nextId++;
    set((state) => ({ toasts: [...state.toasts, { id, type, message }] }));
    setTimeout(() => get().dismiss(id), durationMs);
  },
  dismiss: (id) => set((state) => ({ toasts: state.toasts.filter((t) => t.id !== id) })),
}));
