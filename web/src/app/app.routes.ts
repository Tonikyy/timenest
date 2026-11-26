import { Routes } from '@angular/router';
import { Discovery } from './features/search/discovery';

export const routes: Routes = [
  { path: '', component: Discovery },
  { path: '**', redirectTo: '' },
];
