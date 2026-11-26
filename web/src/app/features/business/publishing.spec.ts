import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PublishingPage } from './publishing';
import { ManagedVacancy } from './publishing-api';

describe('Demo publishing', () => {
  let fixture: ComponentFixture<PublishingPage>;
  let http: HttpTestingController;
  const business = {
    id: 'business',
    name: 'Demo',
    address: 'Demo 1',
    city: 'Demo',
    latitude: 0,
    longitude: 0,
    isDemo: true,
    version: 'AQIDBAUGBwg=',
  };
  const slot: ManagedVacancy = {
    id: 'slot',
    title: 'Fictional massage',
    description: 'Demo',
    serviceTypeId: 'massage',
    startsAt: '2027-07-15T09:00:00Z',
    durationMinutes: 45,
    priceCents: 4550,
    status: 'Available',
    cancelledAt: null,
    version: 'AQIDBAUGBwg=',
  };
  function load(items: ManagedVacancy[] = [], profile: typeof business | null = business) {
    http.expectOne('/api/business').flush(profile);
    http.expectOne('/api/service-types').flush([{ id: 'massage', name: 'Hieronta' }]);
    http
      .expectOne((req) => req.url === '/api/business/vacancies')
      .flush({ items, total: items.length, page: 1, pageSize: 12 });
  }
  beforeEach(() => {
    vi.spyOn(Date, 'now').mockReturnValue(Date.UTC(2027, 0, 15));
    TestBed.configureTestingModule({
      imports: [PublishingPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    fixture = TestBed.createComponent(PublishingPage);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    fixture.destroy();
    vi.restoreAllMocks();
  });

  it('requires a business profile before showing a publishing form', async () => {
    load([], null);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Luo ensin yritysprofiili');
  });

  it('publishes integer cents and local Helsinki fields then refreshes the owner list', async () => {
    load();
    await fixture.whenStable();
    fixture.componentInstance.form.setValue({
      title: slot.title,
      description: slot.description,
      serviceTypeId: slot.serviceTypeId,
      date: '2027-07-15',
      time: '12:00',
      durationMinutes: 45,
      price: '45,50',
    });
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    http.expectOne('/api/account/csrf').flush(null);
    const request = http.expectOne('/api/business/vacancies');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.priceCents).toBe(4550);
    expect(request.request.body.time).toBe('12:00:00');
    expect(request.request.body.date).toBe('2027-07-15');
    request.flush(slot, { status: 201, statusText: 'Created' });
    load([slot]);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('.managed-slot').textContent).toContain(slot.title);
    expect(fixture.componentInstance.form.controls.title.value).toBe('');
  });

  it('edits in Helsinki time and preserves unsaved input on a concurrency conflict', async () => {
    load([slot]);
    await fixture.whenStable();
    fixture.nativeElement.querySelector('.managed-slot button').click();
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#title'));
    expect(fixture.componentInstance.form.controls.time.value).toBe('12:00');
    fixture.componentInstance.form.controls.title.setValue('My edit');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    http.expectOne('/api/account/csrf').flush(null);
    const request = http.expectOne('/api/business/vacancies/slot');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.version).toBe(slot.version);
    request.flush({}, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(fixture.componentInstance.form.controls.title.value).toBe('My edit');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Tiedot ovat muuttuneet',
    );
  });

  it('requires cancellation confirmation and retains a cancelled item in the dashboard', async () => {
    load([slot]);
    await fixture.whenStable();
    const cancelButton: HTMLButtonElement =
      fixture.nativeElement.querySelectorAll('.managed-slot button')[1];
    cancelButton.focus();
    cancelButton.click();
    await fixture.whenStable();
    http.expectNone('/api/account/csrf');
    expect(document.activeElement).toBe(cancelButton);
    const buttons = [
      ...fixture.nativeElement.querySelectorAll('.managed-slot button'),
    ] as HTMLButtonElement[];
    buttons.find((button) => button.textContent?.includes('Vahvista peruminen'))!.click();
    http.expectOne('/api/account/csrf').flush(null);
    const request = http.expectOne('/api/business/vacancies/slot/cancel');
    expect(request.request.body.version).toBe(slot.version);
    const cancelled = {
      ...slot,
      status: 'Cancelled' as const,
      cancelledAt: '2027-01-15T08:00:00Z',
    };
    request.flush(cancelled);
    load([cancelled]);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('.managed-slot').textContent).toContain('Peruttu');
    expect(fixture.nativeElement.querySelector('.managed-slot button')).toBeNull();
  });

  it('does not send invalid prices or incomplete forms', async () => {
    load();
    await fixture.whenStable();
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    http.expectNone('/api/account/csrf');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Täytä kaikki kentät',
    );
  });
});
