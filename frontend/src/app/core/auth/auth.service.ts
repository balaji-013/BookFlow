import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResponse, UserRole } from '../models';

const REFRESH_KEY = 'bookflow.refresh';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly base = `${environment.apiUrl}/auth`;

  private readonly _session = signal<AuthResponse | null>(null);
  readonly session = this._session.asReadonly();
  readonly user = computed(() => this._session()?.user ?? null);
  readonly tenant = computed(() => this._session()?.tenant ?? null);
  readonly isLoggedIn = computed(() => !!this._session());
  readonly role = computed<UserRole | null>(() => this._session()?.user.role ?? null);

  get accessToken(): string | null { return this._session()?.accessToken ?? null; }
  get refreshToken(): string | null { try { return localStorage.getItem(REFRESH_KEY); } catch { return null; } }

  hasRole(...roles: UserRole[]): boolean {
    const r = this.role();
    return !!r && (r === 'SuperAdmin' || roles.includes(r));
  }
  get isOwner() { return this.hasRole('Owner'); }
  get isManager() { return this.hasRole('Owner', 'StoreManager'); }
  get canBill() { return this.hasRole('Owner', 'StoreManager', 'BillingEmployee'); }
  get canManageInventory() { return this.hasRole('Owner', 'StoreManager', 'InventoryManager'); }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.base}/login`, { email, password }).pipe(tap(r => this.store(r)));
  }

  registerTenant(body: { businessName: string; ownerName: string; email: string; password: string; phone?: string; gstin?: string; city?: string; stateCode?: string }) {
    return this.http.post<AuthResponse>(`${this.base}/register-tenant`, body).pipe(tap(r => this.store(r)));
  }

  /** Called by the interceptor and at app start. */
  refresh(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.base}/refresh`, { refreshToken: this.refreshToken }).pipe(tap(r => this.store(r)));
  }

  /** Restores a session from the stored refresh token; resolves false when there is none. */
  async restore(): Promise<boolean> {
    if (!this.refreshToken) return false;
    try { await new Promise<void>((res, rej) => this.refresh().subscribe({ next: () => res(), error: rej })); return true; }
    catch { this.clear(); return false; }
  }

  changePassword(currentPassword: string, newPassword: string) {
    return this.http.post<void>(`${this.base}/change-password`, { currentPassword, newPassword });
  }

  logout(): void {
    const token = this.refreshToken;
    if (token) this.http.post(`${this.base}/logout`, { refreshToken: token }).subscribe({ error: () => { } });
    this.clear();
    this.router.navigateByUrl('/login');
  }

  private store(r: AuthResponse) {
    this._session.set(r);
    try { if (r.refreshToken) localStorage.setItem(REFRESH_KEY, r.refreshToken); } catch { /* private mode */ }
  }

  clear() {
    this._session.set(null);
    try { localStorage.removeItem(REFRESH_KEY); } catch { /* ignore */ }
  }
}
