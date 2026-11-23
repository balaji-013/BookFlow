import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./features/auth/register.component').then(m => m.RegisterComponent) },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./core/layout/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'pos', canActivate: [roleGuard('Owner', 'StoreManager', 'BillingEmployee')], loadComponent: () => import('./features/pos/pos.component').then(m => m.PosComponent) },
      { path: 'books', loadComponent: () => import('./features/books/books.component').then(m => m.BooksComponent) },
      { path: 'customers', loadComponent: () => import('./features/customers/customers.component').then(m => m.CustomersComponent) },
      { path: 'suppliers', canActivate: [roleGuard('Owner', 'StoreManager', 'InventoryManager')], loadComponent: () => import('./features/suppliers/suppliers.component').then(m => m.SuppliersComponent) },
      { path: 'purchasing', canActivate: [roleGuard('Owner', 'StoreManager', 'InventoryManager')], loadComponent: () => import('./features/purchasing/purchasing.component').then(m => m.PurchasingComponent) },
      { path: 'inventory', loadComponent: () => import('./features/inventory/inventory.component').then(m => m.InventoryComponent) },
      { path: 'invoices', loadComponent: () => import('./features/invoices/invoices.component').then(m => m.InvoicesComponent) },
      { path: 'invoices/:id', loadComponent: () => import('./features/invoices/invoice-detail.component').then(m => m.InvoiceDetailComponent) },
      { path: 'payments', canActivate: [roleGuard('Owner', 'StoreManager', 'BillingEmployee')], loadComponent: () => import('./features/payments/payments.component').then(m => m.PaymentsComponent) },
      { path: 'returns', canActivate: [roleGuard('Owner', 'StoreManager', 'BillingEmployee')], loadComponent: () => import('./features/returns/returns.component').then(m => m.ReturnsComponent) },
      { path: 'reports', canActivate: [roleGuard('Owner', 'StoreManager')], loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent) },
      { path: 'settings', loadComponent: () => import('./features/settings/settings.component').then(m => m.SettingsComponent) },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];
