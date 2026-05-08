import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { axe } from 'vitest-axe';
import Badge from '../Badge';

afterEach(() => {
  cleanup();
  document.documentElement.classList.remove('dark');
});

function setTheme(mode: 'light' | 'dark') {
  document.documentElement.classList.toggle('dark', mode === 'dark');
}

describe('Badge', () => {
  it('renders priority-high variant in light theme with mono uppercase', () => {
    setTheme('light');
    render(<Badge variant="priority-high">High</Badge>);
    const badge = screen.getByText('High');
    expect(badge.dataset.variant).toBe('priority-high');
    expect(badge.className).toMatch(/mono-uppercase/);
  });

  it('renders status-active variant in dark theme', () => {
    setTheme('dark');
    render(<Badge variant="status-active">Active</Badge>);
    expect(screen.getByText('Active').dataset.variant).toBe('status-active');
  });

  it('axe-clean in both themes', async () => {
    setTheme('light');
    const { container, unmount } = render(<Badge variant="tag">phase-6</Badge>);
    expect(await axe(container)).toHaveNoViolations();
    unmount();

    setTheme('dark');
    const dark = render(<Badge variant="status-completed">Completed</Badge>);
    expect(await axe(dark.container)).toHaveNoViolations();
  });

  it('applies variant class for each variant', () => {
    const variants = [
      'priority-high',
      'priority-medium',
      'priority-low',
      'tag',
      'status-active',
      'status-completed',
      'status-due-today',
    ] as const;

    for (const variant of variants) {
      const { unmount } = render(<Badge variant={variant}>Label</Badge>);
      const el = screen.getByText('Label');
      expect(el.dataset.variant).toBe(variant);
      unmount();
    }
  });

  it('forwards extra props (data-testid, className)', () => {
    render(
      <Badge variant="tag" data-testid="my-badge" className="extra">
        Tag
      </Badge>,
    );
    const badge = screen.getByTestId('my-badge');
    expect(badge.className).toMatch(/extra/);
  });
});
