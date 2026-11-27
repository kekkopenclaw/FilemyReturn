import {
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Auth endpoints never carry a Bearer token and are never retried. */
function isAuthEndpoint(req: HttpRequest<unknown>): boolean {
  return /\/auth\/(login|register|refresh|logout)$/.test(req.url);
}

/**
 * Adds the in-memory Bearer token to API calls. On a 401, tries one token
 * refresh and replays the original request; if the refresh fails, the
 * session is cleared and the user is sent to /login.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);

  const token = auth.getAccessToken();
  const authedReq =
    token && !isAuthEndpoint(req)
      ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : req;

  return next(authedReq).pipe(
    catchError((err: unknown) => {
      if (
        err instanceof HttpErrorResponse &&
        err.status === 401 &&
        !isAuthEndpoint(req)
      ) {
        return auth.refresh().pipe(
          switchMap(() => {
            const newToken = auth.getAccessToken();
            const retryReq = newToken
              ? req.clone({
                  setHeaders: { Authorization: `Bearer ${newToken}` },
                })
              : req;
            return next(retryReq);
          }),
          catchError((refreshErr: unknown) => {
            auth.clearSessionAndRedirect();
            return throwError(() => refreshErr);
          }),
        );
      }
      return throwError(() => err);
    }),
  );
};
