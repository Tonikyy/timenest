import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NotificationsPage } from './notifications';
import { Inbox, Notice, NotificationsApi } from './notifications-api';

describe('Notification inbox', () => {
  let fixture: ComponentFixture<NotificationsPage>;
  let http: HttpTestingController;
  const item: Notice = {
    id: 'b1522ac5-4adc-44ba-83d9-251906719fe0',
    subject: 'Varaus on peruttu',
    body: '<img src=x onerror=alert(1)>\nPalautus on jonossa.',
    link: '/bookings/2c531c14-43de-47f3-81f0-a503e1981a1a',
    createdAt: '2027-03-28T01:30:00Z',
    readAt: null,
  };
  const inbox: Inbox = { items: [item], total: 1, unread: 1, page: 1, pageSize: 20 };
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [NotificationsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    fixture = TestBed.createComponent(NotificationsPage);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    fixture.destroy();
  });
  const load = (value: Inbox = inbox) => http.expectOne('/api/notifications?page=1').flush(value);

  it('shows loading, safe text, Helsinki time, unread status and the owned booking link', async () => {
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Ladataan');
    load();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(root.querySelector('.notice-body img')).toBeNull();
    expect(root.textContent).toContain('4.30');
    expect(root.textContent).toContain('1 lukematonta');
    expect(root.querySelector('a[href="' + item.link + '"]')).not.toBeNull();
    expect(root.querySelector('img')?.getAttribute('src')).toBe('images/logo.svg');
  });

  it('marks read only after success and prevents duplicate clicks', async () => {
    load();
    const component = fixture.componentInstance;
    component.markRead(item);
    component.markRead(item);
    http.expectOne('/api/account/csrf').flush(null);
    const request = http.expectOne('/api/notifications/' + item.id + '/read');
    expect(request.request.method).toBe('POST');
    expect(component.result()!.unread).toBe(1);
    request.flush({ id: item.id, readAt: '2027-01-15T08:00:00Z' });
    await fixture.whenStable();
    expect(component.result()!.unread).toBe(0);
    expect(fixture.nativeElement.textContent).toContain('Luettu');
    expect(fixture.nativeElement.textContent).not.toContain('Merkitse luetuksi');
  });

  it('preserves unread state on a failed save and supports retry', () => {
    load();
    fixture.componentInstance.markRead(item);
    http.expectOne('/api/account/csrf').flush(null);
    http
      .expectOne('/api/notifications/' + item.id + '/read')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    expect(fixture.componentInstance.result()!.unread).toBe(1);
    expect(fixture.componentInstance.reading()).toBeNull();
    fixture.componentInstance.markRead(item);
    http.expectOne('/api/account/csrf').flush(null);
    http
      .expectOne('/api/notifications/' + item.id + '/read')
      .flush({ id: item.id, readAt: '2027-01-15T08:00:00Z' });
    expect(fixture.componentInstance.result()!.unread).toBe(0);
  });

  it('clears private messages when the session expires during a mutation', async () => {
    load();
    fixture.componentInstance.markRead(item);
    http.expectOne('/api/account/csrf').flush(null);
    http
      .expectOne('/api/notifications/' + item.id + '/read')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    expect(fixture.componentInstance.result()).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/account/login"]')).not.toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain(item.subject);
  });

  it('shows an honest load error and can retry to an empty inbox', async () => {
    http
      .expectOne('/api/notifications?page=1')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    fixture.componentInstance.load();
    load({ ...inbox, items: [], total: 0, unread: 0 });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Ei ilmoituksia');
  });

  it('loads another page and prevents navigation during a read mutation', () => {
    load({ ...inbox, total: 21 });
    fixture.componentInstance.load(2);
    http.expectOne('/api/notifications?page=2').flush({ ...inbox, page: 2, total: 21 });
    fixture.componentInstance.markRead(item);
    fixture.componentInstance.load(1);
    http.expectOne('/api/account/csrf').flush(null);
    http
      .expectOne('/api/notifications/' + item.id + '/read')
      .flush({ id: item.id, readAt: '2027-01-15T08:00:00Z' });
    expect(fixture.componentInstance.result()!.page).toBe(2);
  });

  it('rejects crafted IDs before requesting CSRF or sending a mutation', () => {
    load();
    const error = vi.fn();
    TestBed.inject(NotificationsApi).read('../account/logout?x=').subscribe({ error });
    expect(error).toHaveBeenCalledOnce();
    http.expectNone(() => true);
  });
});
