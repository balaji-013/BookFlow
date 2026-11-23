import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MATERIAL, InrPipe, PageComponent, daysAgo, today } from '../../shared/shared';
import { ReportsApi, InventoryApi } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { AuthService } from '../../core/auth/auth.service';
import { Dashboard, StockRow } from '../../core/models';

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent, RouterLink],
  template: `
    <bf-page title="Dashboard" [subtitle]="auth.tenant()?.businessName">
      <div actions>
        @if (auth.isManager) {
          <mat-form-field><mat-label>Range</mat-label>
            <mat-select [value]="range()" (valueChange)="setRange($event)">
              <mat-option value="7">Last 7 days</mat-option><mat-option value="30">Last 30 days</mat-option><mat-option value="90">Last 90 days</mat-option>
            </mat-select>
          </mat-form-field>
        }
      </div>
    </bf-page>

    @if (loading()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }

    @if (auth.isManager) {
      @if (d(); as d) {
        <div class="kpis">
          <div class="kpi"><div class="label">Today's sales</div><div class="value">{{ d.todaySales | inr }}</div><div class="sub">{{ d.todayInvoices }} invoices</div></div>
          <div class="kpi"><div class="label">This month</div><div class="value">{{ d.monthSales | inr }}</div><div class="sub">{{ d.monthInvoices }} invoices</div></div>
          <div class="kpi"><div class="label">Net sales ({{ days() }}d)</div><div class="value">{{ d.netSales | inr }}</div><div class="sub">Gross {{ d.grossSales | inr }} · Disc {{ d.discounts | inr }}</div></div>
          <div class="kpi"><div class="label">Gross profit</div><div class="value">{{ d.grossProfit | inr }}</div><div class="sub">{{ d.grossMarginPercent }}% margin</div></div>
          <div class="kpi"><div class="label">Purchases</div><div class="value">{{ d.purchases | inr }}</div><div class="sub">Returns {{ d.salesReturns | inr }}</div></div>
          <div class="kpi"><div class="label">Inventory value</div><div class="value">{{ d.inventoryValuation | inr }}</div><div class="sub">at cost</div></div>
          <div class="kpi clickable" routerLink="/inventory" [queryParams]="{ low: 1 }"><div class="label">Low-stock books</div><div class="value">{{ d.lowStockCount }}</div><div class="sub">at or below reorder level</div></div>
          <div class="kpi clickable" routerLink="/payments"><div class="label">Customer dues</div><div class="value">{{ d.customerOutstanding | inr }}</div><div class="sub">Supplier dues {{ d.supplierOutstanding | inr }}</div></div>
        </div>

        <div class="grid-2">
          <section class="card">
            <h3>Daily sales — last {{ days() }} days</h3>
            @if (maxSales() > 0) {
              <div class="chart" role="img" [attr.aria-label]="'Daily sales bar chart, ' + days() + ' days'">
                @for (p of d.trend; track p.date) {
                  <div class="bar-wrap" [matTooltip]="(p.date | date:'d MMM') + ': ' + (p.sales | inr) + ' · ' + p.invoices + ' inv'">
                    <div class="bar" [style.height.%]="(p.sales / maxSales()) * 100"></div>
                  </div>
                }
              </div>
              <div class="axis"><span>{{ d.trend[0].date | date:'d MMM' }}</span><span>{{ d.trend[d.trend.length - 1].date | date:'d MMM' }}</span></div>
            } @else { <p class="empty">No sales in this period yet.</p> }
          </section>

          <section class="card">
            <h3>Top selling books</h3>
            @if (d.topBooks.length) {
              <table mat-table [dataSource]="d.topBooks">
                <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Title</th><td mat-cell *matCellDef="let r">{{ r.title }}</td></ng-container>
                <ng-container matColumnDef="quantity"><th mat-header-cell *matHeaderCellDef class="num">Qty</th><td mat-cell *matCellDef="let r" class="num">{{ r.quantity }}</td></ng-container>
                <ng-container matColumnDef="revenue"><th mat-header-cell *matHeaderCellDef class="num">Revenue</th><td mat-cell *matCellDef="let r" class="num">{{ r.revenue | inr }}</td></ng-container>
                <tr mat-header-row *matHeaderRowDef="['title','quantity','revenue']"></tr>
                <tr mat-row *matRowDef="let row; columns: ['title','quantity','revenue']"></tr>
              </table>
            } @else { <p class="empty">Nothing sold yet.</p> }
          </section>

          <section class="card">
            <h3>Payment mix</h3>
            @if (d.paymentMix.length) {
              <table mat-table [dataSource]="d.paymentMix">
                <ng-container matColumnDef="method"><th mat-header-cell *matHeaderCellDef>Method</th><td mat-cell *matCellDef="let r">{{ r.method }}</td></ng-container>
                <ng-container matColumnDef="count"><th mat-header-cell *matHeaderCellDef class="num">Count</th><td mat-cell *matCellDef="let r" class="num">{{ r.count }}</td></ng-container>
                <ng-container matColumnDef="amount"><th mat-header-cell *matHeaderCellDef class="num">Amount</th><td mat-cell *matCellDef="let r" class="num">{{ r.amount | inr }}</td></ng-container>
                <tr mat-header-row *matHeaderRowDef="['method','count','amount']"></tr>
                <tr mat-row *matRowDef="let row; columns: ['method','count','amount']"></tr>
              </table>
            } @else { <p class="empty">No payments yet.</p> }
          </section>

          <section class="card">
            <h3>Low stock <a routerLink="/inventory" [queryParams]="{ low: 1 }" class="muted small">view all</a></h3>
            @if (low().length) {
              <table mat-table [dataSource]="low()">
                <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Title</th><td mat-cell *matCellDef="let r">{{ r.title }}</td></ng-container>
                <ng-container matColumnDef="qty"><th mat-header-cell *matHeaderCellDef class="num">On hand</th><td mat-cell *matCellDef="let r" class="num">{{ r.quantityOnHand }}</td></ng-container>
                <ng-container matColumnDef="reorder"><th mat-header-cell *matHeaderCellDef class="num">Reorder at</th><td mat-cell *matCellDef="let r" class="num">{{ r.reorderLevel }}</td></ng-container>
                <tr mat-header-row *matHeaderRowDef="['title','qty','reorder']"></tr>
                <tr mat-row *matRowDef="let row; columns: ['title','qty','reorder']"></tr>
              </table>
            } @else { <p class="empty">All books are above their reorder level.</p> }
          </section>
        </div>
      }
    } @else {
      <div class="card">
        <h3>Welcome, {{ auth.user()?.fullName }}</h3>
        <p class="muted">Use the menu to open the point of sale, look up stock, or record a return.</p>
        @if (auth.canBill) { <a mat-flat-button color="primary" routerLink="/pos"><mat-icon>point_of_sale</mat-icon> Start billing</a> }
      </div>
    }`,
  styles: [`
    h3 { margin: 0 0 12px; font-weight: 500; font-size: 16px; display: flex; justify-content: space-between; align-items: baseline; }
    .small { font-size: 12px; }
    .chart { display: flex; align-items: flex-end; gap: 2px; height: 180px; padding-top: 8px; }
    .bar-wrap { flex: 1; height: 100%; display: flex; align-items: flex-end; min-width: 3px; }
    .bar { width: 100%; background: #1f4e94; border-radius: 4px 4px 0 0; min-height: 2px; transition: height .2s; }
    .bar-wrap:hover .bar { background: #3b6fc4; }
    .axis { display: flex; justify-content: space-between; font-size: 12px; color: #5f6368; margin-top: 4px; }
  `],
})
export class DashboardComponent {
  readonly auth = inject(AuthService);
  private reports = inject(ReportsApi);
  private inventory = inject(InventoryApi);
  private notify = inject(NotifyService);
  readonly d = signal<Dashboard | null>(null);
  readonly low = signal<StockRow[]>([]);
  readonly loading = signal(false);
  readonly range = signal('30');
  readonly days = computed(() => Number(this.range()));
  readonly maxSales = computed(() => Math.max(0, ...(this.d()?.trend.map(t => t.sales) ?? [0])));

  constructor() { if (this.auth.isManager) this.load(); }

  setRange(v: string) { this.range.set(v); this.load(); }

  load() {
    this.loading.set(true);
    this.reports.dashboard(daysAgo(this.days() - 1), today()).subscribe({
      next: d => { this.d.set(d); this.loading.set(false); },
      error: e => { this.loading.set(false); this.notify.error(e); },
    });
    this.inventory.lowStock().subscribe({ next: rows => this.low.set(rows.slice(0, 8)), error: () => { } });
  }
}
