import { CommonModule } from '@angular/common';
import { Component, Inject, Pipe, PipeTransform, Input } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { MatBadgeModule } from '@angular/material/badge';
import { RouterModule } from '@angular/router';

/** Everything a feature screen usually needs, in one import. */
export const MATERIAL = [
  CommonModule, FormsModule, ReactiveFormsModule, RouterModule,
  MatButtonModule, MatCardModule, MatCheckboxModule, MatChipsModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule,
  MatProgressBarModule, MatSelectModule, MatTableModule, MatTabsModule, MatTooltipModule, MatAutocompleteModule, MatDatepickerModule, MatNativeDateModule,
  MatMenuModule, MatDividerModule, MatBadgeModule,
];

@Pipe({ name: 'inr', standalone: true })
export class InrPipe implements PipeTransform {
  private fmt = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', minimumFractionDigits: 2 });
  transform(value: number | null | undefined): string { return value == null ? '' : this.fmt.format(value); }
}

@Component({
  selector: 'bf-page',
  standalone: true,
  imports: [CommonModule],
  template: `
    <header class="page-header">
      <div><h1>{{ title }}</h1>@if (subtitle) {<p class="muted">{{ subtitle }}</p>}</div>
      <div class="actions"><ng-content select="[actions]"></ng-content></div>
    </header>
    <ng-content></ng-content>`,
})
export class PageComponent {
  @Input({ required: true }) title = '';
  @Input() subtitle?: string;
}

export interface ConfirmData { title: string; message: string; confirmText?: string; danger?: boolean; requireReason?: boolean; }

@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p>{{ data.message }}</p>
      @if (data.requireReason) {
        <mat-form-field appearance="outline" class="w-full"><mat-label>Reason</mat-label><input matInput [(ngModel)]="reason" maxlength="300" /></mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [color]="data.danger ? 'warn' : 'primary'" [disabled]="data.requireReason && !reason.trim()" (click)="ref.close(data.requireReason ? reason.trim() : true)">
        {{ data.confirmText || 'Confirm' }}
      </button>
    </mat-dialog-actions>`,
})
export class ConfirmDialogComponent {
  reason = '';
  constructor(@Inject(MAT_DIALOG_DATA) public data: ConfirmData, public ref: MatDialogRef<ConfirmDialogComponent>) { }
}

export function today(): string { return toIsoDate(new Date()); }
export function toIsoDate(d: Date | string | null | undefined): string {
  if (!d) return '';
  const dt = typeof d === 'string' ? new Date(d) : d;
  const tz = dt.getTimezoneOffset() * 60000;
  return new Date(dt.getTime() - tz).toISOString().slice(0, 10);
}
export function daysAgo(n: number): string { const d = new Date(); d.setDate(d.getDate() - n); return toIsoDate(d); }
