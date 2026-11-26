import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { BusinessPage } from './business';

describe('Business profile', () => {
  let fixture: ComponentFixture<BusinessPage>;
  let http: HttpTestingController;
  const profile = {
    id: 'business',
    name: 'Demo',
    address: 'Demo 1',
    city: 'Demo',
    latitude: 0,
    longitude: 0,
    isDemo: true,
    version: 'AQIDBAUGBwg=',
  };
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BusinessPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    fixture = TestBed.createComponent(BusinessPage);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    fixture.destroy();
  });

  it('shows sign-in instead of an editable profile for anonymous visitors', async () => {
    http.expectOne('/api/business').flush({}, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/account/login"]')).not.toBeNull();
  });

  it('creates the first profile through the form with a CSRF-protected request', async () => {
    http.expectOne('/api/business').flush(null);
    await fixture.whenStable();
    fixture.componentInstance.form.setValue({
      name: 'Demo',
      address: 'Demo 1',
      city: 'Demo',
      latitude: 0,
      longitude: 0,
    });
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    http.expectOne('/api/account/csrf').flush(null);
    const request = http.expectOne('/api/business');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.ownerId).toBeUndefined();
    request.flush(profile);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Yritysprofiili on tallennettu');
  });

  it('sends the loaded version and preserves edits when the server reports a conflict', async () => {
    http.expectOne('/api/business').flush(profile);
    await fixture.whenStable();
    fixture.componentInstance.form.controls.name.setValue('My edit');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    http.expectOne('/api/account/csrf').flush(null);
    const request = http.expectOne('/api/business');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.version).toBe(profile.version);
    request.flush({}, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(fixture.componentInstance.form.controls.name.value).toBe('My edit');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Lataa profiili uudelleen',
    );
  });
});
