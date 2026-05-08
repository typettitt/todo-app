import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe } from 'vitest-axe';
import TabStrip from '../TabStrip';

afterEach(() => {
  cleanup();
  document.documentElement.classList.remove('dark');
});

function setTheme(mode: 'light' | 'dark') {
  document.documentElement.classList.toggle('dark', mode === 'dark');
}

const TABS = [
  { id: 'all', label: 'All' },
  { id: 'active', label: 'Active' },
  { id: 'completed', label: 'Completed' },
  { id: 'duetoday', label: 'Due Today', testId: 'tab-duetoday' },
];

describe('TabStrip', () => {
  it('renders all tabs in light theme with correct selection state', () => {
    setTheme('light');
    render(<TabStrip aria-label="Filter" activeId="all" onChange={() => {}} tabs={TABS} />);
    const all = screen.getByRole('tab', { name: 'All' });
    expect(all).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Active' })).toHaveAttribute('aria-selected', 'false');
  });

  it('renders in dark theme', () => {
    setTheme('dark');
    render(<TabStrip aria-label="Filter" activeId="active" onChange={() => {}} tabs={TABS} />);
    expect(screen.getByRole('tab', { name: 'Active' })).toHaveAttribute('aria-selected', 'true');
  });

  it('axe-clean in both themes', async () => {
    setTheme('light');
    const { container, unmount } = render(
      <TabStrip aria-label="Filter" activeId="all" onChange={() => {}} tabs={TABS} />,
    );
    expect(await axe(container)).toHaveNoViolations();
    unmount();

    setTheme('dark');
    const dark = render(
      <TabStrip aria-label="Filter" activeId="completed" onChange={() => {}} tabs={TABS} />,
    );
    expect(await axe(dark.container)).toHaveNoViolations();
  });

  it('ArrowRight changes active tab and ArrowLeft reverses', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<TabStrip aria-label="Filter" activeId="all" onChange={onChange} tabs={TABS} />);
    const all = screen.getByRole('tab', { name: 'All' });
    all.focus();
    expect(all).toHaveFocus();

    await user.keyboard('{ArrowRight}');
    expect(onChange).toHaveBeenCalledWith('active');

    await user.keyboard('{ArrowLeft}');
    expect(onChange).toHaveBeenCalledWith('all');
  });

  it('Home / End jump to first / last', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<TabStrip aria-label="Filter" activeId="active" onChange={onChange} tabs={TABS} />);
    const active = screen.getByRole('tab', { name: 'Active' });
    active.focus();

    await user.keyboard('{End}');
    expect(onChange).toHaveBeenCalledWith('duetoday');

    await user.keyboard('{Home}');
    expect(onChange).toHaveBeenCalledWith('all');
  });

  it('clicking a tab calls onChange', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<TabStrip aria-label="Filter" activeId="all" onChange={onChange} tabs={TABS} />);
    await user.click(screen.getByRole('tab', { name: 'Completed' }));
    expect(onChange).toHaveBeenCalledWith('completed');
  });

  it('forwards testId on tabs that supply one', () => {
    render(<TabStrip aria-label="Filter" activeId="all" onChange={() => {}} tabs={TABS} />);
    expect(screen.getByTestId('tab-duetoday')).toBeInTheDocument();
  });
});
