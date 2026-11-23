import { Component, inject, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
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
        <h2>Sign in</h2>
        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field class="w-full"><mat-label>Email</mat-label><input matInput formControlName="email" type="email" autocomplete="username" autofocus /></mat-form-field>
          <mat-form-field class="w-full"><mat-label>Password</mat-label><input matInput formControlName="password" type="password" autocomplete="current-password" /></mat-form-field>
          @if (error()) { <p class="err">{{ error() }}</p> }
          <button mat-flat-button color="primary" class="w-full" type="submit" [disabled]="form.invalid || busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
        </form>
        <p class="muted center">New bookstore? <a routerLink="/register">Register your business</a></p>
      </mat-card>
    </div>`,
  styles: [`
    .auth-wrap { min-height: 100vh; display: grid; place-items: center; background: linear-gradient(160deg, #1f4e94 0%, #0f2a52 100%); padding: 16px; }
    .auth-card { width: 100%; max-width: 400px; padding: 28px; }
    .brand { display: flex; align-items: center; gap: 8px; font-size: 22px; font-weight: 600; color: #1f4e94; margin-bottom: 8px; }
    h2 { font-weight: 500; margin: 0 0 16px; }
    .err { color: #a6192e; margin: 0 0 8px; }
    .center { text-align: center; margin-top: 16px; }
    mat-form-field { margin-bottom: 12px; }
  `],
})
export class LoginComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private notify = inject(NotifyService);
  busy = signal(false); error = signal('');
  form = this.fb.nonNullable.group({ email: ['', [Validators.required, Validators.email]], password: ['', Validators.required] });

  submit() {
    if (this.form.invalid) return;
    this.busy.set(true); this.error.set('');
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => this.router.navigateByUrl(this.route.snapshot.queryParamMap.get('returnUrl') || '/dashboard'),
      error: e => { this.busy.set(false); this.error.set(this.notify.describe(e, 'Invalid email or password')); },
    });
  }
}
