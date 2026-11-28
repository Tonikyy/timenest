import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { switchMap, throwError } from 'rxjs';
import { Session } from '../../core/session';

export interface Notice {
  id: string;
  subject: string;
  body: string;
  link: string;
  createdAt: string;
  readAt: string | null;
}
export interface Inbox {
  items: Notice[];
  total: number;
  unread: number;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);
  private readonly session = inject(Session);

  list(page = 1) {
    return this.http.get<Inbox>('/api/notifications', { params: { page } });
  }

  read(id: string) {
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id))
      return throwError(() => new HttpErrorResponse({ status: 404 }));
    return this.session
      .csrf()
      .pipe(
        switchMap(() =>
          this.http.post<{ id: string; readAt: string }>('/api/notifications/' + id + '/read', {}),
        ),
      );
  }
}
