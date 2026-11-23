import { Component, ElementRef, ViewChild, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of, catchError } from 'rxjs';
import { MatDialog } from '@angular/material/dialog';
import { MATERIAL, InrPipe, PageComponent } from '../../shared/shared';
import { CatalogApi, PartiesApi, SalesApi, newIdempotencyKey as _k } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { AuthService } from '../../core/auth/auth.service';
import { BookLookup, Customer, DiscountType, Invoice, PaymentMethod } from '../../core/models';
import { CustomerDialogComponent } from '../customers/customer-dialog.component';

interface CartLine { book: BookLookup; quantity: number; unitPrice: number; discountPercent: number; }

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent],
  template: `
    <bf-page title="Point of sale" subtitle="Scan a barcode or type a title / ISBN, then press Enter">
      <div actions>
        <button mat-stroked-button (click)="clear()" [disabled]="!cart().length"><mat-icon>delete_sweep</mat-icon> Clear cart</button>
      </div>
    </bf-page>

    <div class="pos">
      <section class="left">
        <mat-form-field class="w-full search">
          <mat-icon matPrefix>search</mat-icon>
          <mat-label>Scan or search books</mat-label>
          <input #searchBox matInput [matAutocomplete]="auto" (input)="onSearch(searchBox.value)" (keydown.enter)="onEnter(searchBox.value); $event.preventDefault()" autocomplete="off" autofocus />
          <mat-autocomplete #auto="matAutocomplete" (optionSelected)="add($event.option.value); searchBox.value=''; results.set([])">
            @for (r of results(); track r.id) {
              <mat-option [value]="r">
                <div class="opt"><span class="t">{{ r.title }}</span><span class="muted">{{ r.authorName }} {{ r.isbn ? '· ' + r.isbn : '' }}</span>
                <span class="right">{{ r.sellingPrice | inr }} <span [class]="'chip ' + (r.quantityOnHand > 0 ? 'ok' : 'bad')">{{ r.quantityOnHand }} in stock</span></span></div>
              </mat-option>
            }
          </mat-autocomplete>
        </mat-form-field>

        <div class="table-card">
          <table mat-table [dataSource]="cart()">
            <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Item</th>
              <td mat-cell *matCellDef="let l"><div>{{ l.book.title }}</div><div class="muted small">{{ l.book.isbn || l.book.barcode || '' }} · stock {{ l.book.quantityOnHand }} · GST {{ l.book.gstRate }}%</div></td></ng-container>
            <ng-container matColumnDef="qty"><th mat-header-cell *matHeaderCellDef class="num">Qty</th>
              <td mat-cell *matCellDef="let l" class="num qty">
                <button mat-icon-button (click)="setQty(l, l.quantity - 1)" aria-label="Decrease"><mat-icon>remove</mat-icon></button>
                <input type="number" min="1" [value]="l.quantity" (change)="setQty(l, +$any($event.target).value)" class="qty-input" />
                <button mat-icon-button (click)="setQty(l, l.quantity + 1)" aria-label="Increase"><mat-icon>add</mat-icon></button>
              </td></ng-container>
            <ng-container matColumnDef="price"><th mat-header-cell *matHeaderCellDef class="num">Price</th>
              <td mat-cell *matCellDef="let l" class="num"><input type="number" min="0" step="0.5" [value]="l.unitPrice" (change)="setPrice(l, +$any($event.target).value)" class="price-input" [disabled]="!auth.isManager" /></td></ng-container>
            <ng-container matColumnDef="disc"><th mat-header-cell *matHeaderCellDef class="num">Disc %</th>
              <td mat-cell *matCellDef="let l" class="num"><input type="number" min="0" max="100" [value]="l.discountPercent" (change)="setDisc(l, +$any($event.target).value)" class="qty-input" /></td></ng-container>
            <ng-container matColumnDef="total"><th mat-header-cell *matHeaderCellDef class="num">Total</th><td mat-cell *matCellDef="let l" class="num">{{ lineTotal(l) | inr }}</td></ng-container>
            <ng-container matColumnDef="x"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let l"><button mat-icon-button (click)="remove(l)" aria-label="Remove"><mat-icon>close</mat-icon></button></td></ng-container>
            <tr mat-header-row *matHeaderRowDef="cols"></tr>
            <tr mat-row *matRowDef="let row; columns: cols"></tr>
          </table>
          @if (!cart().length) { <div class="empty">Cart is empty. Scan a barcode to begin.</div> }
        </div>
      </section>

      <aside class="right card">
        <h3>Customer</h3>
        <mat-form-field class="w-full">
          <mat-label>Walk-in or search customer</mat-label>
          <input matInput [matAutocomplete]="custAuto" (input)="onCustomerSearch($any($event.target).value)" [value]="customer()?.name || ''" />
          <mat-autocomplete #custAuto="matAutocomplete" (optionSelected)="customer.set($event.option.value)" [displayWith]="displayCustomer">
            @for (c of customers(); track c.id) { <mat-option [value]="c">{{ c.name }} <span class="muted">{{ c.phone }}</span></mat-option> }
          </mat-autocomplete>
          @if (customer()) { <button matSuffix mat-icon-button (click)="customer.set(null)" aria-label="Clear"><mat-icon>close</mat-icon></button> }
        </mat-form-field>
        <div class="row-actions"><button mat-button (click)="newCustomer()"><mat-icon>person_add</mat-icon> New customer</button>
          @if (customer()?.outstanding) { <span class="chip warn">Dues {{ customer()!.outstanding | inr }}</span> }</div>

        <h3>Discount</h3>
        <div class="disc-row">
          <mat-form-field><mat-label>Type</mat-label><mat-select [value]="discountType()" (valueChange)="discountType.set($event)"><mat-option value="None">None</mat-option><mat-option value="Percentage">%</mat-option><mat-option value="Fixed">₹</mat-option></mat-select></mat-form-field>
          <mat-form-field><mat-label>Value</mat-label><input matInput type="number" min="0" [value]="discountValue()" (input)="discountValue.set(+$any($event.target).value || 0)" [disabled]="discountType() === 'None'" /></mat-form-field>
        </div>

        <div class="totals">
          <div><span>Items</span><span>{{ itemCount() }}</span></div>
          <div><span>Sub total</span><span>{{ totals().subTotal | inr }}</span></div>
          <div><span>Discount</span><span>- {{ totals().discount | inr }}</span></div>
          <div><span>GST</span><span>{{ totals().tax | inr }}</span></div>
          <div class="grand"><span>Total</span><span>{{ totals().grand | inr }}</span></div>
          <p class="muted small">Final GST split and rounding are computed by the server on the invoice.</p>
        </div>

        <h3>Payment</h3>
        <mat-form-field class="w-full"><mat-label>Method</mat-label>
          <mat-select [value]="method()" (valueChange)="method.set($event)">
            <mat-option value="Cash">Cash</mat-option><mat-option value="Upi">UPI</mat-option><mat-option value="Card">Card</mat-option><mat-option value="BankTransfer">Bank transfer</mat-option>
            <mat-option value="Credit" [disabled]="!customer()">Credit (pay later)</mat-option>
          </mat-select></mat-form-field>
        @if (method() !== 'Credit') {
          <mat-form-field class="w-full"><mat-label>Amount received</mat-label><input matInput type="number" min="0" [value]="received()" (input)="received.set(+$any($event.target).value || 0)" /></mat-form-field>
          @if (received() > totals().grand) { <div class="change">Change: {{ received() - totals().grand | inr }}</div> }
          @if (received() < totals().grand && !customer()) { <div class="muted small">Walk-in customers must pay in full.</div> }
          <div class="quick"><button mat-stroked-button (click)="received.set(totals().grand)">Exact</button>@for (n of [100,200,500,2000]; track n) { <button mat-stroked-button (click)="received.set(n)">₹{{ n }}</button> }</div>
          <mat-form-field class="w-full"><mat-label>Reference (UPI / card txn)</mat-label><input matInput [value]="reference()" (input)="reference.set($any($event.target).value)" /></mat-form-field>
        }
        <button mat-flat-button color="primary" class="w-full pay" (click)="checkout()" [disabled]="!cart().length || submitting() || !canSubmit()">
          @if (submitting()) { Saving… } @else { <span><mat-icon>check_circle</mat-icon> Complete sale {{ totals().grand | inr }}</span> }
        </button>
        <p class="muted small center">Shortcut: F2 focuses search, F9 completes the sale</p>
      </aside>
    </div>`,
  styles: [`
    .pos { display: grid; grid-template-columns: 1fr 360px; gap: 16px; align-items: start; }
    .left .search { margin-bottom: 8px; }
    .opt { display: flex; gap: 8px; align-items: center; width: 100%; } .opt .t { font-weight: 500; } .opt .right { margin-left: auto; white-space: nowrap; }
    .small { font-size: 12px; } .center { text-align: center; }
    .qty { white-space: nowrap; } .qty-input { width: 48px; text-align: center; padding: 6px 4px; border: 1px solid #d0d3d8; border-radius: 6px; }
    .price-input { width: 84px; text-align: right; padding: 6px; border: 1px solid #d0d3d8; border-radius: 6px; }
    .right h3 { margin: 12px 0 8px; font-size: 14px; font-weight: 500; color: #5f6368; text-transform: uppercase; letter-spacing: .04em; } .right h3:first-child { margin-top: 0; }
    .row-actions { display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px; }
    .disc-row { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .totals > div { display: flex; justify-content: space-between; padding: 4px 0; } .totals .grand { font-size: 22px; font-weight: 600; border-top: 1px solid #e3e5e8; margin-top: 4px; padding-top: 8px; }
    .change { font-size: 18px; font-weight: 500; color: #1b6e3a; margin-bottom: 8px; }
    .quick { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 12px; } .quick button { min-width: 0; padding: 0 10px; }
    .pay { height: 52px; font-size: 16px; margin-top: 8px; } .pay mat-icon { margin-right: 6px; }
    @media (max-width: 1000px) { .pos { grid-template-columns: 1fr; } }
  `],
  host: { '(document:keydown.f2)': 'focusSearch($event)', '(document:keydown.f9)': 'checkout(); $event.preventDefault()' },
})
export class PosComponent {
  readonly auth = inject(AuthService);
  private catalog = inject(CatalogApi);
  private parties = inject(PartiesApi);
  private sales = inject(SalesApi);
  private notify = inject(NotifyService);
  private dialog = inject(MatDialog);
  private router = inject(Router);
  @ViewChild('searchBox') searchBox?: ElementRef<HTMLInputElement>;

  cols = ['title', 'qty', 'price', 'disc', 'total', 'x'];
  cart = signal<CartLine[]>([]);
  results = signal<BookLookup[]>([]);
  customers = signal<Customer[]>([]);
  customer = signal<Customer | null>(null);
  discountType = signal<DiscountType>('None');
  discountValue = signal(0);
  method = signal<PaymentMethod>('Cash');
  received = signal(0);
  reference = signal('');
  submitting = signal(false);
  private idempotencyKey = _k();
  private search$ = new Subject<string>();
  private custSearch$ = new Subject<string>();

  itemCount = computed(() => this.cart().reduce((s, l) => s + l.quantity, 0));
  totals = computed(() => {
    const lines = this.cart();
    const subTotal = lines.reduce((s, l) => s + l.quantity * l.unitPrice, 0);
    const lineDisc = lines.reduce((s, l) => s + this.r2(l.quantity * l.unitPrice * l.discountPercent / 100), 0);
    const afterLine = subTotal - lineDisc;
    const invDisc = this.discountType() === 'Percentage' ? this.r2(afterLine * Math.min(100, this.discountValue()) / 100) : this.discountType() === 'Fixed' ? Math.min(afterLine, this.discountValue()) : 0;
    const taxable = afterLine - invDisc;
    const tax = lines.reduce((s, l) => {
      const lt = l.quantity * l.unitPrice - this.r2(l.quantity * l.unitPrice * l.discountPercent / 100);
      const share = afterLine > 0 ? lt / afterLine * invDisc : 0;
      return s + this.r2((lt - share) * l.book.gstRate / 100);
    }, 0);
    const grand = Math.round(taxable + tax);
    return { subTotal, discount: lineDisc + invDisc, taxable, tax, grand };
  });
  canSubmit = computed(() => this.method() === 'Credit' ? !!this.customer() : (this.received() >= this.totals().grand || !!this.customer()));

  constructor() {
    this.search$.pipe(debounceTime(200), distinctUntilChanged(), switchMap(t => t.length < 2 ? of([]) : this.catalog.quickSearch(t, 12).pipe(catchError(() => of([])))))
      .subscribe(r => this.results.set(r));
    this.custSearch$.pipe(debounceTime(250), distinctUntilChanged(), switchMap(t => t.length < 2 ? of(null) : this.parties.customers({ search: t, pageSize: 8 }).pipe(catchError(() => of(null)))))
      .subscribe(r => this.customers.set(r?.items ?? []));
  }

  private r2(n: number) { return Math.round(n * 100) / 100; }
  displayCustomer = (c: Customer | null) => c?.name ?? '';
  onSearch(t: string) { this.search$.next(t.trim()); }
  onCustomerSearch(t: string) { this.custSearch$.next(t.trim()); }
  focusSearch(e: Event) { e.preventDefault(); this.searchBox?.nativeElement.focus(); }

  /** Enter in the search box = barcode scanner path: exact lookup first, else first suggestion. */
  onEnter(code: string) {
    const c = code.trim(); if (!c) return;
    this.catalog.lookup(c).subscribe({
      next: b => { this.add(b); this.resetSearch(); },
      error: () => { const first = this.results()[0]; if (first) { this.add(first); this.resetSearch(); } else this.notify.info(`No book found for "${c}"`); },
    });
  }
  private resetSearch() { if (this.searchBox) this.searchBox.nativeElement.value = ''; this.results.set([]); }

  add(b: BookLookup) {
    if (!b) return;
    this.idempotencyKey = _k();
    this.cart.update(c => {
      const ex = c.find(l => l.book.id === b.id);
      if (ex) return c.map(l => l === ex ? { ...l, quantity: l.quantity + 1 } : l);
      return [...c, { book: b, quantity: 1, unitPrice: b.sellingPrice, discountPercent: 0 }];
    });
    if (b.quantityOnHand <= 0) this.notify.info(`"${b.title}" shows no stock — the sale will be rejected unless stock is received.`);
    this.received.set(0);
  }
  setQty(l: CartLine, q: number) { this.idempotencyKey = _k(); if (!q || q < 1) return this.remove(l); this.cart.update(c => c.map(x => x === l ? { ...x, quantity: Math.floor(q) } : x)); }
  setPrice(l: CartLine, p: number) { this.idempotencyKey = _k(); if (p < 0 || isNaN(p)) return; this.cart.update(c => c.map(x => x === l ? { ...x, unitPrice: p } : x)); }
  setDisc(l: CartLine, d: number) { this.idempotencyKey = _k(); d = Math.min(100, Math.max(0, d || 0)); this.cart.update(c => c.map(x => x === l ? { ...x, discountPercent: d } : x)); }
  remove(l: CartLine) { this.idempotencyKey = _k(); this.cart.update(c => c.filter(x => x !== l)); }
  lineTotal(l: CartLine) { const g = l.quantity * l.unitPrice; return g - this.r2(g * l.discountPercent / 100); }
  clear() { this.cart.set([]); this.customer.set(null); this.discountType.set('None'); this.discountValue.set(0); this.received.set(0); this.reference.set(''); this.idempotencyKey = _k(); this.focusSearch(new Event('x')); }

  newCustomer() {
    this.dialog.open(CustomerDialogComponent, { width: '520px', data: null }).afterClosed().subscribe((c: Customer | undefined) => { if (c) this.customer.set(c); });
  }

  checkout() {
    if (!this.cart().length || this.submitting() || !this.canSubmit()) return;
    const t = this.totals();
    const payAmount = this.method() === 'Credit' ? 0 : Math.min(this.received(), t.grand);
    const body = {
      customerId: this.customer()?.id ?? null, customerName: this.customer()?.name ?? null,
      discountType: this.discountType(), discountValue: this.discountType() === 'None' ? 0 : this.discountValue(),
      lines: this.cart().map(l => ({ bookId: l.book.id, quantity: l.quantity, unitPrice: l.unitPrice, discountPercent: l.discountPercent })),
      payment: payAmount > 0 ? { method: this.method(), amount: payAmount, reference: this.reference() || null } : null,
    };
    this.submitting.set(true);
    this.sales.createInvoice(body, this.idempotencyKey).subscribe({
      next: (inv: Invoice) => {
        this.submitting.set(false);
        this.notify.success(`Invoice ${inv.invoiceNumber} saved`);
        this.clear();
        this.router.navigate(['/invoices', inv.id], { queryParams: { print: 1 } });
      },
      error: e => {
        this.submitting.set(false);
        // Keep the same idempotency key for a retry of the same cart; new key only after an explicit cart change.
        const ids: string[] = e?.error?.bookIds ?? [];
        if (ids.length) {
          const titles = this.cart().filter(l => ids.includes(l.book.id)).map(l => l.book.title).join(', ');
          this.notify.error(e, `Not enough stock for: ${titles}`);
        } else this.notify.error(e);
      },
    });
  }
}
