import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe } from 'vitest-axe';
import { TextInput, Textarea } from '../TextInput';

afterEach(() => {
  cleanup();
  document.documentElement.classList.remove('dark');
});

function setTheme(mode: 'light' | 'dark') {
  document.documentElement.classList.toggle('dark', mode === 'dark');
}

describe('TextInput', () => {
  it('renders labeled input in light theme', () => {
    setTheme('light');
    render(<TextInput id="email" label="Email" />);
    const input = screen.getByLabelText('Email');
    expect(input).toBeInTheDocument();
    expect(input).toHaveAttribute('id', 'email');
  });

  it('renders error and aria-invalid in dark theme', () => {
    setTheme('dark');
    render(<TextInput id="title" label="Title" error="Required" />);
    const input = screen.getByLabelText('Title');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('alert')).toHaveTextContent('Required');
  });

  it('axe-clean in both themes', async () => {
    setTheme('light');
    const { container, unmount } = render(<TextInput id="a" label="Field A" caption="OPTIONAL" />);
    expect(await axe(container)).toHaveNoViolations();
    unmount();

    setTheme('dark');
    const dark = render(<TextInput id="b" label="Field B" />);
    expect(await axe(dark.container)).toHaveNoViolations();
  });

  it('focus visible on Tab (keyboard accessible)', async () => {
    const user = userEvent.setup();
    render(<TextInput id="x" label="X" />);
    await user.tab();
    expect(screen.getByLabelText('X')).toHaveFocus();
  });

  it('forwards refs', () => {
    const ref = { current: null as HTMLInputElement | null };
    render(<TextInput ref={ref} id="r" label="R" />);
    expect(ref.current).toBeInstanceOf(HTMLInputElement);
  });

  it('caption rendered with id linked via aria-describedby', () => {
    render(<TextInput id="cap" label="Cap" caption="HINT" />);
    const input = screen.getByLabelText('Cap');
    const describedBy = input.getAttribute('aria-describedby');
    expect(describedBy).toContain('cap-caption');
    expect(screen.getByText('HINT')).toBeInTheDocument();
  });
});

describe('Textarea', () => {
  it('renders labeled textarea in light theme', () => {
    setTheme('light');
    render(<Textarea id="desc" label="Description" />);
    const textarea = screen.getByLabelText('Description');
    expect(textarea.tagName).toBe('TEXTAREA');
  });

  it('renders error in dark theme', () => {
    setTheme('dark');
    render(<Textarea id="desc" label="Description" error="Too long" />);
    expect(screen.getByLabelText('Description')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('alert')).toHaveTextContent('Too long');
  });

  it('axe-clean', async () => {
    setTheme('light');
    const { container } = render(<Textarea id="t" label="Notes" caption="MARKDOWN OK" />);
    expect(await axe(container)).toHaveNoViolations();
  });

  it('forwards refs and accepts user input', async () => {
    const user = userEvent.setup();
    const ref = { current: null as HTMLTextAreaElement | null };
    render(<Textarea ref={ref} id="t2" label="Notes" />);
    expect(ref.current).toBeInstanceOf(HTMLTextAreaElement);
    await user.type(screen.getByLabelText('Notes'), 'hello');
    expect(ref.current?.value).toBe('hello');
  });
});
