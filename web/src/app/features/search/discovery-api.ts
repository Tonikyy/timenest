import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface ServiceType {
  id: string;
  name: string;
}
export interface Vacancy {
  id: string;
  title: string;
  description: string;
  serviceTypeId: string;
  serviceType: string;
  businessName: string;
  address: string;
  city: string;
  startsAt: string;
  durationMinutes: number;
  priceCents: number;
  currency: string;
  distanceKm: number;
  isDemo: boolean;
}
export interface SearchResult {
  items: Vacancy[];
  total: number;
  page: number;
  pageSize: number;
}
export interface SearchQuery {
  latitude: number;
  longitude: number;
  radiusKm: number;
  page: number;
  pageSize: number;
  serviceTypeId?: string;
  from?: string;
  to?: string;
}

@Injectable({ providedIn: 'root' })
export class DiscoveryApi {
  private readonly http = inject(HttpClient);
  categories() {
    return this.http.get<ServiceType[]>('/api/service-types');
  }
  search(query: SearchQuery) {
    let params = new HttpParams();
    for (const [name, value] of Object.entries(query)) {
      if (value !== undefined && value !== '') params = params.set(name, value);
    }
    return this.http.get<SearchResult>('/api/vacancies', { params });
  }
}
