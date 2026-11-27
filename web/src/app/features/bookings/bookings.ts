import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { EMPTY, finalize, switchMap } from 'rxjs';
import { appointmentDay, appointmentTime } from '../search/appointment-time';
import { Booking, BookingPage, BookingsApi } from './bookings-api';

@Component({
  selector: 'app-bookings',
  imports: [RouterLink],
  templateUrl: './bookings.html',
})
export class BookingsPage {
  private readonly api = inject(BookingsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  readonly id = this.route.snapshot.paramMap.get('id');
  readonly arrival = this.route.snapshot.queryParamMap.get('checkout');
  readonly booking = signal<Booking | null>(null);
  readonly result = signal<BookingPage | null>(null);
  readonly busy = signal(false);
  readonly signedOut = signal(false);
  readonly error = signal('');
  readonly day = appointmentDay;
  readonly time = appointmentTime;
  readonly price = (cents: number) =>
    new Intl.NumberFormat('fi-FI', { style: 'currency', currency: 'EUR' }).format(cents / 100);
  readonly label = (status: Booking['status']) =>
    ({ Pending: 'Odottaa maksua', Confirmed: 'Vahvistettu', Expired: 'Vanhentunut' })[status];

  constructor() {
    this.load(this.arrival === 'return' || this.arrival === 'cancel');
  }

  load(refresh = false, page = 1) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.signedOut.set(false);
    if (this.id) {
      (refresh ? this.api.refresh(this.id) : this.api.get(this.id))
        .pipe(
          takeUntilDestroyed(this.destroyRef),
          finalize(() => this.busy.set(false)),
        )
        .subscribe({
          next: (booking) => this.booking.set(booking),
          error: (error: HttpErrorResponse) => this.failed(error),
        });
    } else {
      this.result.set(null);
      this.api
        .list(page)
        .pipe(
          takeUntilDestroyed(this.destroyRef),
          finalize(() => this.busy.set(false)),
        )
        .subscribe({
          next: (result) => this.result.set(result),
          error: (error: HttpErrorResponse) => this.failed(error),
        });
    }
  }

  pay() {
    if (
      !this.id ||
      this.busy() ||
      this.booking()?.status !== 'Pending' ||
      this.booking()?.recoveryRequired
    )
      return;
    this.busy.set(true);
    this.error.set('');
    this.api
      .checkout(this.id)
      .pipe(
        switchMap(({ url }) => {
          if (url) {
            globalThis.location.assign(url);
            return EMPTY;
          }
          return this.api.get(this.id!);
        }),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (booking) => this.booking.set(booking),
        error: (error: HttpErrorResponse) => this.failed(error),
      });
  }
  private failed(error: HttpErrorResponse) {
    this.signedOut.set(error.status === 401);
    if (error.status === 401)
      this.error.set('Kirjaudu sisään ja avaa sen jälkeen Omat varaukset. Aiempi varaus säilyy.');
    else if (error.status === 404) this.error.set('Varausta ei löytynyt tälle tilille.');
    else if (error.error?.code === 'paymentsUnavailable')
      this.error.set('Yritys ei voi ottaa maksuja vastaan juuri nyt. Yritä myöhemmin uudelleen.');
    else if (error.status === 429)
      this.error.set('Liian monta yritystä. Odota hetki ja yritä uudelleen.');
    else
      this.error.set(
        'Maksun tilannetta ei saatu varmistettua. Älä aloita uutta varausta; tarkista tämän varauksen tila uudelleen.',
      );
  }
}
