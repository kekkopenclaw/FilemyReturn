import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Allows access when a session exists. If the in-memory access token is
 * gone (e.g. after a page reload), one silent refresh is attempted — the
 * refresh token lives in an HttpOnly cookie only the server can see, so
 * there is no local check for it; a 401 from the refresh means the user
 * is sent to /login.
 */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);

  if (auth.isAuthenticated()) {
    return true;
  }

  return auth.refresh().pipe(
    map(() => true),
    catchError(() => {
      auth.clearSessionAndRedirect();
      return of(false);
    }),
  );
};
