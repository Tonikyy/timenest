import { Routes } from '@angular/router';
import { Discovery } from './features/search/discovery';
import { AccountPage } from './features/account/account';

export const routes: Routes = [
  { path: '', component: Discovery },
  { path: 'account', component: AccountPage, data: { mode: 'home' } },
  { path: 'account/login', component: AccountPage, data: { mode: 'login' } },
  { path: 'account/register', component: AccountPage, data: { mode: 'register' } },
  { path: 'account/forgot', component: AccountPage, data: { mode: 'forgot-password' } },
  { path: 'account/reset', component: AccountPage, data: { mode: 'reset-password' } },
  { path: 'account/confirm', component: AccountPage, data: { mode: 'confirm-email' } },
  { path: 'account/resend', component: AccountPage, data: { mode: 'resend-confirmation' } },
  { path: '**', redirectTo: '' },
];
