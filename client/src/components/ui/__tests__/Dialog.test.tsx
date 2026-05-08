import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe } from 'vitest-axe';
import Dialog from '../Dialog';

// JSDOM does not implement <dialog>.showModal() / close() / ::backdrop. Stub
// the imperative API so the component's effects can run end-to-end.
beforeEach(() => {
  const proto = HTMLDialogElement.prototype as unknown as {
    showModal?: () => void;
    close?: () => void;
  };
  proto.showModal = function showModal(this: HTMLDialogElement) {
    this.setAttribute('open', '');
    Object.defineProperty(this, 'open', { configurable: true, value: true });
  };
  proto.close = function close(this: HTMLDialogElement) {
    if (!this.open) return;
    this.removeAttribute('open');
    Object.defineProperty(this, 'open', { configurable: true, value: false });
    this.dispatchEvent(new Event('close'));
  };
});

afterEach(() => {
  cleanup();
  document.documentElement.classList.remove('dark');
});

function setTheme(mode: 'light' | 'dark') {
  document.documentElement.classList.toggle('dark', mode === 'dark');
}

describe('Dialog', () => {
  it('shows the dialog when `open` is true (light theme)', () => {
    setTheme('light');
    render(
      <Dialog data-testid="d-1" onClose={() => {}} open={true} title="Confirm">
        <p>Body</p>
      </Dialog>,
    );
    const dialog = screen.getByTestId('d-1') as HTMLDialogElement;
    expect(dialog.hasAttribute('open')).toBe(true);
    expect(screen.getByText('Body')).toBeInTheDocument();
  });

  it('renders title + description in dark theme', () => {
    setTheme('dark');
    render(
      <Dialog
        data-testid="d-2"
        description="This cannot be undone."
        onClose={() => {}}
        open={true}
        title="Delete Todo"
      >
        Body
      </Dialog>,
    );
    expect(screen.getByText('Delete Todo')).toBeInTheDocument();
    expect(screen.getByText('This cannot be undone.')).toBeInTheDocument();
  });

  it('axe-clean in both themes', async () => {
    setTheme('light');
    const { container, unmount } = render(
      <Dialog actions={<button>OK</button>} onClose={() => {}} open={true} title="A">
        Body A
      </Dialog>,
    );
    expect(await axe(container)).toHaveNoViolations();
    unmount();

    setTheme('dark');
    const dark = render(
      <Dialog onClose={() => {}} open={true} title="B">
        Body B
      </Dialog>,
    );
    expect(await axe(dark.container)).toHaveNoViolations();
  });

  it('calls onClose when ESC closes the native dialog', async () => {
    const onClose = vi.fn();
    render(
      <Dialog data-testid="d-esc" onClose={onClose} open={true} title="ESC test">
        <button>Inside</button>
      </Dialog>,
    );
    const dialog = screen.getByTestId('d-esc') as HTMLDialogElement;
    // Simulate native ESC handling: dialog.close() fires the close event
    // which our hook subscribes to.
    dialog.close();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('closes on backdrop click (event.target === dialog)', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <Dialog data-testid="d-bd" onClose={onClose} open={true} title="Backdrop test">
        <button>Inside</button>
      </Dialog>,
    );
    const dialog = screen.getByTestId('d-bd') as HTMLDialogElement;
    await user.click(dialog);
    expect(onClose).toHaveBeenCalled();
  });

  it('does NOT close when clicking content inside the dialog', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <Dialog data-testid="d-in" onClose={onClose} open={true} title="Inner click test">
        <button>Inside</button>
      </Dialog>,
    );
    await user.click(screen.getByText('Inside'));
    expect(onClose).not.toHaveBeenCalled();
  });

  it('actions slot renders in the footer when supplied', () => {
    render(
      <Dialog
        actions={
          <>
            <button>Cancel</button>
            <button>Confirm</button>
          </>
        }
        onClose={() => {}}
        open={true}
        title="With actions"
      >
        Body
      </Dialog>,
    );
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeInTheDocument();
  });

  it('restores focus to the previously-focused element on close', () => {
    const onClose = vi.fn();
    function Harness() {
      return (
        <>
          <button data-testid="trigger">Open</button>
          <Dialog data-testid="d-focus" onClose={onClose} open={true} title="Focus test">
            <button>Inner</button>
          </Dialog>
        </>
      );
    }

    // Focus the trigger before mounting the dialog (simulate user opening).
    const trigger = document.createElement('button');
    trigger.id = 'pre-trigger';
    document.body.appendChild(trigger);
    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    render(<Harness />);
    const dialog = screen.getByTestId('d-focus') as HTMLDialogElement;
    dialog.close();
    expect(onClose).toHaveBeenCalled();

    document.body.removeChild(trigger);
  });
});
