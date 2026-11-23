import { Component, inject, signal } from '@angular/core';
import { MATERIAL, InrPipe, PageComponent, daysAgo, today } from '../../shared/shared';
import { ReportsApi, saveBlob } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';

interface ReportDef { key: string; label: string; path: string; cols: { key: string; label: string; type?: 'money' | 'num' | 'date' | 'pct' }[]; dateRange: boolean; extra?: 'year' | 'groupBy' | 'minDays'; }

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent],
  template: `
    <bf-page title="Reports" subtitle="Every report can be exported to Excel">
      <div actions><button mat-flat-button color="primary" (click)="download()" [disabled]="busy()"><mat-icon>download</mat-icon> Export Excel</button></div>
    </bf-page>
    <div class="toolbar-row">
      <mat-form-field class="rep"><mat-label>Report</mat-label><mat-select [value]="current().key" (valueChange)="select($event)">@for (r of reports; track r.key) { <mat-option [value]="r.key">{{ r.label }}</mat-option> }</mat-select></mat-form-field>
      @if (current().dateRange) {
        <mat-form-field><mat-label>From</mat-label><input matInput type="date" [value]="from" (change)="from = $any($event.target).value; load()" /></mat-form-field>
        <mat-form-field><mat-label>To</mat-label><input matInput type="date" [value]="to" (change)="to = $any($event.target).value; load()" /></mat-form-field>
      }
      @if (current().extra === 'year') { <mat-form-field><mat-label>Year</mat-label><input matInput type="number" [value]="year" (change)="year = +$any($event.target).value; load()" /></mat-form-field> }
      @if (current().extra === 'groupBy') { <mat-form-field><mat-label>Group by</mat-label><mat-select [value]="groupBy" (valueChange)="groupBy = $event; load()"><mat-option value="category">Category</mat-option><mat-option value="publisher">Publisher</mat-option><mat-option value="author">Author</mat-option><mat-option value="book">Book</mat-option></mat-select></mat-form-field> }
      @if (current().extra === 'minDays') { <mat-form-field><mat-label>Unsold for at least (days)</mat-label><input matInput type="number" [value]="minDays" (change)="minDays = +$any($event.target).value; load()" /></mat-form-field> }
    </div>
    @if (busy()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }
    <div class="table-card">
      <table mat-table [dataSource]="rows()">
        @for (c of current().cols; track c.key) {
          <ng-container [matColumnDef]="c.key">
            <th mat-header-cell *matHeaderCellDef [class.num]="c.type && c.type !== 'date'">{{ c.label }}</th>
            <td mat-cell *matCellDef="let r" [class.num]="c.type && c.type !== 'date'">
              @switch (c.type) { @case ('money') { {{ r[c.key] | inr }} } @case ('date') { {{ r[c.key] | date:'d MMM yyyy' }} } @case ('pct') { {{ r[c.key] }}% } @default { {{ r[c.key] }} } }
            </td>
          </ng-container>
        }
        <tr mat-header-row *matHeaderRowDef="colKeys()"></tr><tr mat-row *matRowDef="let row; columns: colKeys()"></tr>
      </table>
      @if (!busy() && !rows().length) { <div class="empty">No data for this selection.</div> }
      @if (rows().length) { <div class="muted" style="padding:8px 16px">{{ rows().length }} rows{{ totalsLabel() }}</div> }
    </div>`,
  styles: ['.rep { min-width: 260px; }'],
})
export class ReportsComponent {
  private api = inject(ReportsApi); private notify = inject(NotifyService);
  reports: ReportDef[] = [
    { key: 'daily', label: 'Daily sales', path: 'sales/daily', dateRange: true, cols: [{ key: 'date', label: 'Date', type: 'date' }, { key: 'invoices', label: 'Invoices', type: 'num' }, { key: 'gross', label: 'Gross', type: 'money' }, { key: 'discount', label: 'Discount', type: 'money' }, { key: 'tax', label: 'GST', type: 'money' }, { key: 'net', label: 'Net', type: 'money' }, { key: 'cost', label: 'Cost', type: 'money' }, { key: 'profit', label: 'Profit', type: 'money' }] },
    { key: 'monthly', label: 'Monthly sales', path: 'sales/monthly', dateRange: false, extra: 'year', cols: [{ key: 'month', label: 'Month', type: 'num' }, { key: 'invoices', label: 'Invoices', type: 'num' }, { key: 'net', label: 'Net sales', type: 'money' }, { key: 'profit', label: 'Profit', type: 'money' }] },
    { key: 'bybook', label: 'Sales by book (fast movers)', path: 'sales/by-book', dateRange: true, cols: [{ key: 'title', label: 'Title' }, { key: 'isbn', label: 'ISBN' }, { key: 'category', label: 'Category' }, { key: 'quantity', label: 'Qty', type: 'num' }, { key: 'returned', label: 'Returned', type: 'num' }, { key: 'revenue', label: 'Revenue', type: 'money' }, { key: 'profit', label: 'Profit', type: 'money' }] },
    { key: 'bycat', label: 'Sales by category', path: 'sales/by-category', dateRange: true, cols: [{ key: 'category', label: 'Category' }, { key: 'quantity', label: 'Qty', type: 'num' }, { key: 'revenue', label: 'Revenue', type: 'money' }, { key: 'profit', label: 'Profit', type: 'money' }] },
    { key: 'bysup', label: 'Purchases by supplier', path: 'purchases/by-supplier', dateRange: true, cols: [{ key: 'supplier', label: 'Supplier' }, { key: 'receipts', label: 'Receipts', type: 'num' }, { key: 'units', label: 'Units', type: 'num' }, { key: 'amount', label: 'Amount', type: 'money' }, { key: 'paid', label: 'Paid', type: 'money' }, { key: 'outstanding', label: 'Outstanding', type: 'money' }] },
    { key: 'movement', label: 'Stock movement', path: 'inventory/movement', dateRange: true, cols: [{ key: 'title', label: 'Book' }, { key: 'opening', label: 'Opening', type: 'num' }, { key: 'in', label: 'In', type: 'num' }, { key: 'out', label: 'Out', type: 'num' }, { key: 'closing', label: 'Closing', type: 'num' }] },
    { key: 'aging', label: 'Inventory aging (slow movers)', path: 'inventory/aging', dateRange: false, extra: 'minDays', cols: [{ key: 'title', label: 'Book' }, { key: 'quantityOnHand', label: 'On hand', type: 'num' }, { key: 'lastSoldAt', label: 'Last sold', type: 'date' }, { key: 'daysSinceLastSale', label: 'Days idle', type: 'num' }, { key: 'stockValue', label: 'Stock value', type: 'money' }] },
    { key: 'returns', label: 'Sales returns', path: 'returns', dateRange: true, cols: [{ key: 'date', label: 'Date', type: 'date' }, { key: 'returnNumber', label: 'Return' }, { key: 'invoiceNumber', label: 'Invoice' }, { key: 'customer', label: 'Customer' }, { key: 'refundMode', label: 'Refund' }, { key: 'total', label: 'Total', type: 'money' }, { key: 'reason', label: 'Reason' }] },
    { key: 'profit', label: 'Profitability', path: 'profitability', dateRange: true, extra: 'groupBy', cols: [{ key: 'group', label: 'Group' }, { key: 'revenue', label: 'Revenue', type: 'money' }, { key: 'cost', label: 'Cost', type: 'money' }, { key: 'profit', label: 'Profit', type: 'money' }, { key: 'marginPercent', label: 'Margin', type: 'pct' }] },
  ];
  current = signal<ReportDef>(this.reports[0]); rows = signal<Record<string, unknown>[]>([]); busy = signal(false);
  from = daysAgo(29); to = today(); year = new Date().getFullYear(); groupBy = 'category'; minDays = 90;
  constructor() { this.load(); }
  colKeys() { return this.current().cols.map(c => c.key); }
  select(key: string) { this.current.set(this.reports.find(r => r.key === key)!); this.load(); }
  private query() { const c = this.current(); return { ...(c.dateRange ? { from: this.from, to: this.to } : {}), ...(c.extra === 'year' ? { year: this.year } : {}), ...(c.extra === 'groupBy' ? { groupBy: this.groupBy } : {}), ...(c.extra === 'minDays' ? { minDays: this.minDays } : {}) }; }
  load() { this.busy.set(true); this.api.report<Record<string, unknown>>(this.current().path, this.query()).subscribe({ next: r => { this.rows.set(r); this.busy.set(false); }, error: e => { this.busy.set(false); this.notify.error(e); } }); }
  download() { this.busy.set(true); this.api.download(this.current().path, this.query()).subscribe({ next: b => { saveBlob(b, `bookflow-${this.current().key}.xlsx`); this.busy.set(false); }, error: e => { this.busy.set(false); this.notify.error(e); } }); }
  totalsLabel() {
    const money = this.current().cols.filter(c => c.type === 'money');
    if (!money.length) return '';
    return ' · ' + money.map(c => `${c.label} ${new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(this.rows().reduce((s, r) => s + (Number(r[c.key]) || 0), 0))}`).join(' · ');
  }
}
