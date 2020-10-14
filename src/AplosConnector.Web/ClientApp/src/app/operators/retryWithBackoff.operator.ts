import { Observable, of, throwError } from 'rxjs';
import { delay, mergeMap, retryWhen } from 'rxjs/operators';

const DEFAULT_MAX_RETRIES = 2;
const DEFAULT_BACKOFF = 100;
const DEFAULT_DELAY_MS = 3;

export function retryWithBackoff(
  delayMs: number = DEFAULT_DELAY_MS,
  maxRetry = DEFAULT_MAX_RETRIES,
  backoffMs = DEFAULT_BACKOFF
) {
  let retries = maxRetry;

  return (src: Observable<any>) =>
    src.pipe(
      retryWhen((errors: Observable<any>) =>
        errors.pipe(
          mergeMap(error => {
            if (retries-- > 0) {
              const backoffTime = delayMs + (maxRetry - retries) * backoffMs;
              return of(error).pipe(delay(backoffTime));
            }
            return throwError(error);
          })
        )
      )
    );
}
