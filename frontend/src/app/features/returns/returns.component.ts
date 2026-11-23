import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MATERIAL, InrPipe, PageComponent, daysAgo, today } from '../../shared/shared';
import { SalesApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { PagedResult, SalesReturn } from '../../core/models';

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent, RouterLink],
  template: `
    <bf-page title="Sales returns" subtitle="To return items, open the invoice and choose “Return items”."></bf-page>
    <div class="toolbar-row">
      <mat-form-field><mat-label>From</mat-label><input matInput type="date" [value]="from" (change)="from = $any($event.target).value; load()" /></mat-form-field>
      <mat-form-field><mat-label>To</mat-label><input matInput type="date" [value]="to" (change)="to = $any($event.target).value; load()" /></mat-form-field>
    </div>
    <div class="table-card">
      <table mat-table [dataSource]="page()?.items ?? []">
        <ng-container matColumnDef="no"><th mat-header-cell *matHeaderCellDef>Return</th><td mat-cell *matCellDef="let r"><div class="mono">{{ r.returnNumber }}</div><div class="muted">{{ r.returnDate | date:'d MMM yyyy' }}</div></td></ng-container>
        <ng-container matColumnDef="inv"><th mat-header-cell *matHeaderCellDef>Invoice</th><td mat-cell *matCellDef="let r"><a [routerLink]="['/invoices', r.salesInvoiceId]">{{ r.invoiceNumber }}</a><div class="muted">{{ r.customerName }}</div></td></ng-container>
        <ng-container matColumnDef="items"><th mat-header-cell *matHeaderCellDef>Items</th><td mat-cell *matCellDef="let r">@for (i of r.items; track i.id) { <div>{{ i.quantity }} × {{ i.title }} <span class="chip" [class.bad]="i.condition === 'Damaged'">{{ i.condition }}</span></div> }</td></ng-container>
        <ng-container matColumnDef="refund"><th mat-header-cell *matHeaderCellDef>Refund</th><td mat-cell *matCellDef="let r">{{ r.refundMode }}{{ r.creditNoteNumber ? ' · ' + r.creditNoteNumber : '' }}<div class="muted">{{ r.reason }}</div></td></ng-container>
        <ng-container matColumnDef="total"><th mat-header-cell *matHeaderCellDef class="num">Total</th><td mat-cell *matCellDef="let r" class="num">{{ r.total | inr }}</td></ng-container>
        <tr mat-header-row *matHeaderRowDef="cols"></tr><tr mat-row *matRowDef="let row; columns: cols"></tr>
      </table>
      @if (page() && !page()!.items.length) { <div class="empty">No returns in this range.</div> }
      <mat-paginator [length]="page()?.totalCount ?? 0" [pageSize]="25" (page)="pageIndex = $event.pageIndex; load()"></mat-paginator>
    </div>`,
})
export class ReturnsComponent {
  private api = inject(SalesApi); private notify = inject(NotifyService);
  cols = ['no', 'inv', 'items', 'refund', 'total'];
  page = signal<PagedResult<SalesReturn> | null>(null); from = daysAgo(30); to = today(); pageIndex = 0;
  constructor() { this.load(); }
  load() { this.api.returns({ from: this.from, to: this.to, page: this.pageIndex + 1, pageSize: 25 }).subscribe({ next: p => this.page.set(p), error: e => this.notify.error(e) }); }
}
