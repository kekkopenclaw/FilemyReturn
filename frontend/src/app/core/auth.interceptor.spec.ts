import {
  HttpClient,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { environment } from '../../environments/environment';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import type { User } from './models';

// The interceptor must send cookies with every API request (the refresh
// token lives in an HttpOnly cookie — the browser only attaches it when
// withCredentials is set) and keep attaching the in-memory Bearer token.
describe('authInterceptor', () => {
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

  let http: HttpTestingController;
  let client: HttpClient;
  let service: AuthService;

  beforeEach(() => {
    router.navigate.mockClear();
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    service = TestBed.inject(AuthService);
  });

  it('sends cookies with API requests even before any login', () => {
    client.get(`${api}/workspaces`).subscribe({ error: () => undefined });
    const req = http.expectOne(`${api}/workspaces`);
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
    http.verify();
  });

  it('sends cookies and the Bearer token once signed in', () => {
    service.login('a@example.com', 'Password123!@#').subscribe();
    const loginReq = http.expectOne(`${api}/auth/login`);
    // The login request itself carries cookies too (no Bearer yet).
    expect(loginReq.request.withCredentials).toBe(true);
    loginReq.flush({
      accessToken: 'tok',
      refreshToken: '',
      accessTokenExpiresAt: '2026-11-27T00:15:00Z',
      user,
    });

    client.get(`${api}/workspaces`).subscribe({ error: () => undefined });
    const req = http.expectOne(`${api}/workspaces`);
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.headers.get('Authorization')).toBe('Bearer tok');
    req.flush([]);
    http.verify();
  });
});
