import { useEffect, useState } from 'react';
import styles from './Toast.module.css';
import { clearToasts, subscribeToasts, type ToastRecord } from './toast';

export function Toaster() {
  const [visibleItems, setVisibleItems] = useState<ToastRecord[]>([]);

  useEffect(() => {
    clearToasts();
    const unsubscribe = subscribeToasts(setVisibleItems);
    return () => {
      unsubscribe();
      clearToasts();
    };
  }, []);

  return (
    <section aria-label="Notifications" className={styles.viewport}>
      {visibleItems.map((item) => (
        <div
          className={`${styles.toast} ${item.kind === 'error' ? styles.error : styles.message}`}
          key={item.id}
          role={item.kind === 'error' ? 'alert' : 'status'}
        >
          {item.message}
        </div>
      ))}
    </section>
  );
}
