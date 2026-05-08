import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { axe } from 'vitest-axe';
import Card from '../Card';

afterEach(() => {
  cleanup();
  document.documentElement.classList.remove('dark');
});

function setTheme(mode: 'light' | 'dark') {
  document.documentElement.classList.toggle('dark', mode === 'dark');
}

describe('Card', () => {
  it('renders default article element in light theme', () => {
    setTheme('light');
    render(<Card>Body</Card>);
    const el = screen.getByText('Body');
    expect(el.tagName).toBe('ARTICLE');
    expect(el.dataset.statusLine).toBe('none');
  });

  it('applies status-line variant in dark theme', () => {
    setTheme('dark');
    render(<Card statusLine="active">Active row</Card>);
    expect(screen.getByText('Active row').dataset.statusLine).toBe('active');
  });

  it('axe-clean in both themes', async () => {
    setTheme('light');
    const { container, unmount } = render(<Card statusLine="due-today">Due today</Card>);
    expect(await axe(container)).toHaveNoViolations();
    unmount();

    setTheme('dark');
    const dark = render(<Card statusLine="completed">Completed</Card>);
    expect(await axe(dark.container)).toHaveNoViolations();
  });

  it('renders with custom element via `as`', () => {
    render(
      <Card as="section" statusLine="priority-high">
        Section content
      </Card>,
    );
    const el = screen.getByText('Section content');
    expect(el.tagName).toBe('SECTION');
    expect(el.dataset.statusLine).toBe('priority-high');
  });

  it('forwards data-testid and click handler (focusable when interactive)', () => {
    const { rerender } = render(
      <Card as="div" data-testid="card-1" tabIndex={0}>
        Click me
      </Card>,
    );
    const el = screen.getByTestId('card-1');
    el.focus();
    expect(el).toHaveFocus();

    rerender(
      <Card as="div" data-testid="card-1" tabIndex={0} statusLine="priority-low">
        Click me
      </Card>,
    );
    expect(screen.getByTestId('card-1').dataset.statusLine).toBe('priority-low');
  });
});
