import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Allows access when a session exists. If the in-memory access token is
 * gone (e.g. after a page reload) but a refresh token is stored, one silent
 * refresh is attempted before sending the user to /login.
 */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  if (auth.hasRefreshToken()) {
    return auth.refresh().pipe(
      map(() => true),
      catchError(() => {
        auth.clearSessionAndRedirect();
        return of(false);
      }),
    );
  }

  void router.navigate(['/login']);
  return false;
};
