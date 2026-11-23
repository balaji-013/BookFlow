import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from '../auth/auth.service';
import { UserRole } from '../models';

interface NavItem { label: string; icon: string; path: string; roles?: UserRole[]; }

@Component({
  selector: 'bf-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, MatSidenavModule, MatToolbarModule, MatListModule, MatIconModule, MatButtonModule, MatMenuModule, MatTooltipModule],
  template: `
    @if (!online()) { <div class="offline-banner">You are offline — billing is paused until the connection returns.</div> }
    <mat-sidenav-container class="shell">
      <mat-sidenav [mode]="compact() ? 'over' : 'side'" [opened]="!compact() || menuOpen()" (closed)="menuOpen.set(false)" class="nav">
        <div class="brand"><mat-icon>auto_stories</mat-icon><span>BookFlow</span></div>
        <div class="tenant">{{ auth.tenant()?.businessName }}</div>
        <mat-nav-list>
          @for (item of visibleItems(); track item.path) {
            <a mat-list-item [routerLink]="item.path" routerLinkActive="active" (click)="menuOpen.set(false)">
              <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
              <span matListItemTitle>{{ item.label }}</span>
            </a>
          }
        </mat-nav-list>
      </mat-sidenav>
      <mat-sidenav-content>
        <mat-toolbar class="topbar">
          @if (compact()) { <button mat-icon-button (click)="menuOpen.set(!menuOpen())" aria-label="Menu"><mat-icon>menu</mat-icon></button> }
          <span class="spacer"></span>
          @if (auth.canBill) { <a mat-flat-button color="primary" routerLink="/pos" class="pos-btn"><mat-icon>point_of_sale</mat-icon> New sale</a> }
          <button mat-button [matMenuTriggerFor]="userMenu">
            <mat-icon>account_circle</mat-icon> {{ auth.user()?.fullName }}
            <span class="role">{{ auth.user()?.role }}</span>
          </button>
          <mat-menu #userMenu="matMenu">
            <a mat-menu-item routerLink="/settings"><mat-icon>settings</mat-icon>Settings</a>
            <button mat-menu-item (click)="auth.logout()"><mat-icon>logout</mat-icon>Sign out</button>
          </mat-menu>
        </mat-toolbar>
        <main class="content"><router-outlet /></main>
      </mat-sidenav-content>
    </mat-sidenav-container>`,
  styles: [`
    .shell { height: 100vh; }
    .nav { width: 240px; border-right: 1px solid #e3e5e8; background: #fff; }
    .brand { display: flex; align-items: center; gap: 8px; padding: 18px 16px 4px; font-size: 20px; font-weight: 600; color: #1f4e94; }
    .tenant { padding: 0 16px 12px; font-size: 12px; color: #5f6368; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .active { background: #e3ecfb; color: #1f4e94; border-radius: 0 24px 24px 0; }
    .topbar { background: #fff; border-bottom: 1px solid #e3e5e8; position: sticky; top: 0; z-index: 5; gap: 8px; }
    .spacer { flex: 1; }
    .role { font-size: 11px; color: #5f6368; margin-left: 6px; }
    .pos-btn mat-icon { margin-right: 4px; }
    .content { padding: 20px 24px 40px; max-width: 1400px; margin: 0 auto; }
    @media (max-width: 700px) { .content { padding: 12px; } .role { display: none; } }
  `],
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  readonly menuOpen = signal(false);
  readonly compact = signal(window.innerWidth < 960);
  readonly online = signal(navigator.onLine);

  private readonly items: NavItem[] = [
    { label: 'Dashboard', icon: 'dashboard', path: '/dashboard' },
    { label: 'Point of sale', icon: 'point_of_sale', path: '/pos', roles: ['Owner', 'StoreManager', 'BillingEmployee'] },
    { label: 'Invoices', icon: 'receipt_long', path: '/invoices' },
    { label: 'Payments', icon: 'payments', path: '/payments', roles: ['Owner', 'StoreManager', 'BillingEmployee'] },
    { label: 'Returns', icon: 'assignment_return', path: '/returns', roles: ['Owner', 'StoreManager', 'BillingEmployee'] },
    { label: 'Books', icon: 'menu_book', path: '/books' },
    { label: 'Inventory', icon: 'inventory_2', path: '/inventory' },
    { label: 'Purchasing', icon: 'local_shipping', path: '/purchasing', roles: ['Owner', 'StoreManager', 'InventoryManager'] },
    { label: 'Customers', icon: 'people', path: '/customers' },
    { label: 'Suppliers', icon: 'storefront', path: '/suppliers', roles: ['Owner', 'StoreManager', 'InventoryManager'] },
    { label: 'Reports', icon: 'bar_chart', path: '/reports', roles: ['Owner', 'StoreManager'] },
    { label: 'Settings', icon: 'settings', path: '/settings' },
  ];
  readonly visibleItems = computed(() => this.items.filter(i => !i.roles || this.auth.hasRole(...i.roles)));

  @HostListener('window:resize') onResize() { this.compact.set(window.innerWidth < 960); }
  @HostListener('window:online') onOnline() { this.online.set(true); }
  @HostListener('window:offline') onOffline() { this.online.set(false); }
}
