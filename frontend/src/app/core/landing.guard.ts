import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Guard for the public landing page (/).
 *
 * Signed-in users are sent straight to /dashboard. If the in-memory access
 * token is gone (e.g. after a page reload), one silent refresh is attempted
 * first — the refresh token lives in an HttpOnly cookie only the server can
 * see, so there is no local check for it. On success the user goes to
 * /dashboard, on failure the landing page is shown.
 */
export const landingGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    void router.navigate(['/dashboard']);
    return false;
  }

  return auth.refresh().pipe(
    map(() => {
      void router.navigate(['/dashboard']);
      return false;
    }),
    catchError(() => of(true)),
  );
};
