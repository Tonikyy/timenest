import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Session } from './session';

describe('Session', () => {
  let session: Session;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    session = TestBed.inject(Session);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    document.cookie = 'XSRF-TOKEN=; Max-Age=0; Path=/';
  });

  it('gets a fresh CSRF cookie before login and logout', () => {
    const account = { id: 'user', email: 'demo@example.test' };
    session.submit('login', { email: account.email, password: 'long passphrase' }).subscribe();
    http.expectNone('/api/account/login');
    document.cookie = 'XSRF-TOKEN=anonymous; Path=/';
    http.expectOne('/api/account/csrf').flush(null);
    const login = http.expectOne('/api/account/login');
    expect(login.request.headers.get('X-XSRF-TOKEN')).toBe('anonymous');
    login.flush(account);
    expect(session.user()).toEqual(account);
    session.submit('logout', {}).subscribe();
    http.expectNone('/api/account/logout');
    document.cookie = 'XSRF-TOKEN=authenticated; Path=/';
    http.expectOne('/api/account/csrf').flush(null);
    const logout = http.expectOne('/api/account/logout');
    expect(logout.request.headers.get('X-XSRF-TOKEN')).toBe('authenticated');
    logout.flush(null);
    expect(session.user()).toBeNull();
  });

  it('does not report login success on authentication failure', () => {
    let failed = false;
    session.submit('login', {}).subscribe({
      error: () => {
        failed = true;
      },
    });
    http.expectOne('/api/account/csrf').flush(null);
    http.expectOne('/api/account/login').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(failed).toBe(true);
    expect(session.user()).toBeNull();
  });

  it('does not send a mutation if CSRF initialization fails', () => {
    let failed = false;
    session.submit('register', {}).subscribe({
      error: () => {
        failed = true;
      },
    });
    http.expectOne('/api/account/csrf').flush({}, { status: 503, statusText: 'Unavailable' });
    http.expectNone('/api/account/register');
    expect(failed).toBe(true);
  });
});
