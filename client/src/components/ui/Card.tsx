import { createElement } from 'react';
import type { HTMLAttributes, ReactNode } from 'react';
import styles from './Card.module.css';

export type CardStatusLine =
  | 'active'
  | 'completed'
  | 'due-today'
  | 'priority-high'
  | 'priority-medium'
  | 'priority-low'
  | 'none';

export interface CardProps extends HTMLAttributes<HTMLElement> {
  statusLine?: CardStatusLine;
  /** HTML tag to render. Defaults to `article` for semantic list rows. */
  as?: keyof HTMLElementTagNameMap;
  children?: ReactNode;
}

/**
 * Card — 1px-bordered surface with sharp corners + optional colored status
 * line across the top edge.
 */
export function Card({
  statusLine = 'none',
  as = 'article',
  className,
  children,
  ...rest
}: CardProps) {
  const classes = [styles.card, className].filter(Boolean).join(' ');
  return createElement(
    as,
    {
      ...rest,
      className: classes,
      'data-status-line': statusLine,
    },
    children,
  );
}

export default Card;
