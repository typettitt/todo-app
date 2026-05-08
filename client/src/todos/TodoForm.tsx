import { useEffect, useId, useRef, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { zodResolver } from '@hookform/resolvers/zod';
import { useForm, useWatch } from 'react-hook-form';
import { z } from 'zod';
import { useMe } from '../auth/useAuth';
import { ApiProblem, ApiResponseParseError } from '../lib/api';
import { toast } from '../lib/toast';
import { createTodo, updateTodo } from './api';
import { replaceTodoInListCache } from './cache';
import { todosKeys, type TodoListParams } from './queryKeys';
import {
  PrioritySchema,
  type CreateTodoRequest,
  type Priority,
  type TodoResponse,
  type UpdateTodoRequest,
} from './schemas';
import styles from './Todos.module.css';

const dateInputSchema = z.union([
  z.literal(''),
  z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Due date must be YYYY-MM-DD.'),
]);

const TodoFormSchema = z.object({
  title: z
    .string()
    .min(1, 'Title is required.')
    .max(200, 'Title must be at most 200 characters.')
    .refine((value) => value.trim().length > 0, {
      message: 'Title cannot be whitespace.',
    }),
  description: z.string().max(2000, 'Description must be at most 2000 characters.'),
  dueDate: dateInputSchema,
  priority: PrioritySchema,
  tags: z
    .array(
      z
        .string()
        .min(1, 'Tag must not be empty.')
        .max(50, 'Tag must be at most 50 characters.')
        .refine((value) => value.trim().length > 0, {
          message: 'Tag cannot be whitespace.',
        }),
    )
    .max(20, 'Tags is limited to 20 entries.'),
  rowVersion: z.number().optional(),
});

type TodoFormValues = z.infer<typeof TodoFormSchema>;

type TodoFormProps = {
  heading?: string | null;
  initialDueDate?: string | null;
  initialTodo?: TodoResponse;
  listParams: Partial<TodoListParams>;
  mode: 'create' | 'edit';
  onCancel?: () => void;
  onReload?: () => Promise<TodoResponse>;
  onSuccess?: (todo: TodoResponse) => void;
};

const serverFields = ['title', 'description', 'dueDate', 'priority', 'tags'] as const;

export function TodoForm({
  heading,
  initialDueDate,
  initialTodo,
  listParams,
  mode,
  onCancel,
  onReload,
  onSuccess,
}: TodoFormProps) {
  const formId = useId();
  const tagInputRef = useRef<HTMLInputElement | null>(null);
  const queryClient = useQueryClient();
  const me = useMe();
  const userId = me.data?.id ?? '';
  const [formError, setFormError] = useState<string>();
  const [conflict, setConflict] = useState(false);
  const [tagDraft, setTagDraft] = useState('');
  const {
    clearErrors,
    control,
    formState: { errors, isSubmitting },
    handleSubmit,
    register,
    reset,
    setError,
    setValue,
  } = useForm<TodoFormValues>({
    defaultValues: toFormValues(initialTodo, initialDueDate),
    resolver: zodResolver(TodoFormSchema),
  });
  const tags = useWatch({ control, name: 'tags' }) ?? [];
  const createMutation = useMutation({
    mutationFn: createTodo,
    onSuccess: (todo) => {
      queryClient.setQueryData(todosKeys.detail(userId, todo.id), todo);
      // Broaden to lists() so calendar / list / paginated views all see the
      // change. exact-on-list misses the calendar query (different params).
      queryClient.invalidateQueries({
        queryKey: todosKeys.lists(userId),
      });
    },
  });
  const updateMutation = useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdateTodoRequest }) =>
      updateTodo(id, request),
    onSuccess: (todo) => {
      replaceTodoInListCache(queryClient, userId, listParams, todo);
      queryClient.invalidateQueries({
        queryKey: todosKeys.lists(userId),
      });
    },
  });
  const isPending = isSubmitting || createMutation.isPending || updateMutation.isPending;

  useEffect(() => {
    reset(toFormValues(initialTodo, initialDueDate));
  }, [initialDueDate, initialTodo, mode, reset]);

  async function onSubmit(values: TodoFormValues) {
    setFormError(undefined);
    setConflict(false);

    try {
      const todo =
        mode === 'create'
          ? await createMutation.mutateAsync(toCreateRequest(values))
          : await updateMutation.mutateAsync({
              id: requireTodo(initialTodo).id,
              request: toUpdateRequest(values),
            });

      reset(toFormValues(todo));
      onSuccess?.(todo);
    } catch (error) {
      handleMutationError(error);
    }
  }

  function handleMutationError(error: unknown) {
    if (error instanceof ApiResponseParseError) {
      setFormError(error.message);
      toast.error(error.message);
      return;
    }

    if (!(error instanceof ApiProblem)) {
      throw error;
    }

    if (error.status === 409) {
      setConflict(true);
      return;
    }

    if (error.status >= 500) {
      setFormError(error.message);
      toast.error(error.message);
      return;
    }

    const appliedFieldError = applyFieldErrors(error);
    if (!appliedFieldError) {
      setFormError(error.message);
    }
  }

  function applyFieldErrors(error: ApiProblem) {
    let firstField = true;

    for (const field of serverFields) {
      const message = getFieldMessage(error.errors, field);
      if (!message) {
        continue;
      }

      setError(
        field,
        {
          message,
          type: 'server',
        },
        { shouldFocus: firstField && field !== 'tags' },
      );

      if (firstField && field === 'tags') {
        tagInputRef.current?.focus();
      }

      firstField = false;
    }

    return !firstField;
  }

  async function reloadFromServer() {
    if (!onReload) {
      return;
    }

    try {
      const fresh = await onReload();
      reset(toFormValues(fresh));
      clearErrors();
      setConflict(false);
      setFormError(undefined);
    } catch (error) {
      setFormError(error instanceof Error ? error.message : 'Could not reload todo.');
    }
  }

  function addTag() {
    const next = tagDraft.trim();
    if (!next) {
      setError('tags', {
        message: 'Tag cannot be whitespace.',
        type: 'manual',
      });
      tagInputRef.current?.focus();
      return;
    }

    if (next.length > 50) {
      setError('tags', {
        message: 'Tag must be at most 50 characters.',
        type: 'manual',
      });
      tagInputRef.current?.focus();
      return;
    }

    if (tags.length >= 20) {
      setError('tags', {
        message: 'Tags is limited to 20 entries.',
        type: 'manual',
      });
      return;
    }

    if (tags.includes(next)) {
      setTagDraft('');
      return;
    }

    setValue('tags', [...tags, next], {
      shouldDirty: true,
      shouldValidate: true,
    });
    clearErrors('tags');
    setTagDraft('');
  }

  function removeTag(tag: string) {
    setValue(
      'tags',
      tags.filter((value) => value !== tag),
      { shouldDirty: true, shouldValidate: true },
    );
  }

  return (
    <form className={styles.todoForm} onSubmit={handleSubmit(onSubmit)} noValidate>
      {heading !== null ? (
        <div className={styles.formHeader}>
          <h2>{heading ?? (mode === 'create' ? 'New Todo' : 'Edit Todo')}</h2>
        </div>
      ) : null}

      {conflict ? (
        <div className={styles.warningBanner} role="alert">
          <span>Someone else changed this. Reload?</span>
          <button onClick={reloadFromServer} type="button">
            Reload
          </button>
        </div>
      ) : null}

      {formError ? (
        <p className={styles.formError} role="alert">
          {formError}
        </p>
      ) : null}

      <div className={styles.field}>
        <label htmlFor={`${formId}-title`}>Title</label>
        <input
          aria-invalid={errors.title ? 'true' : 'false'}
          id={`${formId}-title`}
          maxLength={200}
          {...register('title')}
        />
        {errors.title ? (
          <p className={styles.fieldError} role="alert">
            {errors.title.message}
          </p>
        ) : null}
      </div>

      <div className={styles.field}>
        <label htmlFor={`${formId}-description`}>Description</label>
        <textarea
          aria-invalid={errors.description ? 'true' : 'false'}
          id={`${formId}-description`}
          maxLength={2000}
          rows={4}
          {...register('description')}
        />
        {errors.description ? (
          <p className={styles.fieldError} role="alert">
            {errors.description.message}
          </p>
        ) : null}
      </div>

      <div className={styles.formGrid}>
        <div className={styles.field}>
          <label htmlFor={`${formId}-dueDate`}>Due Date</label>
          <input
            aria-invalid={errors.dueDate ? 'true' : 'false'}
            id={`${formId}-dueDate`}
            type="date"
            {...register('dueDate')}
          />
          {errors.dueDate ? (
            <p className={styles.fieldError} role="alert">
              {errors.dueDate.message}
            </p>
          ) : null}
        </div>

        <div className={styles.field}>
          <label htmlFor={`${formId}-priority`}>Priority</label>
          <select
            aria-invalid={errors.priority ? 'true' : 'false'}
            id={`${formId}-priority`}
            {...register('priority')}
          >
            <option value="Low">Low</option>
            <option value="Medium">Medium</option>
            <option value="High">High</option>
          </select>
          {errors.priority ? (
            <p className={styles.fieldError} role="alert">
              {errors.priority.message}
            </p>
          ) : null}
        </div>
      </div>

      <div className={styles.field}>
        <label htmlFor={`${formId}-tag`}>Tags</label>
        <div className={styles.tagEditor}>
          <input
            aria-invalid={errors.tags ? 'true' : 'false'}
            id={`${formId}-tag`}
            onChange={(event) => setTagDraft(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault();
                addTag();
              }
            }}
            ref={tagInputRef}
            value={tagDraft}
          />
          <button onClick={addTag} type="button">
            Add
          </button>
        </div>
        {tags.length > 0 ? (
          <ul className={styles.tags} aria-label="Tags">
            {tags.map((tag) => (
              <li key={tag}>
                <span>{tag}</span>
                <button onClick={() => removeTag(tag)} type="button">
                  Remove
                </button>
              </li>
            ))}
          </ul>
        ) : null}
        {errors.tags ? (
          <p className={styles.fieldError} role="alert">
            {errors.tags.message}
          </p>
        ) : null}
      </div>

      <div className={styles.formActions}>
        <button
          className={styles.primaryButton}
          data-testid={mode === 'create' ? 'todo-create-submit' : 'todo-edit-submit'}
          disabled={isPending}
          type="submit"
        >
          {mode === 'create' ? 'Create Todo' : 'Save Changes'}
        </button>
        {onCancel ? (
          <button className={styles.secondaryButton} onClick={onCancel} type="button">
            Cancel
          </button>
        ) : null}
      </div>
    </form>
  );
}

function toFormValues(todo?: TodoResponse, initialDueDate?: string | null): TodoFormValues {
  return {
    title: todo?.title ?? '',
    description: todo?.description ?? '',
    dueDate: todo?.dueDate ?? initialDueDate ?? '',
    priority: todo?.priority ?? 'Low',
    tags: todo?.tags ?? [],
    rowVersion: todo?.rowVersion,
  };
}

function toCreateRequest(values: TodoFormValues): CreateTodoRequest {
  return {
    title: values.title,
    description: emptyToNull(values.description),
    dueDate: values.dueDate || null,
    priority: values.priority,
    tags: values.tags,
  };
}

function toUpdateRequest(values: TodoFormValues): UpdateTodoRequest {
  if (values.rowVersion === undefined) {
    throw new Error('Todo row version is required.');
  }

  return {
    title: values.title,
    description: emptyToNull(values.description),
    dueDate: values.dueDate || null,
    priority: values.priority as Priority,
    tags: values.tags,
    rowVersion: values.rowVersion,
  };
}

function requireTodo(todo: TodoResponse | undefined) {
  if (!todo) {
    throw new Error('Todo is required for edit mode.');
  }

  return todo;
}

function emptyToNull(value: string) {
  return value.length > 0 ? value : null;
}

function getFieldMessage(errors: Record<string, string[]>, field: (typeof serverFields)[number]) {
  const direct = errors[field]?.[0];
  if (direct) {
    return direct;
  }

  const indexedPrefix = `${field}[`;
  const dottedPrefix = `${field}.`;
  const matched = Object.entries(errors).find(
    ([key]) => key.startsWith(indexedPrefix) || key.startsWith(dottedPrefix),
  );

  return matched?.[1][0];
}
