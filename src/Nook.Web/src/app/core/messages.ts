import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { components } from '../api/schema';

type ValidationProblemDetails = components['schemas']['ValidationProblemDetails'];

/** What a page says when a request fails for a reason it has no better words for. */
export const somethingWentWrong = 'Something went wrong. Try again.';

/** What a page says when the API turns a request away for coming too often. */
export const tooManyAttempts = 'Too many attempts. Try again in a minute.';

/** What a form that asks for a new password twice says when the two differ. */
export const passwordsDoNotMatch = 'The two passwords do not match.';

/**
 * What a form shows for a request the API turned down: the messages of a validation problem under the field the API keys
 * each by, or `somethingWentWrong` under no field for any other failure.
 */
export function fieldErrorsOf(error: unknown): Record<string, string[]> {
  if (error instanceof HttpErrorResponse && error.status === HttpStatusCode.BadRequest) {
    const errors = (error.error as ValidationProblemDetails | null)?.errors ?? {};
    if (Object.values(errors).some((messages) => messages.length > 0)) {
      return errors;
    }
  }
  return { '': [somethingWentWrong] };
}
