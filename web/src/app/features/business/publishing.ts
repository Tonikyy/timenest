import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { DiscoveryApi, ServiceType } from '../search/discovery-api';
import { appointmentDay, appointmentTime } from '../search/appointment-time';
import { BusinessApi, BusinessProfile } from './business-api';
import { ManagedVacancy, ManagedVacancyPage, PublishingApi } from './publishing-api';

const inputTime = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Europe/Helsinki',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});
const euros = new Intl.NumberFormat('fi-FI', { style: 'currency', currency: 'EUR' });

@Component({
  selector: 'app-publishing',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './publishing.html',
})
export class PublishingPage {
  private readonly api = inject(PublishingApi);
  private readonly titleInput = viewChild<ElementRef<HTMLInputElement>>('titleInput');
  private readonly businesses = inject(BusinessApi);
  private readonly discovery = inject(DiscoveryApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly profile = signal<BusinessProfile | null>(null);
  readonly categories = signal<ServiceType[]>([]);
  readonly result = signal<ManagedVacancyPage | null>(null);
  readonly selected = signal<ManagedVacancy | null>(null);
  readonly pendingCancel = signal<string | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly signedOut = signal(false);
  readonly day = appointmentDay;
  readonly time = appointmentTime;
  readonly price = (cents: number) => euros.format(cents / 100);
  readonly status = {
    Available: 'Vapaa',
    Held: 'Varauksessa',
    Booked: 'Varattu',
    Cancelled: 'Peruttu',
  };
  readonly form = new FormGroup({
    title: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(160)],
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(1000)],
    }),
    serviceTypeId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    date: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    time: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    durationMinutes: new FormControl(45, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(5), Validators.max(480)],
    }),
    price: new FormControl('', {
      nonNullable: true,
      validators: [
        Validators.required,
        Validators.pattern(/^\d+(?:[.,]\d{1,2})?$/),
        Validators.maxLength(11),
      ],
    }),
  });

  constructor() {
    this.load();
  }

  load(page = 1) {
    this.loading.set(true);
    this.error.set('');
    this.signedOut.set(false);
    this.pendingCancel.set(null);
    forkJoin({
      profile: this.businesses.load(),
      categories: this.discovery.categories(),
      result: this.api.list(page),
    })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.profile.set(data.profile);
          this.categories.set(data.categories);
          this.result.set(data.result);
        },
        error: (failure) => {
          this.result.set(null);
          this.signedOut.set(failure.status === 401);
          this.showError(failure);
        },
      });
  }

  editable(slot: ManagedVacancy) {
    return slot.status === 'Available' && new Date(slot.startsAt).getTime() > Date.now();
  }

  edit(slot: ManagedVacancy) {
    this.selected.set(slot);
    this.error.set('');
    this.message.set('');
    const fields = Object.fromEntries(
      inputTime.formatToParts(new Date(slot.startsAt)).map((part) => [part.type, part.value]),
    );
    this.form.setValue({
      title: slot.title,
      description: slot.description,
      serviceTypeId: slot.serviceTypeId,
      date: fields['year'] + '-' + fields['month'] + '-' + fields['day'],
      time: fields['hour'] + ':' + fields['minute'],
      durationMinutes: slot.durationMinutes,
      price: (slot.priceCents / 100).toFixed(2),
    });
    this.titleInput()?.nativeElement.focus();
  }

  reset() {
    this.selected.set(null);
    this.form.reset();
  }

  save() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving() || !this.profile()) return;
    const { price, ...values } = this.form.getRawValue();
    const priceCents = Math.round(Number(price.replace(',', '.')) * 100);
    if (priceCents <= 0 || priceCents > 2147483647 || !Number.isInteger(values.durationMinutes)) {
      this.error.set('Tarkista hinta ja kokonaisina minuutteina annettu kesto.');
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    this.api
      .save(this.selected()?.id ?? null, {
        ...values,
        time: values.time + ':00',
        priceCents,
        version: this.selected()?.version,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.reset();
          this.message.set('Esittelyaika on tallennettu ja näkyy asiakkaiden haussa.');
          this.load();
        },
        error: (failure) => this.showError(failure),
      });
  }

  cancel(slot: ManagedVacancy) {
    if (this.saving()) return;
    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    this.api
      .cancel(slot)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          if (this.selected()?.id === slot.id) this.reset();
          this.message.set('Esittelyaika on peruttu.');
          this.load(this.result()?.page ?? 1);
        },
        error: (failure) => this.showError(failure),
      });
  }

  private showError(failure: HttpErrorResponse) {
    this.error.set(
      failure.status === 401
        ? 'Kirjaudu sisään hallitaksesi yrityksesi aikoja.'
        : failure.status === 409
          ? 'Tiedot ovat muuttuneet tai aikaa ei voi muokata. Lataa lista uudelleen ja valitse aika muokattavaksi.'
          : failure.status === 400
            ? 'Tarkista tiedot. Ajan on oltava tulevaisuudessa ja kellonajan yksiselitteinen Suomen ajassa.'
            : 'Toiminto ei onnistunut. Yritä uudelleen.',
    );
  }
}
