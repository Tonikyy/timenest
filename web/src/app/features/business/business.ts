import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { BusinessApi, BusinessProfile } from './business-api';

@Component({
  selector: 'app-business',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './business.html',
})
export class BusinessPage {
  private readonly api = inject(BusinessApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly profile = signal<BusinessProfile | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loaded = signal(false);
  readonly signedOut = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(160)],
    }),
    address: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(240)],
    }),
    city: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(100)],
    }),
    latitude: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(-90),
      Validators.max(90),
    ]),
    longitude: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(-180),
      Validators.max(180),
    ]),
  });

  constructor() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.loaded.set(false);
    this.error.set('');
    this.message.set('');
    this.signedOut.set(false);
    this.api
      .load()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (profile) => {
          this.profile.set(profile);
          if (profile) this.form.patchValue(profile);
          this.loaded.set(true);
        },
        error: (failure) => {
          this.signedOut.set(failure.status === 401);
          this.error.set(
            failure.status === 401
              ? 'Kirjaudu sisään hallitaksesi yritystäsi.'
              : 'Profiilia ei voitu ladata. Yritä uudelleen.',
          );
        },
      });
  }

  save() {
    this.form.markAllAsTouched();
    if (!this.loaded() || this.form.invalid || this.saving()) return;
    const values = this.form.getRawValue();
    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    this.api
      .save(
        { ...values, latitude: values.latitude!, longitude: values.longitude! },
        this.profile()?.version ?? null,
      )
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (profile) => {
          this.profile.set(profile);
          this.form.patchValue(profile);
          this.message.set('Yritysprofiili on tallennettu.');
        },
        error: (failure) =>
          this.error.set(
            failure.status === 409
              ? 'Profiili on muuttunut tai tilillä on jo yritys. Lataa profiili uudelleen ennen tallentamista.'
              : failure.status === 401
                ? 'Istuntosi on päättynyt. Kirjaudu uudelleen.'
                : 'Tallennus ei onnistunut. Tarkista tiedot ja yritä uudelleen.',
          ),
      });
  }
}
