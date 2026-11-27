import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import type { User } from './models';

// The refresh token lives in an HttpOnly cookie (`docuarchive.refresh`):
// the service must never read or write it in localStorage, refresh and
// logout send an empty body, and the access token stays in memory only.
describe('AuthService (refresh token in HttpOnly cookie)', () => {
  const router = { navigate: vi.fn() };
  const user: User = {
    id: 'u1',
    email: 'a@example.com',
    displayName: 'A User',
    isSystemAdmin: false,
    createdAt: '2026-11-27T00:00:00Z',
    mustChangePassword: false,
  };
  const api = environment.apiUrl;

  let service: AuthService;
  let http: HttpTestingController;

  const configure = () => {
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
      ],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  };

  beforeEach(() => {
    localStorage.clear();
    router.navigate.mockClear();
    configure();
  });

  it('purges a legacy refresh token left in localStorage by the old flow', () => {
    localStorage.setItem('docdigit.refreshToken', 'stale-token');
    // Re-create the service so its constructor runs after the seed.
    TestBed.resetTestingModule();
    configure();
    expect(localStorage.getItem('docdigit.refreshToken')).toBeNull();
  });

  it('login keeps the access token in memory and stores no refresh token', () => {
    service.login('a@example.com', 'Password123!@#').subscribe();
    const req = http.expectOne(`${api}/auth/login`);
    req.flush({
      accessToken: 'tok',
      refreshToken: '',
      accessTokenExpiresAt: '2026-11-27T00:15:00Z',
      user,
    });
    expect(service.getAccessToken()).toBe('tok');
    expect(service.isAuthenticated()).toBe(true);
    expect(localStorage.getItem('docdigit.refreshToken')).toBeNull();
    http.verify();
  });

  it('refresh sends an empty body and relies on the cookie, then reloads the user', () => {
    let me: User | undefined;
    service.refresh().subscribe((u) => (me = u));
    const refreshReq = http.expectOne(`${api}/auth/refresh`);
    expect(refreshReq.request.method).toBe('POST');
    expect(refreshReq.request.body).toEqual({});
    refreshReq.flush({
      accessToken: 'new-access',
      refreshToken: '',
      accessTokenExpiresAt: '2026-11-27T00:15:00Z',
      user,
    });
    const meReq = http.expectOne(`${api}/auth/me`);
    meReq.flush(user);
    expect(me).toEqual(user);
    expect(service.getAccessToken()).toBe('new-access');
    expect(service.isAuthenticated()).toBe(true);
    http.verify();
  });

  it('logout posts an empty body and clears the session', () => {
    service.login('a@example.com', 'Password123!@#').subscribe();
    const loginReq = http.expectOne(`${api}/auth/login`);
    loginReq.flush({
      accessToken: 'tok',
      refreshToken: '',
      accessTokenExpiresAt: '2026-11-27T00:15:00Z',
      user,
    });
    expect(service.getAccessToken()).toBe('tok');

    service.logout();
    const logoutReq = http.expectOne(`${api}/auth/logout`);
    expect(logoutReq.request.method).toBe('POST');
    expect(logoutReq.request.body).toEqual({});
    logoutReq.flush(null);
    expect(service.getAccessToken()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
    http.verify();
  });
});
