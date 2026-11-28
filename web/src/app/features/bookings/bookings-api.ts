import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { switchMap, throwError } from 'rxjs';
import { Session } from '../../core/session';

export interface Booking {
  id: string;
  title: string;
  businessName: string;
  address: string;
  city: string;
  startsAt: string;
  durationMinutes: number;
  priceCents: number;
  feeCents: number;
  currency: string;
  status: 'Pending' | 'Confirmed' | 'Expired' | 'Cancelled';
  holdExpiresAt: string;
  checkoutStartBefore: string;
  confirmedAt: string | null;
  recoveryRequired: boolean;
  cancellationPolicy: 'LegacyFullRefundBeforeStart' | 'LastMinuteNoRefund';
  cancellationRequested: boolean;
  canCancel: boolean;
  cancellation: {
    actor: 'Customer' | 'Business';
    cancelledAt: string;
    refundAmountCents: number;
  } | null;
  refund: {
    status: 'Queued' | 'Pending' | 'Succeeded' | 'Failed' | 'RecoveryRequired';
    amountCents: number;
  } | null;
}
export interface BookingPage {
  items: Booking[];
  total: number;
  page: number;
  pageSize: number;
}

const bookingIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

@Injectable({ providedIn: 'root' })
export class BookingsApi {
  private readonly http = inject(HttpClient);
  private readonly session = inject(Session);
  list(page: number, business = false) {
    return this.http.get<BookingPage>(business ? '/api/business/bookings' : '/api/bookings', {
      params: { page },
    });
  }
  get(id: string, business = false) {
    if (!bookingIdPattern.test(id)) return throwError(() => new HttpErrorResponse({ status: 404 }));
    return this.http.get<Booking>((business ? '/api/business/bookings/' : '/api/bookings/') + id);
  }
  reserve(vacancyId: string) {
    return this.session
      .csrf()
      .pipe(switchMap(() => this.http.post<{ id: string }>('/api/bookings', { vacancyId })));
  }
  checkout(id: string) {
    if (!bookingIdPattern.test(id)) return throwError(() => new HttpErrorResponse({ status: 404 }));
    return this.session
      .csrf()
      .pipe(
        switchMap(() =>
          this.http.post<{ url: string | null }>('/api/bookings/' + id + '/checkout', {}),
        ),
      );
  }
  cancel(id: string, business = false) {
    if (!bookingIdPattern.test(id)) return throwError(() => new HttpErrorResponse({ status: 404 }));
    return this.session
      .csrf()
      .pipe(
        switchMap(() =>
          this.http.post<Booking>(
            (business ? '/api/business/bookings/' : '/api/bookings/') + id + '/cancel',
            {},
          ),
        ),
      );
  }
  abandon(id: string) {
    if (!bookingIdPattern.test(id)) return throwError(() => new HttpErrorResponse({ status: 404 }));
    return this.session
      .csrf()
      .pipe(switchMap(() => this.http.post<Booking>('/api/bookings/' + id + '/abandon', {})));
  }
  refresh(id: string) {
    if (!bookingIdPattern.test(id)) return throwError(() => new HttpErrorResponse({ status: 404 }));
    return this.session
      .csrf()
      .pipe(switchMap(() => this.http.post<Booking>('/api/bookings/' + id + '/refresh', {})));
  }
}
