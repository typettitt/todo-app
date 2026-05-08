import type { TodoResponse } from './schemas';

export type CalendarMode = 'week' | 'month';

export type CalendarRange = {
  days: Date[];
  end: Date;
  start: Date;
};

export function localDate() {
  return toIsoDate(new Date());
}

export function toIsoDate(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export function formatDate(value?: string | null): string {
  return value ?? 'Unscheduled';
}

export function formatTimestamp(value?: string | null): string {
  if (!value) {
    return '-';
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return parsed.toLocaleDateString(undefined, {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

export function isOverdue(todo: TodoResponse): boolean {
  return Boolean(todo.dueDate && !todo.isCompleted && todo.dueDate < localDate());
}

export function getCalendarRange(cursor: Date, mode: CalendarMode): CalendarRange {
  if (mode === 'week') {
    const start = startOfWeek(cursor);
    const days = Array.from({ length: 7 }, (_, index) => addDays(start, index));
    return {
      days,
      end: days[days.length - 1],
      start,
    };
  }

  const monthStart = new Date(cursor.getFullYear(), cursor.getMonth(), 1);
  const start = startOfWeek(monthStart);
  const days = Array.from({ length: 42 }, (_, index) => addDays(start, index));
  return {
    days,
    end: days[days.length - 1],
    start,
  };
}

export function addDays(date: Date, days: number) {
  const next = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  next.setDate(next.getDate() + days);
  return next;
}

export function formatRangeTitle(range: CalendarRange, mode: CalendarMode) {
  if (mode === 'month') {
    const midpoint = range.days[21] ?? range.start;
    return midpoint.toLocaleDateString(undefined, {
      month: 'long',
      year: 'numeric',
    });
  }

  return `${range.start.toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
  })} - ${range.end.toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  })}`;
}

export function formatDayLabel(day: Date) {
  return day.toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'long',
    weekday: 'long',
    year: 'numeric',
  });
}

export function weekdayLabel(day: Date) {
  return day.toLocaleDateString(undefined, {
    weekday: 'short',
  });
}

function startOfWeek(date: Date) {
  const start = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  start.setDate(start.getDate() - start.getDay());
  return start;
}
