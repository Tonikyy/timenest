import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize } from 'rxjs';
import { OnboardingStatus, PaymentsApi } from './payments-api';

@Component({
  selector: 'app-payments',
  imports: [RouterLink],
  templateUrl: './payments.html',
})
export class PaymentsPage {
  private readonly api = inject(PaymentsApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly arrival = inject(ActivatedRoute).snapshot.queryParamMap.get('stripe');
  readonly status = signal<OnboardingStatus | null>(null);
  readonly loading = signal(false);
  readonly starting = signal(false);
  readonly signedOut = signal(false);
  readonly error = signal('');
  readonly canContinue = computed(() =>
    ['notStarted', 'pendingCreation', 'incomplete', 'ready'].includes(this.status()?.state ?? ''),
  );

  constructor() {
    this.load();
  }

  load() {
    if (this.loading() || this.starting()) return;
    this.loading.set(true);
    this.status.set(null);
    this.signedOut.set(false);
    this.error.set('');
    this.api
      .load()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (status) => this.status.set(status),
        error: (failure: HttpErrorResponse) => this.failed(failure),
      });
  }

  start() {
    if (!this.canContinue() || this.starting() || this.loading()) return;
    this.starting.set(true);
    this.error.set('');
    this.api
      .start()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.starting.set(false)),
      )
      .subscribe({
        next: ({ url }) => globalThis.location.assign(url),
        error: (failure: HttpErrorResponse) => {
          this.status.set(null);
          this.failed(failure);
        },
      });
  }

  private failed(failure: HttpErrorResponse) {
    this.signedOut.set(failure.status === 401);
    if (failure.status === 401) {
      this.error.set(
        'Kirjaudu sisään ja palaa tämän jälkeen maksujen käyttöönottoon yrityksen valikosta.',
      );
    } else if (failure.status === 429) {
      this.error.set('Liian monta yritystä. Odota hetki ja tarkista tila uudelleen.');
    } else if (failure.error?.code === 'recoveryRequired') {
      this.error.set(
        'Aiemman aloituksen tilanne täytyy selvittää. Ota yhteyttä TimeNestin ylläpitoon.',
      );
    } else if (failure.error?.code === 'noBusiness') {
      this.error.set('Luo ensin yritysprofiili yrityksen valikosta.');
    } else if (failure.error?.code === 'demoOnly') {
      this.error.set('Maksujen kokeilu on käytettävissä vain esittely-yrityksille.');
    } else {
      this.error.set('Maksujen käyttöönoton tietoja ei saatu. Tarkista tila ja yritä uudelleen.');
    }
  }
}
