import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { PageEvent } from '@angular/material/paginator';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of, catchError } from 'rxjs';
import { MATERIAL, InrPipe, PageComponent, daysAgo, today } from '../../shared/shared';
import { CatalogApi, InventoryApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { AuthService } from '../../core/auth/auth.service';
import { BookLookup, Movement, PagedResult, StockRow, Valuation } from '../../core/models';

interface AdjLine { book: BookLookup; quantityDelta: number; condition: 'Resalable' | 'Damaged'; }

@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>Stock adjustment</h2>
    <mat-dialog-content>
      <p class="muted">Use positive quantities for stock found, negative for stock lost. Choose "Damaged" to move copies out of sellable stock into the damaged bucket.</p>
      <mat-form-field class="w-full"><mat-label>Reason</mat-label><mat-select [(ngModel)]="reason"><mat-option value="Physical count correction">Physical count correction</mat-option><mat-option value="Damaged in store">Damaged in store</mat-option><mat-option value="Lost / theft">Lost / theft</mat-option><mat-option value="Found stock">Found stock</mat-option><mat-option value="Other">Other</mat-option></mat-select></mat-form-field>
      <mat-form-field class="w-full"><mat-label>Add book</mat-label><input matInput [matAutocomplete]="auto" (input)="search$.next($any($event.target).value)" #inp />
        <mat-autocomplete #auto="matAutocomplete" (optionSelected)="add($event.option.value); inp.value=''">@for (r of results(); track r.id) { <mat-option [value]="r">{{ r.title }} <span class="muted">(stock {{ r.quantityOnHand }})</span></mat-option> }</mat-autocomplete></mat-form-field>
      <table mat-table [dataSource]="lines()">
        <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Book</th><td mat-cell *matCellDef="let l">{{ l.book.title }} <span class="muted">stock {{ l.book.quantityOnHand }}</span></td></ng-container>
        <ng-container matColumnDef="qty"><th mat-header-cell *matHeaderCellDef>Qty (+/-)</th><td mat-cell *matCellDef="let l"><input type="number" [(ngModel)]="l.quantityDelta" style="width:80px" /></td></ng-container>
        <ng-container matColumnDef="cond"><th mat-header-cell *matHeaderCellDef>Condition</th><td mat-cell *matCellDef="let l"><mat-select [(ngModel)]="l.condition" style="width:120px"><mat-option value="Resalable">Resalable</mat-option><mat-option value="Damaged">Damaged</mat-option></mat-select></td></ng-container>
        <ng-container matColumnDef="x"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let l"><button mat-icon-button (click)="removeLine(l)"><mat-icon>close</mat-icon></button></td></ng-container>
        <tr mat-header-row *matHeaderRowDef="['title','qty','cond','x']"></tr><tr mat-row *matRowDef="let row; columns: ['title','qty','cond','x']"></tr>
      </table>
      <mat-form-field class="w-full" style="margin-top:12px"><mat-label>Notes</mat-label><input matInput [(ngModel)]="notes" /></mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button color="primary" (click)="save()" [disabled]="!lines().length || busy()">Post adjustment</button></mat-dialog-actions>`,
})
export class AdjustmentDialogComponent {
  private catalog = inject(CatalogApi); private api = inject(InventoryApi); private notify = inject(NotifyService);
  lines = signal<AdjLine[]>([]); results = signal<BookLookup[]>([]); reason = 'Physical count correction'; notes = ''; busy = signal(false);
  search$ = new Subject<string>();
  constructor(private ref: MatDialogRef<AdjustmentDialogComponent>) {
    this.search$.pipe(debounceTime(200), distinctUntilChanged(), switchMap(t => t.length < 2 ? of([]) : this.catalog.quickSearch(t).pipe(catchError(() => of([]))))).subscribe(r => this.results.set(r));
  }
  add(b: BookLookup) { if (b && !this.lines().some(l => l.book.id === b.id)) this.lines.update(l => [...l, { book: b, quantityDelta: -1, condition: 'Resalable' }]); }
  removeLine(l: AdjLine) { this.lines.set(this.lines().filter(x => x !== l)); }
  save() {
    const bad = this.lines().find(l => !l.quantityDelta || (l.condition === 'Damaged' && l.quantityDelta > 0));
    if (bad) { this.notify.info(bad.quantityDelta ? 'Damaged quantities must be negative.' : 'Quantity cannot be zero.'); return; }
    this.busy.set(true);
    this.api.adjust({ reason: this.reason, notes: this.notes || null, lines: this.lines().map(l => ({ bookId: l.book.id, quantityDelta: l.quantityDelta, condition: l.condition })) })
      .subscribe({ next: () => { this.notify.success('Adjustment posted'); this.ref.close(true); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
}

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent],
  template: `
    <bf-page title="Inventory" subtitle="Stock on hand, valuation and the movement ledger">
      <div actions>@if (auth.canManageInventory) { <button mat-flat-button color="primary" (click)="adjust()"><mat-icon>tune</mat-icon> Stock adjustment</button> }</div>
    </bf-page>
    @if (auth.isManager && valuation(); as v) {
      <div class="kpis">
        <div class="kpi"><div class="label">Titles in stock</div><div class="value">{{ v.distinctBooks }}</div></div>
        <div class="kpi"><div class="label">Units on hand</div><div class="value">{{ v.totalUnits }}</div><div class="sub">{{ v.damagedUnits }} damaged</div></div>
        <div class="kpi"><div class="label">Value at cost</div><div class="value">{{ v.valueAtCost | inr }}</div></div>
        <div class="kpi"><div class="label">Value at selling price</div><div class="value">{{ v.valueAtSellingPrice | inr }}</div></div>
      </div>
    }
    <mat-tab-group (selectedIndexChange)="tab.set($event)" [selectedIndex]="tab()">
      <mat-tab label="Stock">
        <div class="toolbar-row" style="margin-top:12px">
          <mat-form-field><mat-label>Search</mat-label><input matInput (input)="search$.next($any($event.target).value)" /></mat-form-field>
          <mat-checkbox [checked]="lowOnly()" (change)="lowOnly.set($event.checked); pageIndex = 0; loadStock()">Low stock only</mat-checkbox>
        </div>
        <div class="table-card">
          <table mat-table [dataSource]="stock()?.items ?? []">
            <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Book</th><td mat-cell *matCellDef="let r"><div>{{ r.title }}</div><div class="muted mono">{{ r.isbn || r.barcode }}</div></td></ng-container>
            <ng-container matColumnDef="cat"><th mat-header-cell *matHeaderCellDef>Category</th><td mat-cell *matCellDef="let r">{{ r.categoryName }}</td></ng-container>
            <ng-container matColumnDef="onhand"><th mat-header-cell *matHeaderCellDef class="num">On hand</th><td mat-cell *matCellDef="let r" class="num"><span [class]="'chip ' + (r.quantityOnHand <= 0 ? 'bad' : r.isLow ? 'warn' : 'ok')">{{ r.quantityOnHand }}</span></td></ng-container>
            <ng-container matColumnDef="damaged"><th mat-header-cell *matHeaderCellDef class="num">Damaged</th><td mat-cell *matCellDef="let r" class="num">{{ r.quantityDamaged || '' }}</td></ng-container>
            <ng-container matColumnDef="reorder"><th mat-header-cell *matHeaderCellDef class="num">Reorder at</th><td mat-cell *matCellDef="let r" class="num">{{ r.reorderLevel }}</td></ng-container>
            <ng-container matColumnDef="cost"><th mat-header-cell *matHeaderCellDef class="num">Avg cost</th><td mat-cell *matCellDef="let r" class="num">{{ r.averageCost | inr }}</td></ng-container>
            <ng-container matColumnDef="value"><th mat-header-cell *matHeaderCellDef class="num">Stock value</th><td mat-cell *matCellDef="let r" class="num">{{ r.stockValue | inr }}</td></ng-container>
            <ng-container matColumnDef="ledger"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let r"><button mat-icon-button matTooltip="Movement history" (click)="showMovements(r)"><mat-icon>history</mat-icon></button></td></ng-container>
            <tr mat-header-row *matHeaderRowDef="stockCols"></tr><tr mat-row *matRowDef="let row; columns: stockCols"></tr>
          </table>
          @if (stock() && !stock()!.items.length) { <div class="empty">No stock rows.</div> }
          <mat-paginator [length]="stock()?.totalCount ?? 0" [pageSize]="pageSize" [pageIndex]="pageIndex" [pageSizeOptions]="[25, 50, 100]" (page)="onPage($event)"></mat-paginator>
        </div>
      </mat-tab>
      <mat-tab label="Movements">
        <div class="toolbar-row" style="margin-top:12px">
          <mat-form-field><mat-label>From</mat-label><input matInput type="date" [value]="from" (change)="from = $any($event.target).value; loadMovements()" /></mat-form-field>
          <mat-form-field><mat-label>To</mat-label><input matInput type="date" [value]="to" (change)="to = $any($event.target).value; loadMovements()" /></mat-form-field>
          <mat-form-field><mat-label>Type</mat-label><mat-select [value]="type" (valueChange)="type = $event; loadMovements()"><mat-option [value]="null">All</mat-option>@for (t of types; track t) { <mat-option [value]="t">{{ t }}</mat-option> }</mat-select></mat-form-field>
          @if (bookFilter()) { <span class="chip info">{{ bookFilter()!.title }} <button mat-icon-button (click)="bookFilter.set(null); loadMovements()" style="width:24px;height:24px;line-height:24px"><mat-icon style="font-size:16px">close</mat-icon></button></span> }
        </div>
        <div class="table-card">
          <table mat-table [dataSource]="movements()?.items ?? []">
            <ng-container matColumnDef="when"><th mat-header-cell *matHeaderCellDef>When</th><td mat-cell *matCellDef="let m">{{ m.occurredAt | date:'d MMM yyyy, HH:mm' }}</td></ng-container>
            <ng-container matColumnDef="book"><th mat-header-cell *matHeaderCellDef>Book</th><td mat-cell *matCellDef="let m">{{ m.title }}</td></ng-container>
            <ng-container matColumnDef="type"><th mat-header-cell *matHeaderCellDef>Type</th><td mat-cell *matCellDef="let m"><span class="chip">{{ m.movementType }}</span></td></ng-container>
            <ng-container matColumnDef="qty"><th mat-header-cell *matHeaderCellDef class="num">Qty</th><td mat-cell *matCellDef="let m" class="num" [style.color]="m.quantity < 0 ? '#a6192e' : '#1b6e3a'">{{ m.quantity > 0 ? '+' : '' }}{{ m.quantity }}</td></ng-container>
            <ng-container matColumnDef="ref"><th mat-header-cell *matHeaderCellDef>Reference</th><td mat-cell *matCellDef="let m">{{ m.referenceNumber || m.referenceType }} <span class="muted">{{ m.notes }}</span></td></ng-container>
            <tr mat-header-row *matHeaderRowDef="movCols"></tr><tr mat-row *matRowDef="let row; columns: movCols"></tr>
          </table>
          @if (movements() && !movements()!.items.length) { <div class="empty">No movements in this range.</div> }
          <mat-paginator [length]="movements()?.totalCount ?? 0" [pageSize]="movPageSize" [pageIndex]="movPageIndex" [pageSizeOptions]="[25, 50, 100]" (page)="onMovPage($event)"></mat-paginator>
        </div>
      </mat-tab>
    </mat-tab-group>`,
})
export class InventoryComponent {
  readonly auth = inject(AuthService);
  private api = inject(InventoryApi); private notify = inject(NotifyService); private dialog = inject(MatDialog); private route = inject(ActivatedRoute);
  stockCols = ['title', 'cat', 'onhand', 'damaged', 'reorder', 'cost', 'value', 'ledger'];
  movCols = ['when', 'book', 'type', 'qty', 'ref'];
  types = ['OpeningStock', 'Purchase', 'Sale', 'SalesReturn', 'PurchaseReturn', 'Damaged', 'Adjustment', 'SaleCancellation'];
  stock = signal<PagedResult<StockRow> | null>(null); movements = signal<PagedResult<Movement> | null>(null); valuation = signal<Valuation | null>(null);
  lowOnly = signal(false); tab = signal(0); bookFilter = signal<StockRow | null>(null);
  search = ''; pageIndex = 0; pageSize = 25; movPageIndex = 0; movPageSize = 50; from = daysAgo(30); to = today(); type: string | null = null;
  search$ = new Subject<string>();

  constructor() {
    this.lowOnly.set(this.route.snapshot.queryParamMap.get('low') === '1');
    this.search$.pipe(debounceTime(300), distinctUntilChanged()).subscribe(s => { this.search = s; this.pageIndex = 0; this.loadStock(); });
    this.loadStock(); this.loadMovements();
    if (this.auth.isManager) this.api.valuation().subscribe({ next: v => this.valuation.set(v), error: () => { } });
  }
  loadStock() { this.api.stock({ search: this.search, lowStock: this.lowOnly(), page: this.pageIndex + 1, pageSize: this.pageSize }).subscribe({ next: p => this.stock.set(p), error: e => this.notify.error(e) }); }
  loadMovements() { this.api.movements({ bookId: this.bookFilter()?.bookId, from: this.from, to: this.to, type: this.type, page: this.movPageIndex + 1, pageSize: this.movPageSize }).subscribe({ next: p => this.movements.set(p), error: e => this.notify.error(e) }); }
  onPage(e: PageEvent) { this.pageIndex = e.pageIndex; this.pageSize = e.pageSize; this.loadStock(); }
  onMovPage(e: PageEvent) { this.movPageIndex = e.pageIndex; this.movPageSize = e.pageSize; this.loadMovements(); }
  showMovements(r: StockRow) { this.bookFilter.set(r); this.from = daysAgo(365); this.movPageIndex = 0; this.loadMovements(); this.tab.set(1); }
  adjust() { this.dialog.open(AdjustmentDialogComponent, { width: '720px' }).afterClosed().subscribe(ok => { if (ok) { this.loadStock(); this.loadMovements(); if (this.auth.isManager) this.api.valuation().subscribe(v => this.valuation.set(v)); } }); }
}
