import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AccountPage } from './account';

describe('Account registration', () => {
  let fixture: ComponentFixture<AccountPage>;
  let http: HttpTestingController;
  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [AccountPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { data: { mode: 'register' }, queryParamMap: convertToParamMap({}) },
          },
        },
      ],
    });
    fixture = TestBed.createComponent(AccountPage);
    http = TestBed.inject(HttpTestingController);
    await fixture.whenStable();
  });
  afterEach(() => {
    http.verify();
    fixture.destroy();
  });

  it('validates input without sending an invalid registration', async () => {
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    http.expectNone('/api/account/csrf');
    expect(fixture.nativeElement.textContent).toContain('Anna kelvollinen sähköpostiosoite.');
  });

  it('submits the form and shows generic email instructions without signing in', async () => {
    const root: HTMLElement = fixture.nativeElement;
    const email = root.querySelector<HTMLInputElement>('#email')!;
    const password = root.querySelector<HTMLInputElement>('#password')!;
    email.value = 'demo@example.test';
    email.dispatchEvent(new Event('input'));
    password.value = 'Long enough passphrase';
    password.dispatchEvent(new Event('input'));
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/account/csrf').flush(null);
    const request = http.expectOne('/api/account/register');
    expect(request.request.body.email).toBe('demo@example.test');
    request.flush(null, { status: 202, statusText: 'Accepted' });
    await fixture.whenStable();
    expect(root.querySelector('[role="status"]')?.textContent).toContain(
      'saat sähköpostitse jatko-ohjeet',
    );
    expect(password.value).toBe('');
    expect(fixture.componentInstance.session.user()).toBeNull();
  });

  it('keeps the form usable and shows an error after a failed request', async () => {
    fixture.componentInstance.form.setValue({
      email: 'demo@example.test',
      password: 'Long enough passphrase',
    });
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    http.expectOne('/api/account/csrf').flush(null);
    http.expectOne('/api/account/register').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Toiminto ei onnistunut',
    );
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(false);
  });
});
