import type { Priority } from './schemas';

export function priorityClass(priority: Priority) {
  if (priority === 'High') {
    return 'priorityHigh';
  }

  if (priority === 'Medium') {
    return 'priorityMedium';
  }

  return 'priorityLow';
}
