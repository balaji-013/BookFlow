import { Component, Inject, inject, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MATERIAL } from '../../shared/shared';
import { PartiesApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { Customer } from '../../core/models';

@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Edit customer' : 'New customer' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid" id="custForm" (ngSubmit)="save()">
        <mat-form-field class="span2"><mat-label>Name</mat-label><input matInput formControlName="name" required /></mat-form-field>
        <mat-form-field><mat-label>Phone</mat-label><input matInput formControlName="phone" /></mat-form-field>
        <mat-form-field><mat-label>Email</mat-label><input matInput formControlName="email" type="email" /></mat-form-field>
        <mat-form-field><mat-label>Type</mat-label><mat-select formControlName="type"><mat-option value="Retail">Retail</mat-option><mat-option value="Institutional">Institutional (school / library)</mat-option></mat-select></mat-form-field>
        <mat-form-field><mat-label>Credit limit (0 = no limit)</mat-label><input matInput type="number" min="0" formControlName="creditLimit" /></mat-form-field>
        <mat-form-field><mat-label>GSTIN</mat-label><input matInput formControlName="gstin" style="text-transform:uppercase" /></mat-form-field>
        <mat-form-field><mat-label>GST state code</mat-label><input matInput formControlName="stateCode" maxlength="2" placeholder="33" /></mat-form-field>
        <mat-form-field class="span2"><mat-label>Address</mat-label><textarea matInput formControlName="address" rows="2"></textarea></mat-form-field>
        @if (data) { <mat-checkbox formControlName="isActive">Active</mat-checkbox> }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button color="primary" form="custForm" type="submit" [disabled]="form.invalid || busy()">Save</button>
    </mat-dialog-actions>`,
  styles: ['.span2 { grid-column: span 2; } .form-grid { grid-template-columns: 1fr 1fr; padding-top: 4px; }'],
})
export class CustomerDialogComponent {
  readonly data = inject<Customer | null>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<CustomerDialogComponent>>(MatDialogRef);
  private fb = inject(FormBuilder);
  private api = inject(PartiesApi);
  private notify = inject(NotifyService);
  busy = signal(false);
  form = this.fb.nonNullable.group({
    name: [this.data?.name ?? '', [Validators.required, Validators.maxLength(150)]],
    phone: [this.data?.phone ?? ''], email: [this.data?.email ?? ''], gstin: [this.data?.gstin ?? ''], stateCode: [this.data?.stateCode ?? ''],
    type: [this.data?.type ?? 'Retail'], creditLimit: [this.data?.creditLimit ?? 0], address: [this.data?.address ?? ''], isActive: [this.data?.isActive ?? true],
  });

  
  save() {
    if (this.form.invalid) return;
    this.busy.set(true);
    const v = this.form.getRawValue();
    const body = { ...v, gstin: v.gstin ? v.gstin.toUpperCase() : null, phone: v.phone || null, email: v.email || null, stateCode: v.stateCode || null, address: v.address || null, rowVersion: this.data?.rowVersion ?? null };
    this.api.saveCustomer(this.data?.id ?? null, body as unknown as Partial<Customer>).subscribe({
      next: c => { this.notify.success('Customer saved'); this.ref.close(c); },
      error: e => { this.busy.set(false); this.notify.error(e); },
    });
  }
}
