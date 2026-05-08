import { forwardRef, useState } from 'react';
import type { InputHTMLAttributes, TextareaHTMLAttributes } from 'react';
import styles from './TextInput.module.css';

interface CommonFieldProps {
  label: string;
  id: string;
  error?: string;
  /** Small mono-uppercase context line below the input. */
  caption?: string;
}

export type TextInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'style'> &
  CommonFieldProps;

export type TextareaProps = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'style'> &
  CommonFieldProps;

/**
 * TextInput — labeled `<input>` primitive. Forwards refs so RHF `register`
 * works directly. `style` deliberately stripped from `...rest` so consumers
 * can't override token-driven visuals.
 */
export const TextInput = forwardRef<HTMLInputElement, TextInputProps>(function TextInput(
  { label, id, error, caption, className, type, ...rest },
  ref,
) {
  const errorId = error ? `${id}-error` : undefined;
  const captionId = caption ? `${id}-caption` : undefined;
  const describedBy =
    [errorId, captionId, rest['aria-describedby']].filter(Boolean).join(' ') || undefined;
  const isPassword = type === 'password';
  const [revealed, setRevealed] = useState(false);
  const effectiveType = isPassword && revealed ? 'text' : type;

  return (
    <div className={[styles.field, className].filter(Boolean).join(' ')}>
      <label className={`${styles.label} mono-uppercase`} htmlFor={id}>
        {label}
      </label>
      <div className={isPassword ? styles.controlWrap : undefined}>
        <input
          {...rest}
          ref={ref}
          id={id}
          type={effectiveType}
          className={`${styles.control} ${isPassword ? styles.controlWithAffix : ''}`}
          aria-invalid={error ? true : rest['aria-invalid']}
          aria-describedby={describedBy}
        />
        {isPassword ? (
          <button
            aria-label={revealed ? 'Hide password' : 'Show password'}
            aria-pressed={revealed}
            className={styles.affixButton}
            onClick={() => setRevealed((value) => !value)}
            tabIndex={-1}
            type="button"
          >
            {revealed ? <EyeOffIcon /> : <EyeIcon />}
          </button>
        ) : null}
      </div>
      {caption ? (
        <span className={`${styles.caption} mono-uppercase`} id={captionId}>
          {caption}
        </span>
      ) : null}
      {error ? (
        <span className={`${styles.error} mono-uppercase`} id={errorId} role="alert">
          {error}
        </span>
      ) : null}
    </div>
  );
});

function EyeIcon() {
  return (
    <svg aria-hidden="true" focusable="false" height="16" viewBox="0 0 24 24" width="16">
      <path
        d="M12 5c-5 0-9 4.5-10 7 1 2.5 5 7 10 7s9-4.5 10-7c-1-2.5-5-7-10-7Zm0 11a4 4 0 1 1 0-8 4 4 0 0 1 0 8Zm0-2a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z"
        fill="currentColor"
      />
    </svg>
  );
}

function EyeOffIcon() {
  return (
    <svg aria-hidden="true" focusable="false" height="16" viewBox="0 0 24 24" width="16">
      <path
        d="m3.3 4.7 16 16 1.4-1.4-2.5-2.5C20 15.5 21.5 13.6 22 12c-1-2.5-5-7-10-7-1.7 0-3.3.4-4.7 1L4.7 3.3 3.3 4.7Zm6.4 6.4 3.2 3.2A2 2 0 0 1 9.7 11.1Zm2.3-3.1c.4-.1.7-.1 1 0 5 0 9 4.5 10 7-.4 1.1-1.5 2.5-3 3.7l-2.4-2.4A4 4 0 0 0 12 8c-.4 0-.8 0-1.1.1L9.4 6.5c.8-.3 1.7-.5 2.6-.5Zm-9 5c.7-1.6 2.2-3.4 4.2-4.7l1.5 1.5A4 4 0 0 0 12 16c.6 0 1.2-.1 1.7-.3l1.5 1.5C13.7 17.7 12.3 18 11 18c-3.7 0-7-3-8-5Z"
        fill="currentColor"
      />
    </svg>
  );
}

/**
 * Textarea — labeled `<textarea>` primitive with the same contract as
 * TextInput. Forwards refs; `style` excluded from pass-through.
 */
export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { label, id, error, caption, className, ...rest },
  ref,
) {
  const errorId = error ? `${id}-error` : undefined;
  const captionId = caption ? `${id}-caption` : undefined;
  const describedBy =
    [errorId, captionId, rest['aria-describedby']].filter(Boolean).join(' ') || undefined;

  return (
    <div className={[styles.field, className].filter(Boolean).join(' ')}>
      <label className={`${styles.label} mono-uppercase`} htmlFor={id}>
        {label}
      </label>
      <textarea
        {...rest}
        ref={ref}
        id={id}
        className={`${styles.control} ${styles.textarea}`}
        aria-invalid={error ? true : rest['aria-invalid']}
        aria-describedby={describedBy}
      />
      {caption ? (
        <span className={`${styles.caption} mono-uppercase`} id={captionId}>
          {caption}
        </span>
      ) : null}
      {error ? (
        <span className={`${styles.error} mono-uppercase`} id={errorId} role="alert">
          {error}
        </span>
      ) : null}
    </div>
  );
});

export default TextInput;
