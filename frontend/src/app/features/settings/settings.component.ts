import { Component, Inject, inject, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Observable } from 'rxjs';
import { MATERIAL, PageComponent } from '../../shared/shared';
import { AdminApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { AuthService } from '../../core/auth/auth.service';
import { AuditLog, PagedResult, Tenant, User } from '../../core/models';

@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Edit employee' : 'Add employee' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" id="userForm" (ngSubmit)="save()" class="form-grid">
        <mat-form-field class="span2"><mat-label>Full name</mat-label><input matInput formControlName="fullName" required /></mat-form-field>
        @if (!data) { <mat-form-field class="span2"><mat-label>Email (login)</mat-label><input matInput formControlName="email" type="email" required /></mat-form-field>
        <mat-form-field class="span2"><mat-label>Temporary password</mat-label><input matInput formControlName="password" type="text" required /></mat-form-field> }
        <mat-form-field><mat-label>Role</mat-label><mat-select formControlName="role"><mat-option value="StoreManager">Store manager</mat-option><mat-option value="BillingEmployee">Billing employee</mat-option><mat-option value="InventoryManager">Inventory manager</mat-option><mat-option value="Owner">Owner</mat-option></mat-select></mat-form-field>
        <mat-form-field><mat-label>Phone</mat-label><input matInput formControlName="phone" /></mat-form-field>
      </form>
      <p class="muted">Billing employee: POS, invoices, payments, returns. Inventory manager: books, stock, purchasing, suppliers. Store manager: everything except employee management.</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button color="primary" form="userForm" type="submit" [disabled]="form.invalid || busy()">Save</button></mat-dialog-actions>`,
  styles: ['.span2 { grid-column: span 2; } .form-grid { grid-template-columns: 1fr 1fr; padding-top: 4px; }'],
})
export class UserDialogComponent {
  readonly data = inject<User | null>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<UserDialogComponent>>(MatDialogRef);
  private fb = inject(FormBuilder); private api = inject(AdminApi); private notify = inject(NotifyService);
  busy = signal(false);
  form = this.fb.nonNullable.group({ fullName: [this.data?.fullName ?? '', Validators.required], email: [this.data?.email ?? '', this.data ? [] : [Validators.required, Validators.email]],
    password: ['', this.data ? [] : [Validators.required, Validators.minLength(8)]], role: [this.data?.role ?? 'BillingEmployee'], phone: [this.data?.phone ?? ''] });
    save() {
    this.busy.set(true); const v = this.form.getRawValue();
    const req: Observable<unknown> = this.data ? this.api.updateUser(this.data.id, { fullName: v.fullName, role: v.role, phone: v.phone || null }) : this.api.createUser({ ...v, phone: v.phone || null });
    req.subscribe({ next: () => { this.notify.success('Saved'); this.ref.close(true); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
}

@Component({
  standalone: true,
  imports: [...MATERIAL, PageComponent],
  template: `
    <bf-page title="Settings" [subtitle]="auth.tenant()?.businessName"></bf-page>
    <mat-tab-group>
      @if (auth.isOwner) {
        <mat-tab label="Business profile">
          <form [formGroup]="tenantForm" (ngSubmit)="saveTenant()" class="card" style="margin-top:12px">
            <div class="form-grid">
              <mat-form-field><mat-label>Bookstore name</mat-label><input matInput formControlName="businessName" required /></mat-form-field>
              <mat-form-field><mat-label>Legal name</mat-label><input matInput formControlName="legalName" /></mat-form-field>
              <mat-form-field><mat-label>GSTIN</mat-label><input matInput formControlName="gstin" style="text-transform:uppercase" /></mat-form-field>
              <mat-form-field><mat-label>GST state code</mat-label><input matInput formControlName="stateCode" maxlength="2" required /></mat-form-field>
              <mat-form-field><mat-label>Phone</mat-label><input matInput formControlName="phone" /></mat-form-field>
              <mat-form-field><mat-label>Invoice prefix</mat-label><input matInput formControlName="invoicePrefix" maxlength="6" style="text-transform:uppercase" required /><mat-hint>e.g. INV → INV/2026-27/000001</mat-hint></mat-form-field>
              <mat-form-field><mat-label>Address line 1</mat-label><input matInput formControlName="addressLine1" /></mat-form-field>
              <mat-form-field><mat-label>Address line 2</mat-label><input matInput formControlName="addressLine2" /></mat-form-field>
              <mat-form-field><mat-label>City</mat-label><input matInput formControlName="city" /></mat-form-field>
              <mat-form-field><mat-label>State</mat-label><input matInput formControlName="state" /></mat-form-field>
              <mat-form-field><mat-label>Pincode</mat-label><input matInput formControlName="pincode" maxlength="6" /></mat-form-field>
            </div>
            <mat-form-field class="w-full"><mat-label>Invoice footer (terms, return policy, thank-you note)</mat-label><textarea matInput formControlName="invoiceFooter" rows="3"></textarea></mat-form-field>
            <button mat-flat-button color="primary" type="submit" [disabled]="tenantForm.invalid || busy()">Save profile</button>
          </form>
        </mat-tab>
        <mat-tab label="Employees">
          <div class="toolbar-row" style="margin-top:12px"><span class="muted">Each employee signs in with their own email.</span><span style="flex:1"></span><button mat-flat-button color="primary" (click)="editUser(null)"><mat-icon>person_add</mat-icon> Add employee</button></div>
          <div class="table-card">
            <table mat-table [dataSource]="users()?.items ?? []">
              <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>Name</th><td mat-cell *matCellDef="let u">{{ u.fullName }}<div class="muted">{{ u.email }}</div></td></ng-container>
              <ng-container matColumnDef="role"><th mat-header-cell *matHeaderCellDef>Role</th><td mat-cell *matCellDef="let u"><span class="chip info">{{ u.role }}</span></td></ng-container>
              <ng-container matColumnDef="last"><th mat-header-cell *matHeaderCellDef>Last login</th><td mat-cell *matCellDef="let u">{{ u.lastLoginAt ? (u.lastLoginAt | date:'d MMM yyyy, HH:mm') : 'never' }}</td></ng-container>
              <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let u"><span [class]="'chip ' + (u.isActive ? 'ok' : 'bad')">{{ u.isActive ? 'Active' : 'Inactive' }}</span></td></ng-container>
              <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let u" class="num">
                <button mat-icon-button matTooltip="Edit" (click)="editUser(u)"><mat-icon>edit</mat-icon></button>
                <button mat-icon-button matTooltip="Reset password" (click)="resetPassword(u)"><mat-icon>lock_reset</mat-icon></button>
                @if (u.id !== auth.user()?.id) { <button mat-icon-button [matTooltip]="u.isActive ? 'Deactivate' : 'Activate'" (click)="toggle(u)"><mat-icon>{{ u.isActive ? 'person_off' : 'person' }}</mat-icon></button> }</td></ng-container>
              <tr mat-header-row *matHeaderRowDef="userCols"></tr><tr mat-row *matRowDef="let row; columns: userCols"></tr>
            </table>
          </div>
        </mat-tab>
        <mat-tab label="Audit log">
          <div class="toolbar-row" style="margin-top:12px"><mat-form-field><mat-label>Entity</mat-label><mat-select [value]="auditEntity" (valueChange)="auditEntity = $event; loadAudit()"><mat-option [value]="null">All</mat-option>@for (e of ['SalesInvoice','Payment','Book','Customer','Supplier','User','GoodsReceipt','StockAdjustment','SalesReturn','Tenant']; track e) { <mat-option [value]="e">{{ e }}</mat-option> }</mat-select></mat-form-field></div>
          <div class="table-card">
            <table mat-table [dataSource]="audit()?.items ?? []">
              <ng-container matColumnDef="when"><th mat-header-cell *matHeaderCellDef>When</th><td mat-cell *matCellDef="let a">{{ a.occurredAt | date:'d MMM yyyy, HH:mm:ss' }}</td></ng-container>
              <ng-container matColumnDef="who"><th mat-header-cell *matHeaderCellDef>Who</th><td mat-cell *matCellDef="let a">{{ a.userName || 'system' }}</td></ng-container>
              <ng-container matColumnDef="what"><th mat-header-cell *matHeaderCellDef>What</th><td mat-cell *matCellDef="let a"><span class="chip">{{ a.action }}</span> {{ a.entityName }} <span class="muted mono">{{ a.entityId | slice:0:8 }}</span></td></ng-container>
              <ng-container matColumnDef="changes"><th mat-header-cell *matHeaderCellDef>Changes</th><td mat-cell *matCellDef="let a"><code class="changes">{{ a.changes | slice:0:300 }}</code></td></ng-container>
              <tr mat-header-row *matHeaderRowDef="auditCols"></tr><tr mat-row *matRowDef="let row; columns: auditCols"></tr>
            </table>
            <mat-paginator [length]="audit()?.totalCount ?? 0" [pageSize]="50" (page)="auditPage = $event.pageIndex; loadAudit()"></mat-paginator>
          </div>
        </mat-tab>
      }
      <mat-tab label="My account">
        <div class="card" style="margin-top:12px;max-width:480px">
          <p><strong>{{ auth.user()?.fullName }}</strong><br /><span class="muted">{{ auth.user()?.email }} · {{ auth.user()?.role }}</span></p>
          <h3>Change password</h3>
          <mat-form-field class="w-full"><mat-label>Current password</mat-label><input matInput type="password" [(ngModel)]="pw.current" /></mat-form-field>
          <mat-form-field class="w-full"><mat-label>New password (min 8)</mat-label><input matInput type="password" [(ngModel)]="pw.next" /></mat-form-field>
          <button mat-flat-button color="primary" (click)="changePassword()" [disabled]="!pw.current || pw.next.length < 8">Update password</button>
        </div>
      </mat-tab>
    </mat-tab-group>`,
  styles: ['.changes { font-size: 11px; white-space: pre-wrap; word-break: break-all; color: #5f6368; } h3 { font-weight: 500; font-size: 16px; }'],
})
export class SettingsComponent {
  readonly auth = inject(AuthService);
  private fb = inject(FormBuilder); private api = inject(AdminApi); private notify = inject(NotifyService); private dialog = inject(MatDialog);
  userCols = ['name', 'role', 'last', 'status', 'actions']; auditCols = ['when', 'who', 'what', 'changes'];
  users = signal<PagedResult<User> | null>(null); audit = signal<PagedResult<AuditLog> | null>(null); busy = signal(false);
  auditEntity: string | null = null; auditPage = 0; pw = { current: '', next: '' };
  tenantForm = this.fb.nonNullable.group({ businessName: ['', Validators.required], legalName: [''], gstin: [''], stateCode: ['33', Validators.required], phone: [''], invoicePrefix: ['INV', Validators.required],
    addressLine1: [''], addressLine2: [''], city: [''], state: ['Tamil Nadu'], pincode: [''], invoiceFooter: [''] });
  constructor() {
    if (this.auth.isOwner) {
      this.api.tenant().subscribe(t => this.tenantForm.patchValue({ ...t, legalName: t.legalName ?? '', gstin: t.gstin ?? '', phone: t.phone ?? '', addressLine1: t.addressLine1 ?? '', addressLine2: t.addressLine2 ?? '', city: t.city ?? '', pincode: t.pincode ?? '', invoiceFooter: t.invoiceFooter ?? '' }));
      this.loadUsers(); this.loadAudit();
    }
  }
  saveTenant() {
    this.busy.set(true); const v = this.tenantForm.getRawValue();
    this.api.updateTenant({ ...v, gstin: v.gstin ? v.gstin.toUpperCase() : undefined, invoicePrefix: v.invoicePrefix.toUpperCase() } as Partial<Tenant>)
      .subscribe({ next: () => { this.busy.set(false); this.notify.success('Profile saved'); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
  loadUsers() { this.api.users({ pageSize: 100 }).subscribe(u => this.users.set(u)); }
  loadAudit() { this.api.audit({ entity: this.auditEntity, page: this.auditPage + 1, pageSize: 50 }).subscribe(a => this.audit.set(a)); }
  editUser(u: User | null) { this.dialog.open(UserDialogComponent, { width: '520px', data: u }).afterClosed().subscribe(ok => { if (ok) this.loadUsers(); }); }
  toggle(u: User) { this.api.setActive(u.id, !u.isActive).subscribe({ next: () => this.loadUsers(), error: e => this.notify.error(e) }); }
  resetPassword(u: User) { const p = prompt(`New password for ${u.fullName} (min 8 characters)`); if (p && p.length >= 8) this.api.resetPassword(u.id, p).subscribe({ next: () => this.notify.success('Password reset'), error: e => this.notify.error(e) }); }
  changePassword() { this.auth.changePassword(this.pw.current, this.pw.next).subscribe({ next: () => { this.notify.success('Password changed — please sign in again.'); this.auth.logout(); }, error: e => this.notify.error(e) }); }
}
