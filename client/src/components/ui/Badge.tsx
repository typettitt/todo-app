import type { HTMLAttributes, ReactNode } from 'react';
import styles from './Badge.module.css';

export type BadgeVariant =
  | 'priority-high'
  | 'priority-medium'
  | 'priority-low'
  | 'tag'
  | 'status-active'
  | 'status-completed'
  | 'status-due-today';

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant: BadgeVariant;
  children: ReactNode;
}

/**
 * Badge — small mono-uppercase pill with 10% colored fill + 1px colored border.
 */
export function Badge({ variant, className, children, ...rest }: BadgeProps) {
  const classes = [styles.badge, styles[variant], 'mono-uppercase', className]
    .filter(Boolean)
    .join(' ');

  return (
    <span {...rest} className={classes} data-variant={variant}>
      {children}
    </span>
  );
}

export default Badge;
