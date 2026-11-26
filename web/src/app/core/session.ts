import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { switchMap, tap } from 'rxjs';

export interface Account {
  id: string;
  email: string;
}
export type AccountAction =
  | 'login'
  | 'logout'
  | 'register'
  | 'resend-confirmation'
  | 'confirm-email'
  | 'forgot-password'
  | 'reset-password';

@Injectable({ providedIn: 'root' })
export class Session {
  private readonly http = inject(HttpClient);
  readonly user = signal<Account | null>(null);

  load() {
    return this.http
      .get<Account | null>('/api/account/me')
      .pipe(tap((user) => this.user.set(user)));
  }

  // Refresh against the current cookie identity before each mutation (also after login/logout).
  csrf() {
    return this.http.get<void>('/api/account/csrf');
  }

  submit(action: AccountAction, body: object) {
    return this.csrf().pipe(
      switchMap(() => this.http.post<Account | null>('/api/account/' + action, body)),
      tap((account) => {
        if (action === 'login') this.user.set(account);
        if (action === 'logout') this.user.set(null);
      }),
    );
  }
}
