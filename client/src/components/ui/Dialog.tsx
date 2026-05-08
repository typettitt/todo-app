import { useEffect, useId, useRef } from 'react';
import type { KeyboardEvent, MouseEvent, ReactNode } from 'react';
import styles from './Dialog.module.css';

export interface DialogProps {
  open: boolean;
  title: string;
  description?: string;
  actions?: ReactNode;
  onClose: () => void;
  children?: ReactNode;
  /** Forwarded to the rendered <dialog> element. */
  'data-testid'?: string;
}

/**
 * Dialog — native `<dialog>` wrapper. Honors:
 *   - imperative `showModal()` / `close()` based on the `open` prop;
 *   - native `close` event (ESC) → onClose;
 *   - backdrop click (event.target === dialogRef) → onClose;
 *   - focus restore to the previously-focused element on close;
 *   - autofocus to first focusable inside on open.
 */
export function Dialog({
  open,
  title,
  description,
  actions,
  onClose,
  children,
  'data-testid': dataTestId,
}: DialogProps) {
  const dialogRef = useRef<HTMLDialogElement | null>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const titleId = useId();
  const descriptionId = useId();

  // Drive showModal/close from the `open` prop.
  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && !dialog.open) {
      previouslyFocusedRef.current = document.activeElement as HTMLElement | null;
      try {
        dialog.showModal();
      } catch {
        // showModal throws if already open; harmless.
      }
      // Focus the first focusable element inside the dialog.
      const focusables = dialog.querySelectorAll<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      );
      const first = focusables[0];
      if (first) {
        first.focus();
      } else {
        dialog.focus();
      }
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  // Wire the native close event (ESC + .close()) → onClose, restore focus.
  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    const handleClose = () => {
      const previous = previouslyFocusedRef.current;
      if (previous && typeof previous.focus === 'function') {
        previous.focus();
      }
      onClose();
    };
    dialog.addEventListener('close', handleClose);
    return () => {
      dialog.removeEventListener('close', handleClose);
    };
  }, [onClose]);

  useEffect(() => {
    if (!open) return;

    const handleDocumentKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        dialogRef.current?.close();
      }
    };

    document.addEventListener('keydown', handleDocumentKeyDown);
    return () => {
      document.removeEventListener('keydown', handleDocumentKeyDown);
    };
  }, [open]);

  function handleClick(event: MouseEvent<HTMLDialogElement>) {
    if (event.target === dialogRef.current) {
      dialogRef.current?.close();
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDialogElement>) {
    if (event.key === 'Escape') {
      event.preventDefault();
      dialogRef.current?.close();
    }
  }

  return (
    <dialog
      ref={dialogRef}
      className={styles.dialog}
      aria-labelledby={titleId}
      aria-describedby={description ? descriptionId : undefined}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      tabIndex={-1}
      data-testid={dataTestId}
    >
      <header className={styles.header}>
        <h2 className={`${styles.title} mono-uppercase`} id={titleId}>
          {title}
        </h2>
        {description ? (
          <p className={styles.description} id={descriptionId}>
            {description}
          </p>
        ) : null}
      </header>
      <section className={styles.body}>{children}</section>
      {actions ? <footer className={styles.footer}>{actions}</footer> : null}
    </dialog>
  );
}

export default Dialog;
