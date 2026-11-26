import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Discovery } from './discovery';

describe('Discovery', () => {
  let fixture: ComponentFixture<Discovery>;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Discovery],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(Discovery);
    http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/service-types').flush([{ id: 'hair', name: 'Hiukset' }]);
  });
  afterEach(() => {
    http.verify();
    fixture.destroy();
  });
  const empty = { items: [], total: 0, page: 1, pageSize: 6 };

  it('loads nearby slots from the API and renders an honest empty state', async () => {
    const request = http.expectOne((req) => req.url === '/api/vacancies');
    expect(request.request.params.get('latitude')).toBe('60.1699');
    expect(request.request.params.get('radiusKm')).toBe('10');
    request.flush(empty);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Vielä ei löytynyt sopivaa aikaa');
    expect(fixture.componentInstance.loading()).toBe(false);
  });

  it('shows an API failure and allows retry without inserting fake appointments', async () => {
    http
      .expectOne((req) => req.url === '/api/vacancies')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Haku ei juuri nyt onnistunut',
    );
    expect(fixture.componentInstance.result()).toBeNull();
    fixture.componentInstance.search();
    http.expectOne((req) => req.url === '/api/vacancies').flush(empty);
    await fixture.whenStable();
    expect(fixture.componentInstance.error()).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('cancels a stale search when the category changes', () => {
    const original = http.expectOne((req) => req.url === '/api/vacancies');
    fixture.componentInstance.chooseCategory('hair');
    expect(original.cancelled).toBe(true);
    const next = http.expectOne((req) => req.url === '/api/vacancies');
    expect(next.request.params.get('serviceTypeId')).toBe('hair');
    expect(next.request.params.get('page')).toBe('1');
    expect(fixture.componentInstance.loading()).toBe(true);
    next.flush(empty);
  });

  it('sends the selected Finland day as UTC boundaries', () => {
    http.expectOne((req) => req.url === '/api/vacancies').flush(empty);
    fixture.componentInstance.filters.controls.date.setValue('2027-03-28');
    fixture.componentInstance.search();
    const request = http.expectOne((req) => req.url === '/api/vacancies');
    expect(request.request.params.get('from')).toBe('2027-03-27T22:00:00.000Z');
    expect(request.request.params.get('to')).toBe('2027-03-28T21:00:00.000Z');
    request.flush(empty);
  });

  it('resets category, date and location filters', () => {
    http.expectOne((req) => req.url === '/api/vacancies').flush(empty);
    fixture.componentInstance.filters.patchValue({
      location: 'tampere',
      radiusKm: 50,
      date: '2027-01-15',
    });
    fixture.componentInstance.selectedCategory.set('hair');
    fixture.componentInstance.resetFilters();
    const request = http.expectOne((req) => req.url === '/api/vacancies');
    expect(request.request.params.get('latitude')).toBe('60.1699');
    expect(request.request.params.get('radiusKm')).toBe('10');
    expect(request.request.params.has('serviceTypeId')).toBe(false);
    expect(request.request.params.has('from')).toBe(false);
    request.flush(empty);
  });
});
