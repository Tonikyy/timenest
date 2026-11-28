import { NotificationsPage } from './features/notifications/notifications';
import { BookingsPage } from './features/bookings/bookings';
import { Routes } from '@angular/router';
import { PaymentsPage } from './features/business/payments';
import { PublishingPage } from './features/business/publishing';
import { BusinessPage } from './features/business/business';
import { Discovery } from './features/search/discovery';
import { AccountPage } from './features/account/account';

export const routes: Routes = [
  { path: '', component: Discovery },
  { path: 'notifications', component: NotificationsPage },
  { path: 'bookings', component: BookingsPage },
  { path: 'bookings/:id', component: BookingsPage },
  { path: 'account', component: AccountPage, data: { mode: 'home' } },
  { path: 'account/login', component: AccountPage, data: { mode: 'login' } },
  { path: 'account/register', component: AccountPage, data: { mode: 'register' } },
  { path: 'account/forgot', component: AccountPage, data: { mode: 'forgot-password' } },
  { path: 'account/reset', component: AccountPage, data: { mode: 'reset-password' } },
  { path: 'account/confirm', component: AccountPage, data: { mode: 'confirm-email' } },
  { path: 'account/resend', component: AccountPage, data: { mode: 'resend-confirmation' } },
  { path: 'business', component: BusinessPage },
  { path: 'business/bookings', component: BookingsPage, data: { business: true } },
  { path: 'business/bookings/:id', component: BookingsPage, data: { business: true } },
  { path: 'business/payments', component: PaymentsPage },
  { path: 'business/vacancies', component: PublishingPage },
  { path: '**', redirectTo: '' },
];
