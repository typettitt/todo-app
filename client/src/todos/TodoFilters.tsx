import type { TodoListParams, TodoSortBy, TodoSortDir, TodoStatusFilter } from './queryKeys';
import styles from './Todos.module.css';

export type StatusTabId = 'all' | 'Active' | 'Completed' | 'DueToday';
export type ViewMode = 'list' | 'calendar';

const statusTabs: Array<{ id: StatusTabId; label: string; testId: string }> = [
  { id: 'all', label: 'All', testId: 'tab-all' },
  { id: 'Active', label: 'Open', testId: 'tab-active' },
  { id: 'Completed', label: 'Done', testId: 'tab-completed' },
  { id: 'DueToday', label: 'Due Today', testId: 'tab-duetoday' },
];

const sortOptions: Array<{ label: string; value: TodoSortBy }> = [
  { label: 'Created', value: 'CreatedAt' },
  { label: 'Due date', value: 'DueDate' },
  { label: 'Priority', value: 'Priority' },
  { label: 'Title', value: 'Title' },
];

const sortDirOptions: Array<{ label: string; value: TodoSortDir }> = [
  { label: 'Descending', value: 'Desc' },
  { label: 'Ascending', value: 'Asc' },
];

type TodoFiltersProps = {
  activeTabId: StatusTabId;
  listParams: TodoListParams;
  localToday: string;
  onCreate: () => void;
  onFiltersChange: (changes: Partial<TodoListParams>) => void;
  onReset: () => void;
  onSearchChange: (value: string) => void;
  onViewChange: (view: ViewMode) => void;
  searchDraft: string;
  view: ViewMode;
};

export function TodoFilters({
  activeTabId,
  listParams,
  localToday,
  onCreate,
  onFiltersChange,
  onReset,
  onSearchChange,
  onViewChange,
  searchDraft,
  view,
}: TodoFiltersProps) {
  return (
    <>
      <div className={styles.viewTabs} role="tablist" aria-label="Todo view">
        <button
          aria-selected={view === 'list'}
          className={view === 'list' ? styles.active : undefined}
          onClick={() => onViewChange('list')}
          role="tab"
          type="button"
        >
          List
        </button>
        <button
          aria-selected={view === 'calendar'}
          className={view === 'calendar' ? styles.active : undefined}
          onClick={() => onViewChange('calendar')}
          role="tab"
          type="button"
        >
          Calendar
        </button>
      </div>

      <section className={styles.filterToolbar} aria-label="Todo filters">
        <label className={styles.field} htmlFor="todo-search">
          <span className={styles.fieldLabel}>Search</span>
          <input
            className={styles.monoInput}
            id="todo-search"
            onChange={(event) => onSearchChange(event.target.value)}
            placeholder="title, description, tag"
            type="search"
            value={searchDraft}
          />
        </label>

        <div className={styles.toolbarGroup}>
          <span className={styles.fieldLabel}>Status</span>
          <div className={styles.statusTabs} role="tablist" aria-label="Todo status">
            {statusTabs.map((tab) => (
              <button
                aria-selected={activeTabId === tab.id}
                className={activeTabId === tab.id ? styles.active : undefined}
                data-testid={tab.testId}
                key={tab.id}
                onClick={() =>
                  onFiltersChange({
                    status: tab.id === 'all' ? null : (tab.id as TodoStatusFilter),
                    today: tab.id === 'DueToday' ? localToday : null,
                  })
                }
                role="tab"
                type="button"
              >
                {tab.label}
              </button>
            ))}
          </div>
        </div>

        <div className={styles.toolbarSelects}>
          <label className={styles.field} htmlFor="todo-sort">
            <span className={styles.fieldLabel}>Sort</span>
            <select
              className={styles.monoInput}
              id="todo-sort"
              onChange={(event) =>
                onFiltersChange({
                  sortBy: event.target.value as TodoSortBy,
                })
              }
              value={listParams.sortBy}
            >
              {sortOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>

          <label className={styles.field} htmlFor="todo-sort-dir">
            <span className={styles.fieldLabel}>Direction</span>
            <select
              className={styles.monoInput}
              id="todo-sort-dir"
              onChange={(event) =>
                onFiltersChange({
                  sortDir: event.target.value as TodoSortDir,
                })
              }
              value={listParams.sortDir}
            >
              {sortDirOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        <div className={styles.toolbarActions}>
          <button className={styles.ghostButton} onClick={onReset} type="button">
            Reset
          </button>
        </div>
      </section>

      <div className={styles.createBar}>
        <button
          className={`${styles.requestButton} ${styles.createBarButton}`}
          onClick={onCreate}
          type="button"
        >
          New Todo
        </button>
      </div>
    </>
  );
}
