import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { switchMap } from 'rxjs';
import { Session } from '../../core/session';

export interface BusinessProfile {
  id: string;
  name: string;
  address: string;
  city: string;
  latitude: number;
  longitude: number;
  isDemo: boolean;
  version: string;
}
export type BusinessDetails = Pick<
  BusinessProfile,
  'name' | 'address' | 'city' | 'latitude' | 'longitude'
>;

@Injectable({ providedIn: 'root' })
export class BusinessApi {
  private readonly http = inject(HttpClient);
  private readonly session = inject(Session);
  load() {
    return this.http.get<BusinessProfile | null>('/api/business');
  }
  save(details: BusinessDetails, version: string | null) {
    return this.session
      .csrf()
      .pipe(
        switchMap(() =>
          version
            ? this.http.put<BusinessProfile>('/api/business', { ...details, version })
            : this.http.post<BusinessProfile>('/api/business', details),
        ),
      );
  }
}
