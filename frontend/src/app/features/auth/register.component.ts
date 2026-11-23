import { Component, inject, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MATERIAL } from '../../shared/shared';
import { AuthService } from '../../core/auth/auth.service';
import { NotifyService } from '../../core/services/notify.service';

@Component({
  standalone: true,
  imports: [...MATERIAL, RouterLink],
  template: `
    <div class="auth-wrap">
      <mat-card class="auth-card">
        <div class="brand"><mat-icon>auto_stories</mat-icon> BookFlow</div>
        <h2>Register your bookstore</h2>
        <p class="muted">Billing, stock, purchases and reports in one place. Free during the pilot.</p>
        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field class="w-full"><mat-label>Bookstore name</mat-label><input matInput formControlName="businessName" /></mat-form-field>
          <mat-form-field class="w-full"><mat-label>Owner name</mat-label><input matInput formControlName="ownerName" /></mat-form-field>
          <mat-form-field class="w-full"><mat-label>Email</mat-label><input matInput formControlName="email" type="email" autocomplete="username" /></mat-form-field>
          <mat-form-field class="w-full"><mat-label>Password (min 8 characters)</mat-label><input matInput formControlName="password" type="password" autocomplete="new-password" /></mat-form-field>
          <div class="form-grid">
            <mat-form-field><mat-label>Phone</mat-label><input matInput formControlName="phone" /></mat-form-field>
            <mat-form-field><mat-label>City</mat-label><input matInput formControlName="city" /></mat-form-field>
          </div>
          <mat-form-field class="w-full"><mat-label>GSTIN (optional)</mat-label><input matInput formControlName="gstin" style="text-transform:uppercase" /><mat-hint>Leave blank if not registered under GST</mat-hint></mat-form-field>
          @if (error()) { <p class="err">{{ error() }}</p> }
          <button mat-flat-button color="primary" class="w-full" type="submit" [disabled]="form.invalid || busy()">{{ busy() ? 'Creating…' : 'Create account' }}</button>
        </form>
        <p class="muted center">Already registered? <a routerLink="/login">Sign in</a></p>
      </mat-card>
    </div>`,
  styles: [`
    .auth-wrap { min-height: 100vh; display: grid; place-items: center; background: linear-gradient(160deg, #1f4e94 0%, #0f2a52 100%); padding: 16px; }
    .auth-card { width: 100%; max-width: 480px; padding: 28px; }
    .brand { display: flex; align-items: center; gap: 8px; font-size: 22px; font-weight: 600; color: #1f4e94; margin-bottom: 8px; }
    h2 { font-weight: 500; margin: 0 0 4px; }
    .err { color: #a6192e; margin: 0 0 8px; }
    .center { text-align: center; margin-top: 16px; }
    mat-form-field { margin-bottom: 12px; }
  `],
})
export class RegisterComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);
  private notify = inject(NotifyService);
  busy = signal(false); error = signal('');
  form = this.fb.nonNullable.group({
    businessName: ['', [Validators.required, Validators.maxLength(150)]],
    ownerName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    phone: [''], city: [''], gstin: [''],
  });

  submit() {
    if (this.form.invalid) return;
    this.busy.set(true); this.error.set('');
    const v = this.form.getRawValue();
    this.auth.registerTenant({ ...v, gstin: v.gstin ? v.gstin.toUpperCase() : undefined, phone: v.phone || undefined, city: v.city || undefined, stateCode: '33' }).subscribe({
      next: () => { this.notify.success('Welcome to BookFlow! Add your first books to get started.'); this.router.navigateByUrl('/books'); },
      error: e => { this.busy.set(false); this.error.set(this.notify.describe(e)); },
    });
  }
}
