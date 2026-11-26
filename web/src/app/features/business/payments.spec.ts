import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OnboardingStatus } from './payments-api';
import { PaymentsPage } from './payments';

describe('Stripe onboarding', () => {
  let fixture: ComponentFixture<PaymentsPage>;
  let http: HttpTestingController;
  const endpoint = '/api/business/payments';
  const navigate = vi.fn();
  const status = (state: OnboardingStatus['state']): OnboardingStatus => ({
    state,
    detailsSubmitted: state === 'ready',
    chargesEnabled: state === 'ready',
    payoutsEnabled: state === 'ready',
    transfersActive: state === 'ready',
  });
  const setup = (arrival = '') => {
    TestBed.configureTestingModule({
      imports: [PaymentsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({ stripe: arrival }) } },
        },
      ],
    });
    fixture = TestBed.createComponent(PaymentsPage);
    http = TestBed.inject(HttpTestingController);
  };
  beforeEach(() => {
    navigate.mockClear();
    vi.stubGlobal('location', { assign: navigate });
  });
  afterEach(() => {
    http.verify();
    fixture.destroy();
    vi.unstubAllGlobals();
  });

  it('shows sign-in for an expired session without creating a link', async () => {
    setup('refresh');
    http.expectOne(endpoint).flush({}, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('a[href="/account/login"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.primary-button')).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it.each(['noBusiness', 'unavailable', 'recoveryRequired'] as const)(
    'prevents starting when status is %s',
    async (state) => {
      setup();
      http.expectOne(endpoint).flush(status(state));
      await fixture.whenStable();
      expect(fixture.nativeElement.querySelector('.primary-button')).toBeNull();
      fixture.componentInstance.start();
      http.expectNone('/api/account/csrf');
      expect(navigate).not.toHaveBeenCalled();
    },
  );

  it('fetches CSRF before creating a link and redirects only on success', async () => {
    setup();
    http.expectOne(endpoint).flush(status('notStarted'));
    await fixture.whenStable();
    fixture.nativeElement.querySelector('.primary-button').click();
    fixture.componentInstance.start(); // Double clicks must not create duplicate requests.
    const csrf = http.expectOne('/api/account/csrf');
    http.expectNone(endpoint + '/onboarding-link');
    csrf.flush(null);
    const link = http.expectOne(endpoint + '/onboarding-link');
    expect(link.request.method).toBe('POST');
    expect(link.request.body).toEqual({});
    expect(navigate).not.toHaveBeenCalled();
    link.flush({ url: 'https://connect.stripe.com/setup/test-only' });
    expect(navigate).toHaveBeenCalledExactlyOnceWith('https://connect.stripe.com/setup/test-only');
  });

  it('never treats the return query as completion and rereads current status', async () => {
    setup('return');
    http.expectOne(endpoint).flush(status('incomplete'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Käyttöönotto on kesken');
    expect(fixture.nativeElement.textContent).not.toContain('Stripe-käyttöönotto on valmis');
    fixture.nativeElement.querySelector('.secondary-button').click();
    http.expectOne(endpoint).flush(status('ready'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Stripe-käyttöönotto on valmis esittelyyn');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('offers an explicit fresh link after Stripe reports expiry', async () => {
    setup('refresh');
    http.expectOne(endpoint).flush(status('incomplete'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('vanhentunut tai jo käytetty');
    http.expectNone(endpoint + '/onboarding-link');
    fixture.nativeElement.querySelector('.primary-button').click();
    http.expectOne('/api/account/csrf').flush(null);
    http
      .expectOne(endpoint + '/onboarding-link')
      .flush({ url: 'https://connect.stripe.com/setup/fresh' });
    expect(navigate).toHaveBeenCalledExactlyOnceWith('https://connect.stripe.com/setup/fresh');
  });

  it('removes stale readiness when status cannot be verified', async () => {
    setup();
    http.expectOne(endpoint).flush(status('ready'));
    await fixture.whenStable();
    fixture.nativeElement.querySelector('.secondary-button').click();
    http.expectOne(endpoint).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Stripe-käyttöönotto on valmis');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('recovers from a link failure by reloading the pending attempt', async () => {
    setup();
    http.expectOne(endpoint).flush(status('notStarted'));
    await fixture.whenStable();
    fixture.nativeElement.querySelector('.primary-button').click();
    http.expectOne('/api/account/csrf').flush(null);
    http
      .expectOne(endpoint + '/onboarding-link')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(navigate).not.toHaveBeenCalled();
    fixture.nativeElement.querySelector('.secondary-button').click();
    http.expectOne(endpoint).flush(status('pendingCreation'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Aloitus jäi kesken');
    expect(fixture.nativeElement.querySelector('.primary-button').disabled).toBe(false);
  });

  it('does not request a link after CSRF failure', async () => {
    setup();
    http.expectOne(endpoint).flush(status('notStarted'));
    await fixture.whenStable();
    fixture.nativeElement.querySelector('.primary-button').click();
    http.expectOne('/api/account/csrf').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    http.expectNone(endpoint + '/onboarding-link');
    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });
});
