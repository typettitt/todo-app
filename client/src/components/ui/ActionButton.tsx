import { forwardRef } from 'react';
import type { ButtonHTMLAttributes } from 'react';
import styles from './ActionButton.module.css';

export type ActionButtonVariant = 'primary' | 'success' | 'danger' | 'neutral';
export type ActionButtonSize = 'sm' | 'md';

export interface ActionButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ActionButtonVariant;
  size?: ActionButtonSize;
  /** Small mono-uppercase context line above the label (e.g. `POST`, `DELETE`). */
  caption?: string;
}

/**
 * ActionButton — industrial primitive with a colored 1px border + 2px left bar.
 * Forwards refs so React Hook Form `register()` works downstream.
 */
const ActionButton = forwardRef<HTMLButtonElement, ActionButtonProps>(function ActionButton(
  { variant = 'neutral', size = 'md', caption, className, children, type, ...rest },
  ref,
) {
  const classes = [styles.button, styles[variant], styles[size], 'mono-uppercase', className]
    .filter(Boolean)
    .join(' ');
  const isBusy = rest['aria-busy'] === true || rest['aria-busy'] === 'true';

  return (
    <button {...rest} ref={ref} type={type ?? 'button'} className={classes}>
      {caption ? <span className={styles.caption}>{caption}</span> : null}
      <span className={styles.label}>
        {isBusy ? <span aria-hidden="true" className={styles.spinner} /> : children}
      </span>
    </button>
  );
});

export default ActionButton;
export { ActionButton };
