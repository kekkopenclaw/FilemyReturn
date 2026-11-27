import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, Observable, of, switchMap, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthResponse, ChangePasswordRequest, RegisterResponse, User } from './models';

/**
 * Legacy localStorage key from before the refresh token moved to an
 * HttpOnly cookie — removed on startup so no stale token lingers in
 * storage for users upgrading from the old flow.
 */
const LEGACY_REFRESH_TOKEN_KEY = 'docdigit.refreshToken';

/**
 * Authentication state.
 *
 * The access token lives in memory only (never persisted) so it cannot be
 * read back from storage by another script. The refresh token lives in an
 * HttpOnly cookie set by the API (`docuarchive.refresh`) — JavaScript
 * cannot read it at all, so an injected script cannot steal the long-lived
 * token. Refresh and logout therefore send an empty body and rely on the
 * browser attaching the cookie (the interceptor sets withCredentials).
 * Whether a cookie exists is something only the server knows, so the
 * guards and the interceptor simply attempt a refresh and treat a 401 as
 * "not signed in".
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly baseUrl = environment.apiUrl;

  private accessToken: string | null = null;

  /** Currently signed-in user, null when signed out. */
  readonly currentUser = signal<User | null>(null);

  /** True once a session has been established (login or token refresh). */
  readonly isAuthenticated = signal(false);

  constructor() {
    // One-time cleanup for the pre-cookie flow: a refresh token left in
    // localStorage by an older build must not survive the upgrade.
    localStorage.removeItem(LEGACY_REFRESH_TOKEN_KEY);
  }

  getAccessToken(): string | null {
    return this.accessToken;
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/auth/login`, { email, password })
      .pipe(tap((res) => this.storeSession(res)));
  }

  register(
    email: string,
    password: string,
    displayName: string,
  ): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>(`${this.baseUrl}/auth/register`, {
      email,
      password,
      displayName,
    });
  }

  /**
   * Change a password via POST /api/auth/change-password (204 on success).
   * Works both for a signed-in user (Bearer token attached, email ignored)
   * and for the first-login flow (no token; email + current password are
   * required and the account must be flagged MustChangePassword).
   */
  changePassword(
    email: string | null,
    currentPassword: string,
    newPassword: string,
  ): Observable<void> {
    const body: ChangePasswordRequest = {
      email: email || null,
      currentPassword,
      newPassword,
    };
    return this.http.post<void>(
      `${this.baseUrl}/auth/change-password`,
      body,
    );
  }

  /**
   * Exchange the HttpOnly refresh cookie for a new access token, then reload
   * the current user. Emits an error when there is no usable cookie (the
   * server answers 401) — the caller treats that as "not signed in".
   */
  refresh(): Observable<User> {
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/auth/refresh`, {})
      .pipe(
        tap((res) => this.storeSession(res)),
        switchMap(() => this.me()),
      );
  }

  me(): Observable<User> {
    return this.http
      .get<User>(`${this.baseUrl}/auth/me`)
      .pipe(tap((user) => this.currentUser.set(user)));
  }

  /** Best-effort server-side logout, then clear the local session. */
  logout(): void {
    this.clearSession();
    this.http
      .post(`${this.baseUrl}/auth/logout`, {})
      .pipe(catchError(() => of(null)))
      .subscribe();
    void this.router.navigate(['/login']);
  }

  /** Clear tokens and user state without a server call (e.g. refresh failed). */
  clearSessionAndRedirect(): void {
    this.clearSession();
    void this.router.navigate(['/login']);
  }

  private storeSession(res: AuthResponse): void {
    // The refresh token is NOT in the body any more (cookie only) — only
    // the short-lived access token is kept, in memory.
    this.accessToken = res.accessToken;
    this.currentUser.set(res.user);
    this.isAuthenticated.set(true);
  }

  private clearSession(): void {
    this.accessToken = null;
    this.currentUser.set(null);
    this.isAuthenticated.set(false);
  }
}
