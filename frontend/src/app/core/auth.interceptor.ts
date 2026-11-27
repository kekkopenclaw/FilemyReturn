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
 * Change-password is an auth endpoint too for retry purposes: a failed
 * first-login password change (wrong current password) must stay on the
 * change-password page with its own error, not trigger a token refresh
 * and a redirect to /login. The Bearer token is still attached above
 * when one exists, so signed-in users take the authenticated path.
 */
function isChangePassword(req: HttpRequest<unknown>): boolean {
  return /\/auth\/change-password$/.test(req.url);
}

/**
 * Adds the in-memory Bearer token to API calls, and sends cookies with
 * every API request (the refresh token lives in an HttpOnly cookie — the
 * browser only attaches it when withCredentials is set, including in dev
 * where the app runs on localhost:4200 against the API on localhost:5000).
 * On a 401, tries one token refresh and replays the original request; if
 * the refresh fails, the session is cleared and the user is sent to /login.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);

  const withCreds = req.clone({ withCredentials: true });
  const token = auth.getAccessToken();
  const authedReq =
    token && !isAuthEndpoint(req)
      ? withCreds.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : withCreds;

  return next(authedReq).pipe(
    catchError((err: unknown) => {
      if (
        err instanceof HttpErrorResponse &&
        err.status === 401 &&
        !isAuthEndpoint(req) &&
        !isChangePassword(req)
      ) {
        return auth.refresh().pipe(
          switchMap(() => {
            const newToken = auth.getAccessToken();
            const retryReq = newToken
              ? withCreds.clone({
                  setHeaders: { Authorization: `Bearer ${newToken}` },
                })
              : withCreds;
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
