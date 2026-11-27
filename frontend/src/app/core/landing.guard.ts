import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Guard for the public landing page (/).
 *
 * Signed-in users are sent straight to /dashboard. If the in-memory access
 * token is gone (e.g. after a page reload) but a refresh token is stored,
 * one silent refresh is attempted first — on success the user goes to
 * /dashboard, on failure the landing page is shown.
 */
export const landingGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    void router.navigate(['/dashboard']);
    return false;
  }

  if (auth.hasRefreshToken()) {
    return auth.refresh().pipe(
      map(() => {
        void router.navigate(['/dashboard']);
        return false;
      }),
      catchError(() => of(true)),
    );
  }

  return true;
};
