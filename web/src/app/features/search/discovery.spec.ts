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

  it.each([
    {
      label: 'date',
      filters: { date: '2027-01-15' },
      expected: { from: '2027-01-14T22:00:00.000Z', to: '2027-01-15T22:00:00.000Z' },
    },
    {
      label: 'area',
      filters: { location: 'espoo' },
      expected: { latitude: '60.1769', longitude: '24.8052' },
    },
    {
      label: 'radius',
      filters: { radiusKm: 5 },
      expected: { radiusKm: '5' },
    },
  ])(
    'keeps the submitted search while paging with an edited $label',
    async ({ filters, expected }) => {
      const slots = Array.from({ length: 8 }, (_, index) => ({
        id: String(index),
        title: 'Test appointment',
        description: 'Fictional test data',
        serviceTypeId: 'hair',
        serviceType: 'Hiukset',
        businessName: 'Demo',
        address: 'Demo 1',
        city: 'Helsinki',
        startsAt: '2027-01-15T08:00:00Z',
        durationMinutes: 45,
        priceCents: 4500,
        currency: 'EUR',
        distanceKm: 0,
        isDemo: true,
      }));
      const initial = http.expectOne((req) => req.url === '/api/vacancies');
      const submittedParams = initial.request.params;
      initial.flush({ items: slots.slice(0, 6), total: 8, page: 1, pageSize: 6 });
      await fixture.whenStable();
      const root: HTMLElement = fixture.nativeElement;
      expect(root.querySelectorAll('.vacancy-card')).toHaveLength(6);

      fixture.componentInstance.filters.patchValue(filters);
      root.querySelectorAll<HTMLButtonElement>('.pagination button')[1].click();
      const next = http.expectOne((req) => req.url === '/api/vacancies');
      expect(next.request.params.get('page')).toBe('2');
      expect(next.request.params.delete('page').toString()).toBe(
        submittedParams.delete('page').toString(),
      );
      next.flush({ items: slots.slice(6), total: 8, page: 2, pageSize: 6 });
      await fixture.whenStable();
      expect(root.querySelectorAll('.vacancy-card')).toHaveLength(2);
      expect(root.querySelector('.message-state')).toBeNull();

      root.querySelectorAll<HTMLButtonElement>('.pagination button')[0].click();
      const previous = http.expectOne((req) => req.url === '/api/vacancies');
      expect(previous.request.params.toString()).toBe(submittedParams.toString());
      previous.flush({ items: slots.slice(0, 6), total: 8, page: 1, pageSize: 6 });
      await fixture.whenStable();

      root
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      const updated = http.expectOne((req) => req.url === '/api/vacancies');
      expect(updated.request.params.get('page')).toBe('1');
      for (const [name, value] of Object.entries(expected)) {
        expect(updated.request.params.get(name)).toBe(value);
      }
      updated.flush({ items: slots.slice(0, 3), total: 3, page: 1, pageSize: 6 });
      await fixture.whenStable();
      expect(root.querySelectorAll('.vacancy-card')).toHaveLength(3);
      expect(root.querySelector('.message-state')).toBeNull();
      expect(root.querySelector('.pagination')).toBeNull();
    },
  );
});
