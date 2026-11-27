import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { BookingsApi } from '../bookings/bookings-api';
import { Component, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { catchError, finalize, of, Subject, switchMap } from 'rxjs';
import { appointmentDay, appointmentTime, helsinkiDayRange } from './appointment-time';
import { DiscoveryApi, SearchQuery, SearchResult, ServiceType, Vacancy } from './discovery-api';

const locations = [
  { id: 'helsinki', name: 'Helsingin keskusta', latitude: 60.1699, longitude: 24.9384 },
  { id: 'espoo', name: 'Espoo, Tapiola', latitude: 60.1769, longitude: 24.8052 },
  { id: 'tampere', name: 'Tampereen keskusta', latitude: 61.4978, longitude: 23.761 },
];
const euros = new Intl.NumberFormat('fi-FI', {
  style: 'currency',
  currency: 'EUR',
  maximumFractionDigits: 2,
  minimumFractionDigits: 0,
});
const distance = new Intl.NumberFormat('fi-FI', { maximumFractionDigits: 1 });

@Component({
  selector: 'app-discovery',
  imports: [ReactiveFormsModule],
  templateUrl: './discovery.html',
  styleUrl: './discovery.css',
})
export class Discovery {
  private readonly api = inject(DiscoveryApi);
  private readonly bookings = inject(BookingsApi);
  private readonly router = inject(Router);
  readonly reserving = signal(false);
  readonly bookingError = signal('');
  readonly signedOut = signal(false);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<SearchQuery>();
  private submittedQuery: SearchQuery | null = null;
  private readonly details = viewChild.required<ElementRef<HTMLDialogElement>>('details');

  readonly locations = locations;
  readonly categories = signal<ServiceType[]>([]);
  readonly categoryError = signal(false);
  readonly selectedCategory = signal('');
  readonly result = signal<SearchResult | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly locationMessage = signal('');
  readonly locating = signal(false);
  readonly position = signal<{ latitude: number; longitude: number } | null>(null);
  readonly selected = signal<Vacancy | null>(null);
  readonly filters = new FormGroup({
    location: new FormControl('helsinki', { nonNullable: true }),
    radiusKm: new FormControl(10, { nonNullable: true }),
    date: new FormControl('', { nonNullable: true }),
  });
  readonly day = appointmentDay;
  readonly time = appointmentTime;
  readonly price = (cents: number) => euros.format(cents / 100);
  readonly distance = (km: number) => distance.format(km);

  constructor() {
    this.requests
      .pipe(
        switchMap((query) => {
          this.loading.set(true);
          this.error.set(null);
          this.result.set(null);
          return this.api.search(query).pipe(
            catchError(() => {
              this.error.set('Aikoja ei voitu hakea juuri nyt. Yritä hetken kuluttua uudelleen.');
              return of(null);
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.result.set(result));
    this.loadCategories();
    this.search();
  }

  loadCategories() {
    this.categoryError.set(false);
    this.api
      .categories()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (categories) => this.categories.set(categories),
        error: () => this.categoryError.set(true),
      });
  }

  search() {
    const filters = this.filters.getRawValue();
    const location =
      filters.location === 'current'
        ? this.position()
        : locations.find((item) => item.id === filters.location);
    if (!location) return;
    let range = {};
    if (filters.date) {
      try {
        range = helsinkiDayRange(filters.date);
      } catch {
        this.error.set('Tarkista valitsemasi päivämäärä.');
        return;
      }
    }
    this.submittedQuery = {
      latitude: location.latitude,
      longitude: location.longitude,
      radiusKm: filters.radiusKm,
      page: 1,
      pageSize: 6,
      serviceTypeId: this.selectedCategory(),
      ...range,
    };
    this.requests.next(this.submittedQuery);
  }

  goToPage(page: number) {
    if (this.submittedQuery) {
      this.requests.next({ ...this.submittedQuery, page });
    }
  }

  chooseCategory(id: string) {
    this.selectedCategory.set(id);
    this.search();
  }

  resetFilters() {
    this.filters.reset({ location: 'helsinki', radiusKm: 10, date: '' });
    this.selectedCategory.set('');
    this.locationMessage.set('');
    this.search();
  }

  useLocation() {
    if (!navigator.geolocation) {
      this.locationMessage.set('Selaimesi ei tue paikannusta. Valitse alue valikosta.');
      return;
    }
    this.locating.set(true);
    this.locationMessage.set('');
    navigator.geolocation.getCurrentPosition(
      (position) => {
        if (this.destroyRef.destroyed) return;
        this.position.set({
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
        });
        this.filters.controls.location.setValue('current');
        this.locating.set(false);
        this.locationMessage.set('Sijaintisi on käytössä vain tässä haussa.');
        this.search();
      },
      () => {
        if (this.destroyRef.destroyed) return;
        this.locating.set(false);
        this.locationMessage.set('Sijaintia ei saatu. Voit valita alueen valikosta.');
      },
      { timeout: 10_000, maximumAge: 60_000 },
    );
  }

  reserve() {
    const slot = this.selected();
    if (!slot || this.reserving()) return;
    this.reserving.set(true);
    this.bookingError.set('');
    this.signedOut.set(false);
    this.bookings
      .reserve(slot.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.reserving.set(false)),
      )
      .subscribe({
        next: ({ id }) => {
          this.closeDetails();
          void this.router.navigate(['/bookings', id]);
        },
        error: (error: HttpErrorResponse) => {
          this.signedOut.set(error.status === 401);
          if (error.status === 401)
            this.bookingError.set('Kirjaudu sisään varataksesi esittelyajan.');
          else if (error.error?.code === 'paymentsUnavailable')
            this.bookingError.set(
              'Tämän yrityksen testimaksut eivät ole vielä käytössä. Valitse toinen aika.',
            );
          else if (error.error?.code === 'priceTooLow')
            this.bookingError.set(
              'Tämän ajan hintaa ei voi maksaa palvelussa. Valitse toinen aika.',
            );
          else if (error.error?.code === 'notBookable')
            this.bookingError.set('Et voi varata oman yrityksesi aikaa.');
          else if (error.status === 409)
            this.bookingError.set(
              'Aika ei ole enää varattavissa tai se alkaa alle 40 minuutin kuluttua. Päivitä haku ja valitse toinen aika.',
            );
          else
            this.bookingError.set(
              'Varausta ei voitu varmistaa. Tarkista Omat varaukset ennen uutta yritystä.',
            );
        },
      });
  }

  openDetails(slot: Vacancy) {
    this.bookingError.set('');
    this.signedOut.set(false);
    this.selected.set(slot);
    this.details().nativeElement.showModal();
  }
  closeDetails() {
    this.details().nativeElement.close();
  }
  closeBackdrop(event: MouseEvent) {
    if (event.target === event.currentTarget) this.closeDetails();
  }
}
