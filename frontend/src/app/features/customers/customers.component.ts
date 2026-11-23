import { Component, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { PageEvent } from '@angular/material/paginator';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { MATERIAL, InrPipe, PageComponent } from '../../shared/shared';
import { PartiesApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { AuthService } from '../../core/auth/auth.service';
import { Customer, PagedResult, Statement } from '../../core/models';
import { CustomerDialogComponent } from './customer-dialog.component';
import { StatementDialogComponent } from '../suppliers/statement-dialog.component';

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent],
  template: `
    <bf-page title="Customers" subtitle="Retail and institutional customers, dues and statements">
      <div actions>@if (auth.canBill) { <button mat-flat-button color="primary" (click)="edit(null)"><mat-icon>person_add</mat-icon> New customer</button> }</div>
    </bf-page>
    <div class="toolbar-row">
      <mat-form-field><mat-label>Search name / phone / GSTIN</mat-label><input matInput (input)="search$.next($any($event.target).value)" /></mat-form-field>
      <mat-checkbox [checked]="includeInactive()" (change)="includeInactive.set($event.checked); load()">Show inactive</mat-checkbox>
    </div>
    @if (loading()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }
    <div class="table-card">
      <table mat-table [dataSource]="page()?.items ?? []">
        <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>Name</th><td mat-cell *matCellDef="let c"><div>{{ c.name }}</div><div class="muted">{{ c.type }}{{ c.gstin ? ' · ' + c.gstin : '' }}</div></td></ng-container>
        <ng-container matColumnDef="phone"><th mat-header-cell *matHeaderCellDef>Phone</th><td mat-cell *matCellDef="let c">{{ c.phone }}</td></ng-container>
        <ng-container matColumnDef="limit"><th mat-header-cell *matHeaderCellDef class="num">Credit limit</th><td mat-cell *matCellDef="let c" class="num">{{ c.creditLimit ? (c.creditLimit | inr) : '—' }}</td></ng-container>
        <ng-container matColumnDef="due"><th mat-header-cell *matHeaderCellDef class="num">Outstanding</th><td mat-cell *matCellDef="let c" class="num"><span [class]="'chip ' + (c.outstanding > 0 ? 'warn' : 'ok')">{{ c.outstanding | inr }}</span></td></ng-container>
        <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let c"><span [class]="'chip ' + (c.isActive ? 'ok' : '')">{{ c.isActive ? 'Active' : 'Inactive' }}</span></td></ng-container>
        <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let c" class="num">
          <button mat-icon-button matTooltip="Statement" (click)="statement(c)"><mat-icon>receipt_long</mat-icon></button>
          @if (auth.canBill) { <button mat-icon-button matTooltip="Edit" (click)="edit(c)"><mat-icon>edit</mat-icon></button> }</td></ng-container>
        <tr mat-header-row *matHeaderRowDef="cols"></tr>
        <tr mat-row *matRowDef="let row; columns: cols"></tr>
      </table>
      @if (page() && !page()!.items.length) { <div class="empty">No customers found.</div> }
      <mat-paginator [length]="page()?.totalCount ?? 0" [pageSize]="pageSize" [pageIndex]="pageIndex" [pageSizeOptions]="[25, 50, 100]" (page)="onPage($event)"></mat-paginator>
    </div>`,
})
export class CustomersComponent {
  readonly auth = inject(AuthService);
  private api = inject(PartiesApi);
  private notify = inject(NotifyService);
  private dialog = inject(MatDialog);
  cols = ['name', 'phone', 'limit', 'due', 'status', 'actions'];
  page = signal<PagedResult<Customer> | null>(null);
  loading = signal(false);
  includeInactive = signal(false);
  search = ''; pageIndex = 0; pageSize = 25;
  search$ = new Subject<string>();

  constructor() {
    this.search$.pipe(debounceTime(300), distinctUntilChanged()).subscribe(s => { this.search = s; this.pageIndex = 0; this.load(); });
    this.load();
  }
  load() {
    this.loading.set(true);
    this.api.customers({ search: this.search, includeInactive: this.includeInactive(), page: this.pageIndex + 1, pageSize: this.pageSize })
      .subscribe({ next: p => { this.page.set(p); this.loading.set(false); }, error: e => { this.loading.set(false); this.notify.error(e); } });
  }
  onPage(e: PageEvent) { this.pageIndex = e.pageIndex; this.pageSize = e.pageSize; this.load(); }
  edit(c: Customer | null) { this.dialog.open(CustomerDialogComponent, { width: '560px', data: c }).afterClosed().subscribe(r => { if (r) this.load(); }); }
  statement(c: Customer) {
    this.api.customerStatement(c.id).subscribe({
      next: (s: Statement) => this.dialog.open(StatementDialogComponent, { width: '760px', data: { statement: s, kind: 'customer' } }),
      error: e => this.notify.error(e),
    });
  }
}
