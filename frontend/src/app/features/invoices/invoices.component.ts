import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PageEvent } from '@angular/material/paginator';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { MATERIAL, InrPipe, PageComponent, daysAgo, today } from '../../shared/shared';
import { SalesApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { InvoiceListItem, PagedResult } from '../../core/models';

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent, RouterLink],
  template: `
    <bf-page title="Invoices" subtitle="All sales invoices, with payment status">
      <div actions><a mat-flat-button color="primary" routerLink="/pos"><mat-icon>point_of_sale</mat-icon> New sale</a></div>
    </bf-page>
    <div class="toolbar-row">
      <mat-form-field><mat-label>Search invoice no. / customer</mat-label><input matInput (input)="search$.next($any($event.target).value)" /></mat-form-field>
      <mat-form-field><mat-label>From</mat-label><input matInput type="date" [value]="from" (change)="from = $any($event.target).value; reload()" /></mat-form-field>
      <mat-form-field><mat-label>To</mat-label><input matInput type="date" [value]="to" (change)="to = $any($event.target).value; reload()" /></mat-form-field>
      <mat-form-field><mat-label>Payment</mat-label><mat-select [value]="paymentStatus" (valueChange)="paymentStatus = $event; reload()"><mat-option [value]="null">All</mat-option><mat-option value="Unpaid">Unpaid</mat-option><mat-option value="Partial">Partial</mat-option><mat-option value="Paid">Paid</mat-option></mat-select></mat-form-field>
      <mat-form-field><mat-label>Status</mat-label><mat-select [value]="status" (valueChange)="status = $event; reload()"><mat-option [value]="null">All</mat-option><mat-option value="Issued">Issued</mat-option><mat-option value="PartiallyReturned">Partially returned</mat-option><mat-option value="Returned">Returned</mat-option><mat-option value="Cancelled">Cancelled</mat-option></mat-select></mat-form-field>
    </div>
    @if (loading()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }
    <div class="table-card">
      <table mat-table [dataSource]="page()?.items ?? []">
        <ng-container matColumnDef="no"><th mat-header-cell *matHeaderCellDef>Invoice</th><td mat-cell *matCellDef="let i"><a [routerLink]="['/invoices', i.id]" class="mono">{{ i.invoiceNumber }}</a><div class="muted">{{ i.invoiceDate | date:'d MMM yyyy' }} · {{ i.createdAt | date:'HH:mm' }}</div></td></ng-container>
        <ng-container matColumnDef="customer"><th mat-header-cell *matHeaderCellDef>Customer</th><td mat-cell *matCellDef="let i">{{ i.customerName }}</td></ng-container>
        <ng-container matColumnDef="items"><th mat-header-cell *matHeaderCellDef class="num">Items</th><td mat-cell *matCellDef="let i" class="num">{{ i.itemCount }}</td></ng-container>
        <ng-container matColumnDef="total"><th mat-header-cell *matHeaderCellDef class="num">Total</th><td mat-cell *matCellDef="let i" class="num">{{ i.grandTotal | inr }}</td></ng-container>
        <ng-container matColumnDef="due"><th mat-header-cell *matHeaderCellDef class="num">Due</th><td mat-cell *matCellDef="let i" class="num">{{ i.amountDue ? (i.amountDue | inr) : '—' }}</td></ng-container>
        <ng-container matColumnDef="pay"><th mat-header-cell *matHeaderCellDef>Payment</th><td mat-cell *matCellDef="let i"><span [class]="'chip ' + (i.paymentStatus === 'Paid' ? 'ok' : i.paymentStatus === 'Partial' ? 'warn' : 'bad')">{{ i.paymentStatus }}</span></td></ng-container>
        <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let i"><span [class]="'chip ' + (i.status === 'Issued' ? '' : i.status === 'Cancelled' ? 'bad' : 'info')">{{ i.status }}</span></td></ng-container>
        <tr mat-header-row *matHeaderRowDef="cols"></tr>
        <tr mat-row *matRowDef="let row; columns: cols" [routerLink]="['/invoices', row.id]" class="clickable"></tr>
      </table>
      @if (page() && !page()!.items.length) { <div class="empty">No invoices in this range.</div> }
      <mat-paginator [length]="page()?.totalCount ?? 0" [pageSize]="pageSize" [pageIndex]="pageIndex" [pageSizeOptions]="[25, 50, 100]" (page)="onPage($event)"></mat-paginator>
    </div>`,
})
export class InvoicesComponent {
  private api = inject(SalesApi); private notify = inject(NotifyService);
  cols = ['no', 'customer', 'items', 'total', 'due', 'pay', 'status'];
  page = signal<PagedResult<InvoiceListItem> | null>(null); loading = signal(false);
  search = ''; from = daysAgo(30); to = today(); paymentStatus: string | null = null; status: string | null = null; pageIndex = 0; pageSize = 25;
  search$ = new Subject<string>();
  constructor() { this.search$.pipe(debounceTime(300), distinctUntilChanged()).subscribe(s => { this.search = s; this.reload(); }); this.load(); }
  reload() { this.pageIndex = 0; this.load(); }
  load() {
    this.loading.set(true);
    this.api.invoices({ search: this.search, from: this.from, to: this.to, paymentStatus: this.paymentStatus, status: this.status, page: this.pageIndex + 1, pageSize: this.pageSize })
      .subscribe({ next: p => { this.page.set(p); this.loading.set(false); }, error: e => { this.loading.set(false); this.notify.error(e); } });
  }
  onPage(e: PageEvent) { this.pageIndex = e.pageIndex; this.pageSize = e.pageSize; this.load(); }
}
