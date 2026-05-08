import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiProblem, ApiResponseParseError } from '../lib/api';
import { toast } from '../lib/toast';

export function applyApiProblemToForm<T extends FieldValues>(
  error: unknown,
  fields: Path<T>[],
  setError: UseFormSetError<T>,
  setFormError: (message: string | undefined) => void,
) {
  if (error instanceof ApiResponseParseError) {
    setFormError(error.message);
    return true;
  }

  if (!(error instanceof ApiProblem)) {
    return false;
  }

  let firstField = true;

  for (const field of fields) {
    const message = error.errors[field]?.[0];
    if (!message) {
      continue;
    }

    setError(
      field,
      {
        message,
        type: 'server',
      },
      { shouldFocus: firstField },
    );
    firstField = false;
  }

  if (error.status >= 500) {
    toast.error(error.message);
  }

  if (firstField) {
    setFormError(error.message);
  }

  return true;
}
