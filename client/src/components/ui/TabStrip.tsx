import { useRef } from 'react';
import type { KeyboardEvent, ReactNode } from 'react';
import styles from './TabStrip.module.css';

export interface TabStripTab {
  id: string;
  label: ReactNode;
  testId?: string;
}

export interface TabStripProps {
  tabs: TabStripTab[];
  activeId: string;
  onChange: (id: string) => void;
  'aria-label': string;
}

/**
 * TabStrip — sharp-cornered segmented control with horizontal-scroll on
 * overflow. Keyboard: ArrowLeft/Right (with focus moving to the new tab and
 * `onChange` firing immediately), Home/End jump to ends.
 */
export function TabStrip({ tabs, activeId, onChange, 'aria-label': ariaLabel }: TabStripProps) {
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);

  function focusAndSelect(index: number) {
    const next = tabs[index];
    if (!next) return;
    onChange(next.id);
    // Focus moves on the next tick once React updates aria-selected.
    queueMicrotask(() => {
      tabRefs.current[index]?.focus();
    });
  }

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    if (event.key === 'ArrowRight') {
      event.preventDefault();
      focusAndSelect((index + 1) % tabs.length);
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      focusAndSelect((index - 1 + tabs.length) % tabs.length);
    } else if (event.key === 'Home') {
      event.preventDefault();
      focusAndSelect(0);
    } else if (event.key === 'End') {
      event.preventDefault();
      focusAndSelect(tabs.length - 1);
    }
  }

  return (
    <div role="tablist" aria-label={ariaLabel} className={styles.tablist}>
      {tabs.map((tab, index) => {
        const selected = tab.id === activeId;
        return (
          <button
            key={tab.id}
            ref={(node) => {
              tabRefs.current[index] = node;
            }}
            type="button"
            role="tab"
            aria-selected={selected}
            tabIndex={selected ? 0 : -1}
            data-testid={tab.testId}
            className={`${styles.tab} mono-uppercase`}
            onClick={() => onChange(tab.id)}
            onKeyDown={(event) => handleKeyDown(event, index)}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

export default TabStrip;
