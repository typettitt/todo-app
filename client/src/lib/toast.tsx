type ToastKind = 'error' | 'message';

export type ToastRecord = {
  id: number;
  kind: ToastKind;
  message: string;
};

type ToastListener = (items: ToastRecord[]) => void;

const AUTO_DISMISS_MS = 5000;
const MAX_VISIBLE_TOASTS = 4;

let nextToastId = 1;
let items: ToastRecord[] = [];
const listeners = new Set<ToastListener>();

export const toast = {
  error(message: string) {
    return emit('error', message);
  },
  message(message: string) {
    return emit('message', message);
  },
};

function emit(kind: ToastKind, message: string) {
  const id = nextToastId++;
  items = [...items, { id, kind, message }].slice(-MAX_VISIBLE_TOASTS);
  notify();

  if (typeof window !== 'undefined') {
    window.setTimeout(() => dismiss(id), AUTO_DISMISS_MS);
  }

  return id;
}

export function subscribeToasts(listener: ToastListener) {
  listeners.add(listener);
  listener([...items]);
  return () => {
    listeners.delete(listener);
  };
}

function dismiss(id: number) {
  items = items.filter((item) => item.id !== id);
  notify();
}

export function clearToasts() {
  items = [];
  notify();
}

function notify() {
  const snapshot = [...items];
  listeners.forEach((listener) => listener(snapshot));
}
