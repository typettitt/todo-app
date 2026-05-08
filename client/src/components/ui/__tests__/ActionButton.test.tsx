import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe } from 'vitest-axe';
import ActionButton from '../ActionButton';

afterEach(() => {
  cleanup();
  document.documentElement.classList.remove('dark');
});

function setTheme(mode: 'light' | 'dark') {
  document.documentElement.classList.toggle('dark', mode === 'dark');
}

describe('ActionButton', () => {
  it('renders mono uppercase label in light theme', () => {
    setTheme('light');
    render(<ActionButton variant="primary">Submit</ActionButton>);
    const button = screen.getByRole('button', { name: /submit/i });
    expect(button).toBeInTheDocument();
    expect(button.className).toMatch(/primary/);
    expect(button.className).toMatch(/mono-uppercase/);
  });

  it('renders in dark theme without console errors', () => {
    setTheme('dark');
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    render(<ActionButton variant="success">Save</ActionButton>);
    expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });

  it('axe-clean in both themes', async () => {
    setTheme('light');
    const { container, unmount } = render(
      <ActionButton variant="danger" caption="DELETE">
        Delete
      </ActionButton>,
    );
    expect(await axe(container)).toHaveNoViolations();
    unmount();

    setTheme('dark');
    const dark = render(
      <ActionButton variant="neutral" size="sm">
        Cancel
      </ActionButton>,
    );
    expect(await axe(dark.container)).toHaveNoViolations();
  });

  it('fires onClick on Enter and Space (keyboard accessible)', async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    render(<ActionButton onClick={onClick}>Go</ActionButton>);
    const button = screen.getByRole('button', { name: /go/i });
    button.focus();
    expect(button).toHaveFocus();

    await user.keyboard('{Enter}');
    expect(onClick).toHaveBeenCalledTimes(1);

    await user.keyboard(' ');
    expect(onClick).toHaveBeenCalledTimes(2);
  });

  it('disabled blocks click', async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    render(
      <ActionButton disabled onClick={onClick}>
        Nope
      </ActionButton>,
    );
    await user.click(screen.getByRole('button', { name: /nope/i }));
    expect(onClick).not.toHaveBeenCalled();
  });

  it('renders caption above label', () => {
    render(
      <ActionButton variant="success" caption="POST">
        Create Todo
      </ActionButton>,
    );
    expect(screen.getByText('POST')).toBeInTheDocument();
    expect(screen.getByText('Create Todo')).toBeInTheDocument();
  });

  it('aria-busy swaps label for spinner', () => {
    render(
      <ActionButton aria-busy="true" variant="primary">
        Loading
      </ActionButton>,
    );
    const button = screen.getByRole('button');
    expect(button).toHaveAttribute('aria-busy', 'true');
    // Spinner replaces children when busy.
    expect(button.querySelector('span[aria-hidden="true"]')).not.toBeNull();
  });

  it('forwards refs to the underlying button', () => {
    const ref = { current: null as HTMLButtonElement | null };
    render(<ActionButton ref={ref}>Ref</ActionButton>);
    expect(ref.current).toBeInstanceOf(HTMLButtonElement);
  });

  it('applies size class', () => {
    render(
      <ActionButton size="sm" variant="neutral">
        Small
      </ActionButton>,
    );
    const button = screen.getByRole('button');
    expect(button.className).toMatch(/sm/);
  });
});
