import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { Inbox, Notice, NotificationsApi } from './notifications-api';

@Component({
  selector: 'app-notifications',
  imports: [RouterLink],
  templateUrl: './notifications.html',
  styles: [
    `
      .notice-body {
        white-space: pre-wrap;
        overflow-wrap: anywhere;
      }
      .unread {
        border-inline-start: 4px solid var(--brand-ink);
      }
      .notice-heading {
        display: flex;
        gap: 1rem;
        flex-wrap: wrap;
        align-items: baseline;
      }
    `,
  ],
})
export class NotificationsPage {
  private readonly api = inject(NotificationsApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly result = signal<Inbox | null>(null);
  readonly busy = signal(false);
  readonly reading = signal<string | null>(null);
  readonly error = signal('');
  readonly signedOut = signal(false);
  readonly date = (value: string) =>
    new Intl.DateTimeFormat('fi-FI', {
      timeZone: 'Europe/Helsinki',
      dateStyle: 'short',
      timeStyle: 'short',
    }).format(new Date(value));

  constructor() {
    this.load();
  }

  load(page = 1) {
    if (this.busy() || this.reading()) return;
    this.busy.set(true);
    this.result.set(null);
    this.error.set('');
    this.signedOut.set(false);
    this.api
      .list(page)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => this.result.set(result),
        error: (error) => this.failed(error),
      });
  }

  markRead(item: Notice) {
    if (item.readAt || this.busy() || this.reading()) return;
    this.reading.set(item.id);
    this.error.set('');
    this.api
      .read(item.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.reading.set(null)),
      )
      .subscribe({
        next: (updated) =>
          this.result.update((result) =>
            result
              ? {
                  ...result,
                  unread: Math.max(0, result.unread - 1),
                  items: result.items.map((row) =>
                    row.id === item.id ? { ...row, readAt: updated.readAt } : row,
                  ),
                }
              : null,
          ),
        error: (error) => this.failed(error),
      });
  }

  private failed(error: HttpErrorResponse) {
    if (error.status === 401) {
      this.result.set(null);
      this.signedOut.set(true);
      this.error.set('Kirjaudu sisään nähdäksesi omat ilmoituksesi.');
    } else {
      this.error.set('Ilmoitusten käsittely ei onnistunut. Yritä uudelleen.');
    }
  }
}
