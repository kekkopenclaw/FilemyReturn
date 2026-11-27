import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, Observable, of, switchMap, tap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthResponse, RegisterResponse, User } from './models';

const REFRESH_TOKEN_KEY = 'docdigit.refreshToken';

/**
 * Authentication state.
 *
 * The access token lives in memory only (never persisted) so it cannot be
 * read back from storage by another script. Only the refresh token is kept
 * in localStorage, and it is revoked server-side on logout.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly baseUrl = environment.apiUrl;

  private accessToken: string | null = null;
  private refreshTokenValue: string | null =
    localStorage.getItem(REFRESH_TOKEN_KEY);

  /** Currently signed-in user, null when signed out. */
  readonly currentUser = signal<User | null>(null);

  /** True once a session has been established (login or token refresh). */
  readonly isAuthenticated = signal(false);

  getAccessToken(): string | null {
    return this.accessToken;
  }

  hasRefreshToken(): boolean {
    return this.refreshTokenValue !== null;
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
   * Exchange the stored refresh token for a new access token, then reload
   * the current user. Emits an error when there is no usable refresh token.
   */
  refresh(): Observable<User> {
    if (!this.refreshTokenValue) {
      return throwError(() => new Error('No refresh token available'));
    }
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/auth/refresh`, {
        refreshToken: this.refreshTokenValue,
      })
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
    const token = this.refreshTokenValue;
    this.clearSession();
    if (token) {
      this.http
        .post(`${this.baseUrl}/auth/logout`, { refreshToken: token })
        .pipe(catchError(() => of(null)))
        .subscribe();
    }
    void this.router.navigate(['/login']);
  }

  /** Clear tokens and user state without a server call (e.g. refresh failed). */
  clearSessionAndRedirect(): void {
    this.clearSession();
    void this.router.navigate(['/login']);
  }

  private storeSession(res: AuthResponse): void {
    this.accessToken = res.accessToken;
    this.refreshTokenValue = res.refreshToken;
    localStorage.setItem(REFRESH_TOKEN_KEY, res.refreshToken);
    this.currentUser.set(res.user);
    this.isAuthenticated.set(true);
  }

  private clearSession(): void {
    this.accessToken = null;
    this.refreshTokenValue = null;
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    this.currentUser.set(null);
    this.isAuthenticated.set(false);
  }
}
