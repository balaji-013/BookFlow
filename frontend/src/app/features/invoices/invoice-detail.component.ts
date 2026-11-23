import { Component, Inject, Input, OnInit, inject, signal } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MATERIAL, InrPipe, PageComponent, ConfirmDialogComponent } from '../../shared/shared';
import { SalesApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { AuthService } from '../../core/auth/auth.service';
import { Invoice, RefundMode, ReturnCondition } from '../../core/models';

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe],
  template: `
    <h2 mat-dialog-title>Sales return — {{ inv.invoiceNumber }}</h2>
    <mat-dialog-content>
      <table mat-table [dataSource]="rows">
        <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Item</th><td mat-cell *matCellDef="let r">{{ r.item.title }}<div class="muted">sold {{ r.item.quantity }}, returned {{ r.item.quantityReturned }}</div></td></ng-container>
        <ng-container matColumnDef="qty"><th mat-header-cell *matHeaderCellDef>Return qty</th><td mat-cell *matCellDef="let r"><input type="number" min="0" [max]="r.max" [(ngModel)]="r.qty" style="width:70px" [disabled]="r.max === 0" /></td></ng-container>
        <ng-container matColumnDef="cond"><th mat-header-cell *matHeaderCellDef>Condition</th><td mat-cell *matCellDef="let r"><mat-select [(ngModel)]="r.condition" style="width:130px"><mat-option value="Resalable">Resalable</mat-option><mat-option value="Damaged">Damaged</mat-option></mat-select></td></ng-container>
        <ng-container matColumnDef="amt"><th mat-header-cell *matHeaderCellDef class="num">Refund</th><td mat-cell *matCellDef="let r" class="num">{{ r.qty * r.item.lineTotal / r.item.quantity | inr }}</td></ng-container>
        <tr mat-header-row *matHeaderRowDef="['title','qty','cond','amt']"></tr><tr mat-row *matRowDef="let row; columns: ['title','qty','cond','amt']"></tr>
      </table>
      <div class="form-grid" style="margin-top:12px">
        <mat-form-field><mat-label>Refund as</mat-label><mat-select [(ngModel)]="refundMode"><mat-option value="Cash">Cash refund</mat-option><mat-option value="CreditNote" [disabled]="!inv.customerId">Credit note (named customer only)</mat-option><mat-option value="AdjustAgainstDue" [disabled]="!inv.amountDue">Adjust against amount due</mat-option></mat-select></mat-form-field>
        <mat-form-field><mat-label>Reason</mat-label><input matInput [(ngModel)]="reason" /></mat-form-field>
      </div>
      <p class="num"><strong>Total refund {{ total() | inr }}</strong></p>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button color="primary" (click)="save()" [disabled]="total() <= 0 || busy()">Record return</button></mat-dialog-actions>`,
  styles: ['.form-grid { grid-template-columns: 1fr 1fr; }'],
})
export class ReturnDialogComponent {
  readonly inv = inject<Invoice>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ReturnDialogComponent>>(MatDialogRef);
  private api = inject(SalesApi); private notify = inject(NotifyService);
  rows = this.inv.items.map(item => ({ item, max: item.quantity - item.quantityReturned, qty: 0, condition: 'Resalable' as ReturnCondition }));
  refundMode: RefundMode = 'Cash'; reason = ''; busy = signal(false);
    total() { return this.rows.reduce((s, r) => s + (r.qty > 0 ? r.qty * r.item.lineTotal / r.item.quantity : 0), 0); }
  save() {
    if (this.rows.some(r => r.qty < 0 || r.qty > r.max)) { this.notify.info('Return quantity exceeds what was sold.'); return; }
    this.busy.set(true);
    const lines = this.rows.filter(r => r.qty > 0).map(r => ({ salesInvoiceItemId: r.item.id, quantity: r.qty, condition: r.condition }));
    this.api.createReturn({ salesInvoiceId: this.inv.id, reason: this.reason || null, refundMode: this.refundMode, lines })
      .subscribe({ next: r => { this.notify.success(`Return ${r.returnNumber} recorded`); this.ref.close(true); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
}

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent, RouterLink],
  template: `
    @if (inv(); as inv) {
      <bf-page [title]="inv.invoiceNumber" [subtitle]="inv.customerName + ' · ' + (inv.invoiceDate | date:'d MMM yyyy')">
        <div actions>
          <a mat-button routerLink="/invoices"><mat-icon>arrow_back</mat-icon> All invoices</a>
          <button mat-stroked-button (click)="print()"><mat-icon>print</mat-icon> Print</button>
          @if (auth.canBill && inv.status !== 'Cancelled' && inv.status !== 'Returned') { <button mat-stroked-button (click)="returnItems()"><mat-icon>assignment_return</mat-icon> Return items</button> }
          @if (auth.isManager && inv.status === 'Issued') { <button mat-stroked-button color="warn" (click)="cancel()"><mat-icon>cancel</mat-icon> Cancel invoice</button> }
        </div>
      </bf-page>
      <div class="chips">
        <span [class]="'chip ' + (inv.status === 'Issued' ? 'info' : inv.status === 'Cancelled' ? 'bad' : 'warn')">{{ inv.status }}</span>
        <span [class]="'chip ' + (inv.paymentStatus === 'Paid' ? 'ok' : inv.paymentStatus === 'Partial' ? 'warn' : 'bad')">{{ inv.paymentStatus }}</span>
        @if (inv.isInterState) { <span class="chip">IGST</span> }
        @if (inv.cancelReason) { <span class="muted">Cancelled: {{ inv.cancelReason }}</span> }
      </div>
      <div class="grid-2">
        <section class="card">
          <table mat-table [dataSource]="inv.items">
            <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Item</th><td mat-cell *matCellDef="let i">{{ i.title }}<div class="muted">{{ i.isbn }} · HSN {{ i.hsnCode }}{{ i.quantityReturned ? ' · returned ' + i.quantityReturned : '' }}</div></td></ng-container>
            <ng-container matColumnDef="qty"><th mat-header-cell *matHeaderCellDef class="num">Qty</th><td mat-cell *matCellDef="let i" class="num">{{ i.quantity }}</td></ng-container>
            <ng-container matColumnDef="price"><th mat-header-cell *matHeaderCellDef class="num">Rate</th><td mat-cell *matCellDef="let i" class="num">{{ i.unitPrice | inr }}</td></ng-container>
            <ng-container matColumnDef="disc"><th mat-header-cell *matHeaderCellDef class="num">Disc</th><td mat-cell *matCellDef="let i" class="num">{{ i.discountAmount ? (i.discountAmount | inr) : '—' }}</td></ng-container>
            <ng-container matColumnDef="gst"><th mat-header-cell *matHeaderCellDef class="num">GST</th><td mat-cell *matCellDef="let i" class="num">{{ i.gstRate }}%<div class="muted">{{ i.cgstAmount + i.sgstAmount + i.igstAmount | inr }}</div></td></ng-container>
            <ng-container matColumnDef="total"><th mat-header-cell *matHeaderCellDef class="num">Total</th><td mat-cell *matCellDef="let i" class="num">{{ i.lineTotal | inr }}</td></ng-container>
            <tr mat-header-row *matHeaderRowDef="cols"></tr><tr mat-row *matRowDef="let row; columns: cols"></tr>
          </table>
        </section>
        <aside>
          <section class="card totals">
            <div><span>Sub total</span><span>{{ inv.subTotal | inr }}</span></div>
            <div><span>Discount</span><span>- {{ inv.discountTotal | inr }}</span></div>
            <div><span>Taxable</span><span>{{ inv.taxableTotal | inr }}</span></div>
            @if (inv.cgstTotal) { <div><span>CGST</span><span>{{ inv.cgstTotal | inr }}</span></div><div><span>SGST</span><span>{{ inv.sgstTotal | inr }}</span></div> }
            @if (inv.igstTotal) { <div><span>IGST</span><span>{{ inv.igstTotal | inr }}</span></div> }
            @if (inv.roundOff) { <div><span>Round off</span><span>{{ inv.roundOff | inr }}</span></div> }
            <div class="grand"><span>Grand total</span><span>{{ inv.grandTotal | inr }}</span></div>
            <div><span>Paid</span><span>{{ inv.amountPaid | inr }}</span></div>
            <div><span>Due</span><span [style.color]="inv.amountDue > 0 ? '#a6192e' : ''">{{ inv.amountDue | inr }}</span></div>
          </section>
          <section class="card" style="margin-top:16px">
            <h3>Payments</h3>
            @for (p of inv.payments; track p.paymentId) { <div class="pay"><span>{{ p.paidAt | date:'d MMM, HH:mm' }} · {{ p.method }} {{ p.reference ? '(' + p.reference + ')' : '' }}</span><span class="num">{{ p.amount | inr }}</span></div> }
            @if (!inv.payments.length) { <p class="muted">No payments yet.</p> }
            @if (inv.amountDue > 0 && auth.canBill) { <a mat-stroked-button routerLink="/payments" [queryParams]="{ customerId: inv.customerId }">Record payment</a> }
            <p class="muted" style="margin-top:8px">Billed by {{ inv.createdByName || '—' }} at {{ inv.createdAt | date:'d MMM yyyy, HH:mm' }}</p>
          </section>
        </aside>
      </div>
      @if (printHtml()) { <iframe class="print-frame" [srcdoc]="printHtml()" title="Printable invoice" #frame></iframe> }
    } @else { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }`,
  styles: [`
    .chips { display: flex; gap: 8px; align-items: center; margin-bottom: 12px; }
    .totals > div { display: flex; justify-content: space-between; padding: 4px 0; } .totals .grand { font-size: 20px; font-weight: 600; border-top: 1px solid #e3e5e8; margin-top: 4px; padding-top: 8px; }
    .pay { display: flex; justify-content: space-between; padding: 4px 0; border-bottom: 1px solid #f0f1f3; }
    h3 { margin: 0 0 8px; font-weight: 500; font-size: 16px; }
    .print-frame { position: fixed; left: -9999px; width: 800px; height: 1000px; border: 0; }
  `],
})
export class InvoiceDetailComponent implements OnInit {
  readonly auth = inject(AuthService);
  private api = inject(SalesApi); private notify = inject(NotifyService); private dialog = inject(MatDialog); private route = inject(ActivatedRoute); private sanitizer = inject(DomSanitizer);
  @Input() id!: string;
  cols = ['title', 'qty', 'price', 'disc', 'gst', 'total'];
  inv = signal<Invoice | null>(null); printHtml = signal<SafeHtml | null>(null);

  ngOnInit() { this.load(); if (this.route.snapshot.queryParamMap.get('print') === '1') setTimeout(() => this.print(), 600); }
  load() { this.api.invoice(this.id).subscribe({ next: i => this.inv.set(i), error: e => this.notify.error(e) }); }

  /** Fetches the server-rendered (authenticated) HTML and prints it from a hidden iframe. */
  print() {
    this.api.printHtml(this.id).subscribe({
      next: html => {
        const w = window.open('', '_blank', 'noopener,width=800,height=900');
        if (w) { w.document.open(); w.document.write(html); w.document.close(); w.focus(); setTimeout(() => w.print(), 300); }
        else { this.printHtml.set(this.sanitizer.bypassSecurityTrustHtml(html)); this.notify.info('Pop-up blocked — allow pop-ups to print.'); }
      },
      error: e => this.notify.error(e),
    });
  }
  returnItems() { this.dialog.open(ReturnDialogComponent, { width: '760px', data: this.inv() }).afterClosed().subscribe(ok => { if (ok) this.load(); }); }
  cancel() {
    this.dialog.open(ConfirmDialogComponent, { data: { title: 'Cancel invoice', message: 'Stock will be restored and any payment recorded as refunded. This cannot be undone.', confirmText: 'Cancel invoice', danger: true, requireReason: true } })
      .afterClosed().subscribe((reason: string | undefined) => { if (reason) this.api.cancel(this.id, reason).subscribe({ next: () => { this.notify.success('Invoice cancelled'); this.load(); }, error: e => this.notify.error(e) }); });
  }
}
