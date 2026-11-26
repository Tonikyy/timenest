import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { switchMap } from 'rxjs';
import { Session } from '../../core/session';

export interface ManagedVacancy {
  id: string;
  title: string;
  description: string;
  serviceTypeId: string;
  startsAt: string;
  durationMinutes: number;
  priceCents: number;
  status: 'Available' | 'Held' | 'Booked' | 'Cancelled';
  cancelledAt: string | null;
  version: string;
}
export interface ManagedVacancyPage {
  items: ManagedVacancy[];
  total: number;
  page: number;
  pageSize: number;
}
export interface Publication {
  title: string;
  description: string;
  serviceTypeId: string;
  date: string;
  time: string;
  durationMinutes: number;
  priceCents: number;
  version?: string;
}

@Injectable({ providedIn: 'root' })
export class PublishingApi {
  private readonly http = inject(HttpClient);
  private readonly session = inject(Session);
  list(page = 1) {
    return this.http.get<ManagedVacancyPage>('/api/business/vacancies', {
      params: new HttpParams().set('page', page),
    });
  }
  save(id: string | null, publication: Publication) {
    return this.session
      .csrf()
      .pipe(
        switchMap(() =>
          id
            ? this.http.put<ManagedVacancy>('/api/business/vacancies/' + id, publication)
            : this.http.post<ManagedVacancy>('/api/business/vacancies', publication),
        ),
      );
  }
  cancel(slot: ManagedVacancy) {
    return this.session.csrf().pipe(
      switchMap(() =>
        this.http.post<ManagedVacancy>('/api/business/vacancies/' + slot.id + '/cancel', {
          version: slot.version,
        }),
      ),
    );
  }
}
