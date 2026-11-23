import { Component, Inject, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { PageEvent } from '@angular/material/paginator';
import { Observable, Subject, debounceTime, distinctUntilChanged, switchMap, of, catchError } from 'rxjs';
import { MATERIAL, InrPipe, PageComponent, ConfirmDialogComponent } from '../../shared/shared';
import { CatalogApi, PartiesApi, PurchasingApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { AuthService } from '../../core/auth/auth.service';
import { BookLookup, GoodsReceipt, PagedResult, PurchaseOrder, Supplier } from '../../core/models';

interface PoLine { book: BookLookup; quantity: number; unitCost: number; }

// ---------- New purchase order / direct receipt ----------
@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe],
  template: `
    <h2 mat-dialog-title>{{ data.mode === 'po' ? 'New purchase order' : 'Receive goods without PO' }}</h2>
    <mat-dialog-content>
      <div class="form-grid">
        <mat-form-field class="span2"><mat-label>Supplier</mat-label><input matInput [matAutocomplete]="sa" (input)="supSearch$.next($any($event.target).value)" [value]="supplier()?.name || ''" />
          <mat-autocomplete #sa="matAutocomplete" (optionSelected)="supplier.set($event.option.value)" [displayWith]="dn">@for (s of suppliers(); track s.id) { <mat-option [value]="s">{{ s.name }}</mat-option> }</mat-autocomplete></mat-form-field>
        @if (data.mode === 'grn') { <mat-form-field><mat-label>Supplier invoice no.</mat-label><input matInput [(ngModel)]="supplierInvoice" /></mat-form-field> }
        @if (data.mode === 'po') { <mat-form-field><mat-label>Expected date</mat-label><input matInput type="date" [(ngModel)]="expected" /></mat-form-field> }
      </div>
      <mat-form-field class="w-full"><mat-label>Add book</mat-label><input matInput [matAutocomplete]="ba" (input)="bookSearch$.next($any($event.target).value)" #inp />
        <mat-autocomplete #ba="matAutocomplete" (optionSelected)="add($event.option.value); inp.value=''">@for (b of books(); track b.id) { <mat-option [value]="b">{{ b.title }} <span class="muted">stock {{ b.quantityOnHand }}</span></mat-option> }</mat-autocomplete></mat-form-field>
      <table mat-table [dataSource]="lines()">
        <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Book</th><td mat-cell *matCellDef="let l">{{ l.book.title }}</td></ng-container>
        <ng-container matColumnDef="qty"><th mat-header-cell *matHeaderCellDef>Qty</th><td mat-cell *matCellDef="let l"><input type="number" min="1" [(ngModel)]="l.quantity" style="width:70px" /></td></ng-container>
        <ng-container matColumnDef="cost"><th mat-header-cell *matHeaderCellDef>Unit cost</th><td mat-cell *matCellDef="let l"><input type="number" min="0" step="0.01" [(ngModel)]="l.unitCost" style="width:90px" /></td></ng-container>
        <ng-container matColumnDef="total"><th mat-header-cell *matHeaderCellDef class="num">Total</th><td mat-cell *matCellDef="let l" class="num">{{ l.quantity * l.unitCost | inr }}</td></ng-container>
        <ng-container matColumnDef="x"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let l"><button mat-icon-button (click)="removeLine(l)"><mat-icon>close</mat-icon></button></td></ng-container>
        <tr mat-header-row *matHeaderRowDef="['title','qty','cost','total','x']"></tr><tr mat-row *matRowDef="let row; columns: ['title','qty','cost','total','x']"></tr>
      </table>
      <p class="num" style="margin:8px 0"><strong>Total {{ total() | inr }}</strong> <span class="muted">(before GST)</span></p>
      <mat-form-field class="w-full"><mat-label>Notes</mat-label><input matInput [(ngModel)]="notes" /></mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      @if (data.mode === 'po') { <button mat-stroked-button (click)="save(false)" [disabled]="!valid() || busy()">Save draft</button><button mat-flat-button color="primary" (click)="save(true)" [disabled]="!valid() || busy()">Submit order</button> }
      @else { <button mat-flat-button color="primary" (click)="save(true)" [disabled]="!valid() || busy()">Receive into stock</button> }
    </mat-dialog-actions>`,
  styles: ['.span2 { grid-column: span 2; } .form-grid { grid-template-columns: 1fr 1fr 1fr; padding-top: 4px; }'],
})
export class PoDialogComponent {
  readonly data = inject<{ mode: 'po' | 'grn' }>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<PoDialogComponent>>(MatDialogRef);
  private catalog = inject(CatalogApi); private parties = inject(PartiesApi); private api = inject(PurchasingApi); private notify = inject(NotifyService);
  supplier = signal<Supplier | null>(null); suppliers = signal<Supplier[]>([]); books = signal<BookLookup[]>([]); lines = signal<PoLine[]>([]);
  expected = ''; notes = ''; supplierInvoice = ''; busy = signal(false);
  supSearch$ = new Subject<string>(); bookSearch$ = new Subject<string>();
  dn = (s: Supplier | null) => s?.name ?? '';
  constructor() {
    this.supSearch$.pipe(debounceTime(250), distinctUntilChanged(), switchMap(t => this.parties.suppliers({ search: t, pageSize: 10 }).pipe(catchError(() => of(null))))).subscribe(r => this.suppliers.set(r?.items ?? []));
    this.bookSearch$.pipe(debounceTime(200), distinctUntilChanged(), switchMap(t => t.length < 2 ? of([]) : this.catalog.quickSearch(t).pipe(catchError(() => of([]))))).subscribe(r => this.books.set(r));
    this.parties.suppliers({ pageSize: 10 }).subscribe(r => this.suppliers.set(r.items));
  }
  add(b: BookLookup) { if (b && !this.lines().some(l => l.book.id === b.id)) this.lines.update(l => [...l, { book: b, quantity: 1, unitCost: Math.round(b.sellingPrice * 0.7 * 100) / 100 }]); }
  removeLine(l: PoLine) { this.lines.set(this.lines().filter(x => x !== l)); }
  total() { return this.lines().reduce((s, l) => s + l.quantity * l.unitCost, 0); }
  valid() { return !!this.supplier() && this.lines().length > 0 && this.lines().every(l => l.quantity > 0 && l.unitCost >= 0); }
  save(submit: boolean) {
    this.busy.set(true);
    const lines = this.lines().map(l => ({ bookId: l.book.id, quantity: l.quantity, unitCost: l.unitCost }));
    const req: Observable<unknown> = this.data.mode === 'po'
      ? this.api.createOrder({ supplierId: this.supplier()!.id, expectedDate: this.expected || null, notes: this.notes || null, lines, submit })
      : this.api.directReceipt({ supplierId: this.supplier()!.id, supplierInvoiceNumber: this.supplierInvoice || null, notes: this.notes || null, lines });
    req.subscribe({ next: () => { this.notify.success(this.data.mode === 'po' ? 'Purchase order saved' : 'Stock received'); this.ref.close(true); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
}

// ---------- Receive against PO ----------
@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>Receive goods — {{ po.poNumber }}</h2>
    <mat-dialog-content>
      <p class="muted">{{ po.supplierName }}. Enter the quantities that physically arrived; partial receipts are fine.</p>
      <div class="form-grid"><mat-form-field><mat-label>Supplier invoice no.</mat-label><input matInput [(ngModel)]="invoiceNo" /></mat-form-field><mat-form-field><mat-label>Supplier invoice date</mat-label><input matInput type="date" [(ngModel)]="invoiceDate" /></mat-form-field></div>
      <table mat-table [dataSource]="rows">
        <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Book</th><td mat-cell *matCellDef="let r">{{ r.item.title }}</td></ng-container>
        <ng-container matColumnDef="ordered"><th mat-header-cell *matHeaderCellDef class="num">Ordered</th><td mat-cell *matCellDef="let r" class="num">{{ r.item.quantityOrdered }}</td></ng-container>
        <ng-container matColumnDef="received"><th mat-header-cell *matHeaderCellDef class="num">Already received</th><td mat-cell *matCellDef="let r" class="num">{{ r.item.quantityReceived }}</td></ng-container>
        <ng-container matColumnDef="now"><th mat-header-cell *matHeaderCellDef>Receive now</th><td mat-cell *matCellDef="let r"><input type="number" min="0" [max]="r.item.quantityOrdered - r.item.quantityReceived" [(ngModel)]="r.qty" style="width:80px" /></td></ng-container>
        <ng-container matColumnDef="cost"><th mat-header-cell *matHeaderCellDef>Unit cost</th><td mat-cell *matCellDef="let r"><input type="number" min="0" step="0.01" [(ngModel)]="r.cost" style="width:90px" /></td></ng-container>
        <tr mat-header-row *matHeaderRowDef="['title','ordered','received','now','cost']"></tr><tr mat-row *matRowDef="let row; columns: ['title','ordered','received','now','cost']"></tr>
      </table>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button color="primary" (click)="save()" [disabled]="!valid() || busy()">Receive</button></mat-dialog-actions>`,
  styles: ['.form-grid { grid-template-columns: 1fr 1fr; padding-top: 4px; }'],
})
export class ReceiveDialogComponent {
  readonly po = inject<PurchaseOrder>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ReceiveDialogComponent>>(MatDialogRef);
  private api = inject(PurchasingApi); private notify = inject(NotifyService);
  rows = this.po.items.map(item => ({ item, qty: item.quantityOrdered - item.quantityReceived, cost: item.unitCost }));
  invoiceNo = ''; invoiceDate = ''; busy = signal(false);
    valid() { return this.rows.some(r => r.qty > 0) && this.rows.every(r => r.qty >= 0 && r.qty <= r.item.quantityOrdered - r.item.quantityReceived); }
  save() {
    this.busy.set(true);
    const lines = this.rows.filter(r => r.qty > 0).map(r => ({ bookId: r.item.bookId, quantity: r.qty, unitCost: r.cost }));
    this.api.receive(this.po.id, { supplierInvoiceNumber: this.invoiceNo || null, supplierInvoiceDate: this.invoiceDate || null, lines })
      .subscribe({ next: g => { this.notify.success(`${g.grnNumber} received into stock`); this.ref.close(true); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
}

// ---------- Main screen ----------
@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent],
  template: `
    <bf-page title="Purchasing" subtitle="Purchase orders, goods receipts and supplier returns">
      <div actions>
        <button mat-stroked-button (click)="open('grn')"><mat-icon>move_to_inbox</mat-icon> Receive without PO</button>
        <button mat-flat-button color="primary" (click)="open('po')"><mat-icon>add_shopping_cart</mat-icon> New purchase order</button>
      </div>
    </bf-page>
    <mat-tab-group>
      <mat-tab label="Purchase orders">
        <div class="toolbar-row" style="margin-top:12px"><mat-form-field><mat-label>Status</mat-label><mat-select [value]="status" (valueChange)="status = $event; loadOrders()"><mat-option [value]="null">All</mat-option>@for (s of ['Draft','Submitted','PartiallyReceived','Received','Cancelled']; track s) { <mat-option [value]="s">{{ s }}</mat-option> }</mat-select></mat-form-field></div>
        <div class="table-card">
          <table mat-table [dataSource]="orders()?.items ?? []">
            <ng-container matColumnDef="no"><th mat-header-cell *matHeaderCellDef>PO</th><td mat-cell *matCellDef="let p"><div class="mono">{{ p.poNumber }}</div><div class="muted">{{ p.orderDate | date:'d MMM yyyy' }}</div></td></ng-container>
            <ng-container matColumnDef="supplier"><th mat-header-cell *matHeaderCellDef>Supplier</th><td mat-cell *matCellDef="let p">{{ p.supplierName }}</td></ng-container>
            <ng-container matColumnDef="items"><th mat-header-cell *matHeaderCellDef>Items</th><td mat-cell *matCellDef="let p">{{ p.items.length }} titles · {{ ordered(p) }} ordered · {{ received(p) }} received</td></ng-container>
            <ng-container matColumnDef="total"><th mat-header-cell *matHeaderCellDef class="num">Total</th><td mat-cell *matCellDef="let p" class="num">{{ p.total | inr }}</td></ng-container>
            <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let p"><span [class]="'chip ' + chip(p.status)">{{ p.status }}</span></td></ng-container>
            <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let p" class="num">
              @if (p.status === 'Draft') { <button mat-button (click)="submit(p)">Submit</button> }
              @if (p.status === 'Submitted' || p.status === 'PartiallyReceived') { <button mat-flat-button color="primary" (click)="receive(p)">Receive</button> }
              @if (p.status === 'Draft' || p.status === 'Submitted') { <button mat-icon-button matTooltip="Cancel order" (click)="cancel(p)"><mat-icon>cancel</mat-icon></button> }</td></ng-container>
            <tr mat-header-row *matHeaderRowDef="poCols"></tr><tr mat-row *matRowDef="let row; columns: poCols"></tr>
          </table>
          @if (orders() && !orders()!.items.length) { <div class="empty">No purchase orders.</div> }
          <mat-paginator [length]="orders()?.totalCount ?? 0" [pageSize]="25" (page)="poPage = $event.pageIndex; loadOrders()"></mat-paginator>
        </div>
      </mat-tab>
      <mat-tab label="Goods receipts">
        <div class="table-card" style="margin-top:12px">
          <table mat-table [dataSource]="receipts()?.items ?? []">
            <ng-container matColumnDef="no"><th mat-header-cell *matHeaderCellDef>GRN</th><td mat-cell *matCellDef="let g"><div class="mono">{{ g.grnNumber }}</div><div class="muted">{{ g.receivedDate | date:'d MMM yyyy' }}{{ g.poNumber ? ' · ' + g.poNumber : ' · direct' }}</div></td></ng-container>
            <ng-container matColumnDef="supplier"><th mat-header-cell *matHeaderCellDef>Supplier</th><td mat-cell *matCellDef="let g">{{ g.supplierName }}<div class="muted">{{ g.supplierInvoiceNumber }}</div></td></ng-container>
            <ng-container matColumnDef="items"><th mat-header-cell *matHeaderCellDef>Items</th><td mat-cell *matCellDef="let g">{{ g.items.length }} titles · {{ units(g) }} units</td></ng-container>
            <ng-container matColumnDef="total"><th mat-header-cell *matHeaderCellDef class="num">Total</th><td mat-cell *matCellDef="let g" class="num">{{ g.total | inr }}</td></ng-container>
            <ng-container matColumnDef="paid"><th mat-header-cell *matHeaderCellDef class="num">Paid</th><td mat-cell *matCellDef="let g" class="num">{{ g.amountPaid | inr }}</td></ng-container>
            <tr mat-header-row *matHeaderRowDef="grnCols"></tr><tr mat-row *matRowDef="let row; columns: grnCols"></tr>
          </table>
          @if (receipts() && !receipts()!.items.length) { <div class="empty">No goods received yet.</div> }
          <mat-paginator [length]="receipts()?.totalCount ?? 0" [pageSize]="25" (page)="grnPage = $event.pageIndex; loadReceipts()"></mat-paginator>
        </div>
      </mat-tab>
    </mat-tab-group>`,
})
export class PurchasingComponent {
  readonly auth = inject(AuthService);
  private api = inject(PurchasingApi); private notify = inject(NotifyService); private dialog = inject(MatDialog);
  poCols = ['no', 'supplier', 'items', 'total', 'status', 'actions']; grnCols = ['no', 'supplier', 'items', 'total', 'paid'];
  orders = signal<PagedResult<PurchaseOrder> | null>(null); receipts = signal<PagedResult<GoodsReceipt> | null>(null);
  status: string | null = null; poPage = 0; grnPage = 0;
  constructor() { this.loadOrders(); this.loadReceipts(); }
  loadOrders() { this.api.orders({ status: this.status, page: this.poPage + 1, pageSize: 25 }).subscribe({ next: p => this.orders.set(p), error: e => this.notify.error(e) }); }
  loadReceipts() { this.api.receipts({ page: this.grnPage + 1, pageSize: 25 }).subscribe({ next: p => this.receipts.set(p), error: e => this.notify.error(e) }); }
  ordered(p: PurchaseOrder) { return p.items.reduce((s, i) => s + i.quantityOrdered, 0); }
  received(p: PurchaseOrder) { return p.items.reduce((s, i) => s + i.quantityReceived, 0); }
  units(g: GoodsReceipt) { return g.items.reduce((s, i) => s + i.quantity, 0); }
  chip(s: string) { return s === 'Received' ? 'ok' : s === 'Cancelled' ? 'bad' : s === 'Draft' ? '' : 'info'; }
  open(mode: 'po' | 'grn') { this.dialog.open(PoDialogComponent, { width: '820px', data: { mode } }).afterClosed().subscribe(ok => { if (ok) { this.loadOrders(); this.loadReceipts(); } }); }
  submit(p: PurchaseOrder) { this.api.submit(p.id).subscribe({ next: () => this.loadOrders(), error: e => this.notify.error(e) }); }
  receive(p: PurchaseOrder) { this.dialog.open(ReceiveDialogComponent, { width: '820px', data: p }).afterClosed().subscribe(ok => { if (ok) { this.loadOrders(); this.loadReceipts(); } }); }
  cancel(p: PurchaseOrder) {
    this.dialog.open(ConfirmDialogComponent, { data: { title: 'Cancel purchase order', message: `Cancel ${p.poNumber}?`, confirmText: 'Cancel order', danger: true } })
      .afterClosed().subscribe(ok => { if (ok) this.api.cancel(p.id).subscribe({ next: () => this.loadOrders(), error: e => this.notify.error(e) }); });
  }
}
