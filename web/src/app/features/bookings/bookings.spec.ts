import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  ActivatedRoute,
  convertToParamMap,
  DefaultUrlSerializer,
  provideRouter,
} from '@angular/router';
import { Observable } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Booking, BookingsApi } from './bookings-api';
import { BookingsPage } from './bookings';

describe('Customer payments', () => {
  let fixture: ComponentFixture<BookingsPage>;
  let http: HttpTestingController;
  const navigate = vi.fn();
  const pending: Booking = {
    id: 'b1522ac5-4adc-44ba-83d9-251906719fe0',
    title: 'Esittelyleikkaus',
    businessName: 'Esittelykampaamo',
    address: 'Testikatu 1',
    city: 'Helsinki',
    startsAt: '2027-01-18T10:00:00Z',
    durationMinutes: 45,
    priceCents: 4500,
    feeCents: 500,
    currency: 'eur',
    status: 'Pending',
    holdExpiresAt: '2027-01-18T09:35:00Z',
    checkoutStartBefore: '2027-01-18T09:04:00Z',
    confirmedAt: null,
    recoveryRequired: false,
  };
  const setup = (id: string | null = pending.id, arrival = '') => {
    TestBed.configureTestingModule({
      imports: [BookingsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap(id ? { id } : {}),
              queryParamMap: convertToParamMap({ checkout: arrival }),
            },
          },
        },
      ],
    });
    fixture = TestBed.createComponent(BookingsPage);
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

  it.each(['', 'return', 'cancel'])(
    'rejects a crafted route before making requests (%s)',
    async (arrival) => {
      const tree = new DefaultUrlSerializer().parse(
        '/bookings/..%2Faccount%2Flogout%3Fx%3D?checkout=' + arrival,
      );
      const id = tree.root.children['primary'].segments[1].path;
      expect(id).toBe('../account/logout?x=');
      setup(id, arrival);
      await fixture.whenStable();
      expect(fixture.nativeElement.textContent).toContain('Varausta ei löytynyt tälle tilille.');
      http.expectNone(() => true);
    },
  );

  it.each(['get', 'refresh', 'checkout'] as const)(
    'rejects invalid IDs at the %s API boundary',
    (method) => {
      setup();
      http.expectOne('/api/bookings/' + pending.id).flush(pending);
      const error = vi.fn();
      const request: Observable<unknown> =
        TestBed.inject(BookingsApi)[method]('../account/logout?x=');
      request.subscribe({ error });
      expect(error).toHaveBeenCalledOnce();
      expect(error.mock.calls[0][0]).toBeInstanceOf(HttpErrorResponse);
      expect(error.mock.calls[0][0].status).toBe(404);
      http.expectNone(() => true);
    },
  );

  it('does not trust the return query and confirms only after server refresh', async () => {
    setup(pending.id, 'return');
    http.expectOne('/api/account/csrf').flush(null);
    const request = http.expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0/refresh');
    expect(request.request.method).toBe('POST');
    request.flush(pending);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Odottaa maksua');
    expect(fixture.nativeElement.textContent).not.toContain('Maksu on vahvistettu');
    fixture.componentInstance.load(true);
    http.expectOne('/api/account/csrf').flush(null);
    http
      .expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0/refresh')
      .flush({ ...pending, status: 'Confirmed' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Maksu on vahvistettu');
    expect(fixture.nativeElement.querySelector('.primary-button')).toBeNull();
  });
  it('uses CSRF and one request for repeated payment clicks', async () => {
    setup();
    http.expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0').flush(pending);
    fixture.componentInstance.pay();
    fixture.componentInstance.pay();
    http.expectOne('/api/account/csrf').flush(null);
    const request = http.expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0/checkout');
    expect(request.request.body).toEqual({});
    request.flush({ url: 'https://checkout.stripe.com/c/pay/test-only' });
    expect(navigate).toHaveBeenCalledExactlyOnceWith('https://checkout.stripe.com/c/pay/test-only');
  });
  it('keeps loading until a closed checkout state is reread', async () => {
    setup();
    http.expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0').flush(pending);
    fixture.componentInstance.pay();
    http.expectOne('/api/account/csrf').flush(null);
    http
      .expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0/checkout')
      .flush({ url: null });
    expect(fixture.componentInstance.busy()).toBe(true);
    http
      .expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0')
      .flush({ ...pending, status: 'Expired' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Vanhentunut');
    expect(navigate).not.toHaveBeenCalled();
  });
  it.each(['Confirmed', 'Expired', 'Recovery'] as const)(
    'prevents checkout for %s',
    async (state) => {
      setup();
      http.expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0').flush({
        ...pending,
        status: state === 'Recovery' ? 'Pending' : state,
        recoveryRequired: state === 'Recovery',
      });
      await fixture.whenStable();
      fixture.componentInstance.pay();
      http.expectNone('/api/account/csrf');
      expect(fixture.nativeElement.querySelector('.primary-button')).toBeNull();
    },
  );
  it('does not call a failed provider check paid or expired', async () => {
    setup(pending.id, 'cancel');
    http.expectOne('/api/account/csrf').flush(null);
    http
      .expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0/refresh')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(fixture.componentInstance.booking()).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });
  it('offers sign-in recovery', async () => {
    setup();
    http
      .expectOne('/api/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('a[href="/account/login"]')).not.toBeNull();
  });
  it('renders an honest empty list and paginates', async () => {
    setup(null);
    http.expectOne('/api/bookings?page=1').flush({ items: [], total: 0, page: 1, pageSize: 20 });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Sinulla ei ole vielä varauksia');
    fixture.componentInstance.load(false, 2);
    http
      .expectOne('/api/bookings?page=2')
      .flush({ items: [pending], total: 21, page: 2, pageSize: 20 });
    await fixture.whenStable();
    expect(
      fixture.nativeElement.querySelector(
        'a[href="/bookings/b1522ac5-4adc-44ba-83d9-251906719fe0"]',
      ),
    ).not.toBeNull();
  });
});
