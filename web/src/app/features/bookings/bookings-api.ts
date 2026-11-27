import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { switchMap } from 'rxjs';
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
  status: 'Pending' | 'Confirmed' | 'Expired';
  holdExpiresAt: string;
  checkoutStartBefore: string;
  confirmedAt: string | null;
  recoveryRequired: boolean;
}
export interface BookingPage {
  items: Booking[];
  total: number;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class BookingsApi {
  private readonly http = inject(HttpClient);
  private readonly session = inject(Session);
  list(page: number) {
    return this.http.get<BookingPage>('/api/bookings', { params: { page } });
  }
  get(id: string) {
    return this.http.get<Booking>('/api/bookings/' + id);
  }
  reserve(vacancyId: string) {
    return this.session
      .csrf()
      .pipe(switchMap(() => this.http.post<{ id: string }>('/api/bookings', { vacancyId })));
  }
  checkout(id: string) {
    return this.session
      .csrf()
      .pipe(
        switchMap(() =>
          this.http.post<{ url: string | null }>('/api/bookings/' + id + '/checkout', {}),
        ),
      );
  }
  refresh(id: string) {
    return this.session
      .csrf()
      .pipe(switchMap(() => this.http.post<Booking>('/api/bookings/' + id + '/refresh', {})));
  }
}
