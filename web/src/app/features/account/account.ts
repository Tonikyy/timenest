import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AccountAction, Session } from '../../core/session';

@Component({
  selector: 'app-account',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './account.html',
})
export class AccountPage {
  readonly session = inject(Session);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly mode: AccountAction | 'home' = this.route.snapshot.data['mode'];
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  private readonly code = this.route.snapshot.queryParamMap.get('code') ?? '';
  private readonly userId = this.route.snapshot.queryParamMap.get('userId') ?? '';
  readonly needsEmail = !['home', 'confirm-email', 'logout'].includes(this.mode);
  readonly needsPassword = ['login', 'register', 'reset-password'].includes(this.mode);
  readonly title = {
    home: 'Oma tili',
    login: 'Kirjaudu sisään',
    register: 'Luo TimeNest-tili',
    'resend-confirmation': 'Uusi vahvistuslinkki',
    'confirm-email': 'Vahvista sähköpostisi',
    'forgot-password': 'Unohtuiko salasana?',
    'reset-password': 'Vaihda salasana',
    logout: 'Kirjaudu ulos',
  }[this.mode];
  readonly form = new FormGroup({
    email: new FormControl(this.route.snapshot.queryParamMap.get('email') ?? '', {
      nonNullable: true,
      validators: this.needsEmail
        ? [Validators.required, Validators.email, Validators.maxLength(254)]
        : [],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: this.needsPassword
        ? [Validators.required, Validators.minLength(12), Validators.maxLength(128)]
        : [],
    }),
  });

  constructor() {
    if (this.code) {
      // Remove bearer links from the address bar and subsequent referrers after reading them.
      void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    }
    if (this.mode === 'home') {
      this.busy.set(true);
      this.session
        .load()
        .pipe(
          takeUntilDestroyed(this.destroyRef),
          finalize(() => this.busy.set(false)),
        )
        .subscribe({
          error: () =>
            this.error.set('Tilin tietoja ei voitu ladata. Päivitä sivu ja yritä uudelleen.'),
        });
    }
  }

  submit() {
    if (this.busy()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid || this.mode === 'home') return;
    if (['confirm-email', 'reset-password'].includes(this.mode) && !this.code) {
      this.error.set('Linkki puuttuu tai on virheellinen. Pyydä uusi linkki.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    this.session
      .submit(this.mode, { ...this.form.getRawValue(), code: this.code, userId: this.userId })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.form.controls.password.reset();
          if (this.mode === 'login') void this.router.navigateByUrl('/account');
          else if (this.mode === 'confirm-email')
            this.message.set('Sähköposti on vahvistettu. Voit nyt kirjautua.');
          else if (this.mode === 'reset-password')
            this.message.set('Salasana on vaihdettu. Kirjaudu uudella salasanalla.');
          else
            this.message.set(
              'Jos osoitteelle voidaan lähettää viesti, saat sähköpostitse jatko-ohjeet.',
            );
        },
        error: (failure) =>
          this.error.set(
            failure.status === 429
              ? 'Liian monta yritystä. Odota hetki ja yritä uudelleen.'
              : this.mode === 'login'
                ? 'Kirjautuminen ei onnistunut. Tarkista tiedot ja vahvista sähköpostisi.'
                : 'Toiminto ei onnistunut. Tarkista tiedot ja linkin voimassaolo tai yritä myöhemmin.',
          ),
      });
  }

  logout() {
    this.busy.set(true);
    this.error.set('');
    this.session
      .submit('logout', {})
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        error: () => this.error.set('Uloskirjautuminen ei onnistunut. Yritä uudelleen.'),
      });
  }
}
