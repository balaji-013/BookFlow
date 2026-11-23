import { Component, Inject, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of, catchError } from 'rxjs';
import { MATERIAL, InrPipe, PageComponent, daysAgo, today } from '../../shared/shared';
import { PartiesApi, PaymentsApi, newIdempotencyKey } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { CreditNote, Customer, OutstandingInvoice, PagedResult, Payment, PaymentMethod } from '../../core/models';

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe],
  template: `
    <h2 mat-dialog-title>Record customer payment</h2>
    <mat-dialog-content>
      <mat-form-field class="w-full"><mat-label>Customer</mat-label><input matInput [matAutocomplete]="ca" (input)="search$.next($any($event.target).value)" [value]="customer()?.name || ''" />
        <mat-autocomplete #ca="matAutocomplete" (optionSelected)="select($event.option.value)" [displayWith]="dn">@for (c of customers(); track c.id) { <mat-option [value]="c">{{ c.name }} <span class="muted">{{ c.phone }} · due {{ c.outstanding | inr }}</span></mat-option> }</mat-autocomplete></mat-form-field>
      @if (customer()) {
        <div class="form-grid">
          <mat-form-field><mat-label>Amount</mat-label><input matInput type="number" min="0.01" step="0.01" [(ngModel)]="amount" (ngModelChange)="autoAllocate()" /></mat-form-field>
          <mat-form-field><mat-label>Method</mat-label><mat-select [(ngModel)]="method"><mat-option value="Cash">Cash</mat-option><mat-option value="Upi">UPI</mat-option><mat-option value="Card">Card</mat-option><mat-option value="BankTransfer">Bank transfer</mat-option><mat-option value="CreditNote" [disabled]="!creditNotes().length">Credit note</mat-option></mat-select></mat-form-field>
          @if (method === 'CreditNote') { <mat-form-field class="span2"><mat-label>Credit note</mat-label><mat-select [(ngModel)]="creditNoteId">@for (cn of creditNotes(); track cn.id) { <mat-option [value]="cn.id">{{ cn.creditNoteNumber }} — balance {{ cn.balance | inr }}</mat-option> }</mat-select></mat-form-field> }
          <mat-form-field class="span2"><mat-label>Reference</mat-label><input matInput [(ngModel)]="reference" /></mat-form-field>
        </div>
        <h4>Allocate to invoices <span class="muted">(oldest first; edit if needed)</span></h4>
        <table mat-table [dataSource]="outstanding()">
          <ng-container matColumnDef="no"><th mat-header-cell *matHeaderCellDef>Invoice</th><td mat-cell *matCellDef="let o">{{ o.invoiceNumber }}<div class="muted">{{ o.invoiceDate | date:'d MMM yyyy' }}</div></td></ng-container>
          <ng-container matColumnDef="due"><th mat-header-cell *matHeaderCellDef class="num">Due</th><td mat-cell *matCellDef="let o" class="num">{{ o.amountDue | inr }}</td></ng-container>
          <ng-container matColumnDef="alloc"><th mat-header-cell *matHeaderCellDef class="num">Allocate</th><td mat-cell *matCellDef="let o" class="num"><input type="number" min="0" [max]="o.amountDue" step="0.01" [(ngModel)]="alloc[o.id]" style="width:100px;text-align:right" /></td></ng-container>
          <tr mat-header-row *matHeaderRowDef="['no','due','alloc']"></tr><tr mat-row *matRowDef="let row; columns: ['no','due','alloc']"></tr>
        </table>
        @if (!outstanding().length) { <p class="muted">No unpaid invoices. The payment will be recorded as an unallocated advance.</p> }
        <p class="num">Allocated {{ allocated() | inr }} of {{ amount | inr }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button color="primary" (click)="save()" [disabled]="!customer() || amount <= 0 || allocated() > amount + 0.001 || busy()">Save payment</button></mat-dialog-actions>`,
  styles: ['.span2 { grid-column: span 2; } .form-grid { grid-template-columns: 1fr 1fr; } h4 { margin: 12px 0 4px; font-weight: 500; }'],
})
export class PaymentDialogComponent {
  readonly data = inject<{ customerId?: string | null }>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<PaymentDialogComponent>>(MatDialogRef);
  private parties = inject(PartiesApi); private api = inject(PaymentsApi); private notify = inject(NotifyService);
  customers = signal<Customer[]>([]); customer = signal<Customer | null>(null); outstanding = signal<OutstandingInvoice[]>([]); creditNotes = signal<CreditNote[]>([]);
  amount = 0; method: PaymentMethod = 'Cash'; reference = ''; creditNoteId: string | null = null; alloc: Record<string, number> = {}; busy = signal(false);
  search$ = new Subject<string>(); private key = newIdempotencyKey();
  dn = (c: Customer | null) => c?.name ?? '';
  constructor() {
    this.search$.pipe(debounceTime(250), distinctUntilChanged(), switchMap(t => this.parties.customers({ search: t, pageSize: 10 }).pipe(catchError(() => of(null))))).subscribe(r => this.customers.set(r?.items ?? []));
    if (this.data?.customerId) this.parties.customer(this.data.customerId).subscribe(c => this.select(c));
  }
  select(c: Customer) {
    this.customer.set(c);
    this.parties.outstanding(c.id).subscribe(o => { this.outstanding.set(o); this.amount = o.reduce((s, x) => s + x.amountDue, 0); this.autoAllocate(); });
    this.api.creditNotes({ customerId: c.id, openOnly: true, pageSize: 50 }).subscribe(cn => this.creditNotes.set(cn.items));
  }
  autoAllocate() { let left = this.amount; this.alloc = {}; for (const o of this.outstanding()) { const a = Math.min(left, o.amountDue); if (a > 0) { this.alloc[o.id] = Math.round(a * 100) / 100; left -= a; } } }
  allocated() { return Object.values(this.alloc).reduce((s, v) => s + (+v || 0), 0); }
  save() {
    this.busy.set(true);
    const allocations = Object.entries(this.alloc).filter(([, v]) => +v > 0).map(([invoiceId, amount]) => ({ invoiceId, amount: +amount }));
    this.api.create({ customerId: this.customer()!.id, method: this.method, amount: this.amount, reference: this.reference || null, allocations, creditNoteId: this.method === 'CreditNote' ? this.creditNoteId : null }, this.key)
      .subscribe({ next: p => { this.notify.success(`Payment ${p.paymentNumber} recorded`); this.ref.close(true); }, error: e => { this.busy.set(false); this.key = newIdempotencyKey(); this.notify.error(e); } });
  }
}

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent],
  template: `
    <bf-page title="Payments" subtitle="Customer receipts, refunds and credit notes">
      <div actions><button mat-flat-button color="primary" (click)="record()"><mat-icon>add</mat-icon> Record payment</button></div>
    </bf-page>
    <mat-tab-group>
      <mat-tab label="Payments">
        <div class="toolbar-row" style="margin-top:12px">
          <mat-form-field><mat-label>From</mat-label><input matInput type="date" [value]="from" (change)="from = $any($event.target).value; load()" /></mat-form-field>
          <mat-form-field><mat-label>To</mat-label><input matInput type="date" [value]="to" (change)="to = $any($event.target).value; load()" /></mat-form-field>
          <mat-form-field><mat-label>Direction</mat-label><mat-select [value]="direction" (valueChange)="direction = $event; load()"><mat-option [value]="null">All</mat-option><mat-option value="In">Received</mat-option><mat-option value="Out">Refunded</mat-option></mat-select></mat-form-field>
        </div>
        <div class="table-card">
          <table mat-table [dataSource]="page()?.items ?? []">
            <ng-container matColumnDef="no"><th mat-header-cell *matHeaderCellDef>Payment</th><td mat-cell *matCellDef="let p"><div class="mono">{{ p.paymentNumber }}</div><div class="muted">{{ p.paidAt | date:'d MMM yyyy, HH:mm' }}</div></td></ng-container>
            <ng-container matColumnDef="customer"><th mat-header-cell *matHeaderCellDef>Customer</th><td mat-cell *matCellDef="let p">{{ p.customerName || 'Walk-in' }}</td></ng-container>
            <ng-container matColumnDef="method"><th mat-header-cell *matHeaderCellDef>Method</th><td mat-cell *matCellDef="let p">{{ p.method }} <span class="muted">{{ p.reference }}</span></td></ng-container>
            <ng-container matColumnDef="alloc"><th mat-header-cell *matHeaderCellDef>Invoices</th><td mat-cell *matCellDef="let p">@for (a of p.allocations; track a.invoiceId) { <span class="chip">{{ a.invoiceNumber }}</span> }</td></ng-container>
            <ng-container matColumnDef="amount"><th mat-header-cell *matHeaderCellDef class="num">Amount</th><td mat-cell *matCellDef="let p" class="num" [style.color]="p.direction === 'Out' ? '#a6192e' : ''">{{ p.direction === 'Out' ? '-' : '' }}{{ p.amount | inr }}</td></ng-container>
            <tr mat-header-row *matHeaderRowDef="cols"></tr><tr mat-row *matRowDef="let row; columns: cols"></tr>
          </table>
          @if (page() && !page()!.items.length) { <div class="empty">No payments in this range.</div> }
          <mat-paginator [length]="page()?.totalCount ?? 0" [pageSize]="25" (page)="pageIndex = $event.pageIndex; load()"></mat-paginator>
        </div>
      </mat-tab>
      <mat-tab label="Credit notes">
        <div class="table-card" style="margin-top:12px">
          <table mat-table [dataSource]="notes()?.items ?? []">
            <ng-container matColumnDef="no"><th mat-header-cell *matHeaderCellDef>Credit note</th><td mat-cell *matCellDef="let c"><div class="mono">{{ c.creditNoteNumber }}</div><div class="muted">{{ c.issuedAt | date:'d MMM yyyy' }}</div></td></ng-container>
            <ng-container matColumnDef="customer"><th mat-header-cell *matHeaderCellDef>Customer</th><td mat-cell *matCellDef="let c">{{ c.customerName }}</td></ng-container>
            <ng-container matColumnDef="amount"><th mat-header-cell *matHeaderCellDef class="num">Amount</th><td mat-cell *matCellDef="let c" class="num">{{ c.amount | inr }}</td></ng-container>
            <ng-container matColumnDef="balance"><th mat-header-cell *matHeaderCellDef class="num">Balance</th><td mat-cell *matCellDef="let c" class="num">{{ c.balance | inr }}</td></ng-container>
            <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let c"><span [class]="'chip ' + (c.status === 'Open' ? 'ok' : c.status === 'Used' ? '' : 'warn')">{{ c.status }}</span></td></ng-container>
            <tr mat-header-row *matHeaderRowDef="cnCols"></tr><tr mat-row *matRowDef="let row; columns: cnCols"></tr>
          </table>
          @if (notes() && !notes()!.items.length) { <div class="empty">No credit notes.</div> }
        </div>
      </mat-tab>
    </mat-tab-group>`,
})
export class PaymentsComponent {
  private api = inject(PaymentsApi); private notify = inject(NotifyService); private dialog = inject(MatDialog); private route = inject(ActivatedRoute);
  cols = ['no', 'customer', 'method', 'alloc', 'amount']; cnCols = ['no', 'customer', 'amount', 'balance', 'status'];
  page = signal<PagedResult<Payment> | null>(null); notes = signal<PagedResult<CreditNote> | null>(null);
  from = daysAgo(30); to = today(); direction: string | null = null; pageIndex = 0;
  constructor() {
    this.load(); this.api.creditNotes({ pageSize: 100 }).subscribe(n => this.notes.set(n));
    const cid = this.route.snapshot.queryParamMap.get('customerId');
    if (cid) setTimeout(() => this.record(cid));
  }
  load() { this.api.list({ from: this.from, to: this.to, direction: this.direction, page: this.pageIndex + 1, pageSize: 25 }).subscribe({ next: p => this.page.set(p), error: e => this.notify.error(e) }); }
  record(customerId: string | null = null) { this.dialog.open(PaymentDialogComponent, { width: '720px', data: { customerId } }).afterClosed().subscribe(ok => { if (ok) { this.load(); this.api.creditNotes({ pageSize: 100 }).subscribe(n => this.notes.set(n)); } }); }
}
