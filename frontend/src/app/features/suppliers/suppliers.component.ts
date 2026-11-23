import { Component, Inject, inject, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { PageEvent } from '@angular/material/paginator';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { MATERIAL, InrPipe, PageComponent } from '../../shared/shared';
import { PartiesApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { AuthService } from '../../core/auth/auth.service';
import { PagedResult, Supplier } from '../../core/models';
import { StatementDialogComponent } from './statement-dialog.component';

@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Edit supplier' : 'New supplier' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid" id="supForm" (ngSubmit)="save()">
        <mat-form-field class="span2"><mat-label>Name</mat-label><input matInput formControlName="name" required /></mat-form-field>
        <mat-form-field><mat-label>Contact person</mat-label><input matInput formControlName="contactPerson" /></mat-form-field>
        <mat-form-field><mat-label>Phone</mat-label><input matInput formControlName="phone" /></mat-form-field>
        <mat-form-field><mat-label>Email</mat-label><input matInput formControlName="email" /></mat-form-field>
        <mat-form-field><mat-label>GSTIN</mat-label><input matInput formControlName="gstin" style="text-transform:uppercase" /></mat-form-field>
        <mat-form-field class="span2"><mat-label>Address</mat-label><textarea matInput formControlName="address" rows="2"></textarea></mat-form-field>
        @if (data) { <mat-checkbox formControlName="isActive">Active</mat-checkbox> }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button color="primary" form="supForm" type="submit" [disabled]="form.invalid || busy()">Save</button></mat-dialog-actions>`,
  styles: ['.span2 { grid-column: span 2; } .form-grid { grid-template-columns: 1fr 1fr; padding-top: 4px; }'],
})
export class SupplierDialogComponent {
  readonly data = inject<Supplier | null>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<SupplierDialogComponent>>(MatDialogRef);
  private fb = inject(FormBuilder); private api = inject(PartiesApi); private notify = inject(NotifyService);
  busy = signal(false);
  form = this.fb.nonNullable.group({
    name: [this.data?.name ?? '', Validators.required], contactPerson: [this.data?.contactPerson ?? ''], phone: [this.data?.phone ?? ''], email: [this.data?.email ?? ''],
    gstin: [this.data?.gstin ?? ''], address: [this.data?.address ?? ''], isActive: [this.data?.isActive ?? true],
  });
    save() {
    this.busy.set(true);
    const v = this.form.getRawValue();
    const body = { ...v, gstin: v.gstin ? v.gstin.toUpperCase() : null, phone: v.phone || null, email: v.email || null, contactPerson: v.contactPerson || null, address: v.address || null, rowVersion: this.data?.rowVersion ?? null };
    this.api.saveSupplier(this.data?.id ?? null, body as unknown as Partial<Supplier>).subscribe({ next: s => { this.notify.success('Supplier saved'); this.ref.close(s); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
}

@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>Pay {{ data.name }}</h2>
    <mat-dialog-content>
      <p class="muted">Outstanding: {{ data.outstanding | number:'1.2-2' }}</p>
      <form [formGroup]="form" class="form-grid" id="payForm" (ngSubmit)="save()">
        <mat-form-field><mat-label>Amount</mat-label><input matInput type="number" min="0.01" step="0.01" formControlName="amount" required /></mat-form-field>
        <mat-form-field><mat-label>Method</mat-label><mat-select formControlName="method"><mat-option value="Cash">Cash</mat-option><mat-option value="Upi">UPI</mat-option><mat-option value="BankTransfer">Bank transfer</mat-option><mat-option value="Card">Card</mat-option></mat-select></mat-form-field>
        <mat-form-field class="span2"><mat-label>Reference (UTR / cheque no.)</mat-label><input matInput formControlName="reference" /></mat-form-field>
        <mat-form-field class="span2"><mat-label>Notes</mat-label><input matInput formControlName="notes" /></mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button color="primary" form="payForm" type="submit" [disabled]="form.invalid || busy()">Record payment</button></mat-dialog-actions>`,
  styles: ['.span2 { grid-column: span 2; } .form-grid { grid-template-columns: 1fr 1fr; padding-top: 4px; }'],
})
export class SupplierPaymentDialogComponent {
  readonly data = inject<Supplier>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<SupplierPaymentDialogComponent>>(MatDialogRef);
  private fb = inject(FormBuilder); private api = inject(PartiesApi); private notify = inject(NotifyService);
  busy = signal(false);
  form = this.fb.nonNullable.group({ amount: [this.data.outstanding > 0 ? this.data.outstanding : 0, [Validators.required, Validators.min(0.01)]], method: ['BankTransfer'], reference: [''], notes: [''] });
    save() {
    this.busy.set(true);
    const v = this.form.getRawValue();
    this.api.paySupplier(this.data.id, { amount: v.amount, method: v.method, reference: v.reference || null, notes: v.notes || null })
      .subscribe({ next: () => { this.notify.success('Payment recorded'); this.ref.close(true); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
}

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent],
  template: `
    <bf-page title="Suppliers" subtitle="Publishers and distributors you buy from">
      <div actions><button mat-flat-button color="primary" (click)="edit(null)"><mat-icon>add_business</mat-icon> New supplier</button></div>
    </bf-page>
    <div class="toolbar-row">
      <mat-form-field><mat-label>Search</mat-label><input matInput (input)="search$.next($any($event.target).value)" /></mat-form-field>
      <mat-checkbox [checked]="includeInactive()" (change)="includeInactive.set($event.checked); load()">Show inactive</mat-checkbox>
    </div>
    @if (loading()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }
    <div class="table-card">
      <table mat-table [dataSource]="page()?.items ?? []">
        <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>Name</th><td mat-cell *matCellDef="let s"><div>{{ s.name }}</div><div class="muted">{{ s.contactPerson }}{{ s.gstin ? ' · ' + s.gstin : '' }}</div></td></ng-container>
        <ng-container matColumnDef="phone"><th mat-header-cell *matHeaderCellDef>Phone</th><td mat-cell *matCellDef="let s">{{ s.phone }}</td></ng-container>
        <ng-container matColumnDef="due"><th mat-header-cell *matHeaderCellDef class="num">We owe</th><td mat-cell *matCellDef="let s" class="num"><span [class]="'chip ' + (s.outstanding > 0 ? 'warn' : 'ok')">{{ s.outstanding | inr }}</span></td></ng-container>
        <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let s" class="num">
          <button mat-icon-button matTooltip="Statement" (click)="statement(s)"><mat-icon>receipt_long</mat-icon></button>
          @if (auth.isManager) { <button mat-icon-button matTooltip="Record payment" (click)="pay(s)"><mat-icon>payments</mat-icon></button> }
          <button mat-icon-button matTooltip="Edit" (click)="edit(s)"><mat-icon>edit</mat-icon></button></td></ng-container>
        <tr mat-header-row *matHeaderRowDef="cols"></tr>
        <tr mat-row *matRowDef="let row; columns: cols"></tr>
      </table>
      @if (page() && !page()!.items.length) { <div class="empty">No suppliers yet.</div> }
      <mat-paginator [length]="page()?.totalCount ?? 0" [pageSize]="pageSize" [pageIndex]="pageIndex" [pageSizeOptions]="[25, 50, 100]" (page)="onPage($event)"></mat-paginator>
    </div>`,
})
export class SuppliersComponent {
  readonly auth = inject(AuthService);
  private api = inject(PartiesApi); private notify = inject(NotifyService); private dialog = inject(MatDialog);
  cols = ['name', 'phone', 'due', 'actions'];
  page = signal<PagedResult<Supplier> | null>(null); loading = signal(false); includeInactive = signal(false);
  search = ''; pageIndex = 0; pageSize = 25; search$ = new Subject<string>();
  constructor() { this.search$.pipe(debounceTime(300), distinctUntilChanged()).subscribe(s => { this.search = s; this.pageIndex = 0; this.load(); }); this.load(); }
  load() {
    this.loading.set(true);
    this.api.suppliers({ search: this.search, includeInactive: this.includeInactive(), page: this.pageIndex + 1, pageSize: this.pageSize })
      .subscribe({ next: p => { this.page.set(p); this.loading.set(false); }, error: e => { this.loading.set(false); this.notify.error(e); } });
  }
  onPage(e: PageEvent) { this.pageIndex = e.pageIndex; this.pageSize = e.pageSize; this.load(); }
  edit(s: Supplier | null) { this.dialog.open(SupplierDialogComponent, { width: '560px', data: s }).afterClosed().subscribe(r => { if (r) this.load(); }); }
  pay(s: Supplier) { this.dialog.open(SupplierPaymentDialogComponent, { width: '480px', data: s }).afterClosed().subscribe(r => { if (r) this.load(); }); }
  statement(s: Supplier) { this.api.supplierStatement(s.id).subscribe({ next: st => this.dialog.open(StatementDialogComponent, { width: '760px', data: { statement: st, kind: 'supplier' } }), error: e => this.notify.error(e) }); }
}
