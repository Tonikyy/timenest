import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { switchMap } from 'rxjs';
import { Session } from '../../core/session';

export interface OnboardingStatus {
  state:
    | 'noBusiness'
    | 'unavailable'
    | 'notStarted'
    | 'pendingCreation'
    | 'recoveryRequired'
    | 'incomplete'
    | 'ready';
  detailsSubmitted: boolean;
  chargesEnabled: boolean;
  payoutsEnabled: boolean;
  transfersActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class PaymentsApi {
  private readonly http = inject(HttpClient);
  private readonly session = inject(Session);

  load() {
    return this.http.get<OnboardingStatus>('/api/business/payments');
  }

  start() {
    return this.session
      .csrf()
      .pipe(
        switchMap(() =>
          this.http.post<{ url: string }>('/api/business/payments/onboarding-link', {}),
        ),
      );
  }
}
