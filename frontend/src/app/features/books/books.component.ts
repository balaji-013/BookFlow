import { Component, Inject, inject, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { PageEvent } from '@angular/material/paginator';
import { Subject, debounceTime, distinctUntilChanged, forkJoin } from 'rxjs';
import { MATERIAL, InrPipe, PageComponent, ConfirmDialogComponent } from '../../shared/shared';
import { CatalogApi, saveBlob } from '../../core/services/api.services';
import { NotifyService } from '../../core/services/notify.service';
import { AuthService } from '../../core/auth/auth.service';
import { Book, ImportResult, Named, PagedResult } from '../../core/models';

// ---------------- Book dialog ----------------
@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Edit book' : 'Add book' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid" id="bookForm" (ngSubmit)="save()">
        <mat-form-field class="span3"><mat-label>Title</mat-label><input matInput formControlName="title" required /></mat-form-field>
        <mat-form-field><mat-label>ISBN</mat-label><input matInput formControlName="isbn" placeholder="978…" /></mat-form-field>
        <mat-form-field><mat-label>Barcode (if different)</mat-label><input matInput formControlName="barcode" /></mat-form-field>
        <mat-form-field><mat-label>Edition</mat-label><input matInput formControlName="edition" placeholder="e.g. 3rd, Reprint 2024" /></mat-form-field>
        <mat-form-field><mat-label>Author</mat-label><input matInput formControlName="authorName" [matAutocomplete]="a" /><mat-autocomplete #a="matAutocomplete">@for (x of authors; track x.id) { <mat-option [value]="x.name">{{ x.name }}</mat-option> }</mat-autocomplete></mat-form-field>
        <mat-form-field><mat-label>Publisher</mat-label><input matInput formControlName="publisherName" [matAutocomplete]="p" /><mat-autocomplete #p="matAutocomplete">@for (x of publishers; track x.id) { <mat-option [value]="x.name">{{ x.name }}</mat-option> }</mat-autocomplete></mat-form-field>
        <mat-form-field><mat-label>Category</mat-label><input matInput formControlName="categoryName" [matAutocomplete]="c" /><mat-autocomplete #c="matAutocomplete">@for (x of categories; track x.id) { <mat-option [value]="x.name">{{ x.name }}</mat-option> }</mat-autocomplete></mat-form-field>
        <mat-form-field><mat-label>Language</mat-label><mat-select formControlName="language">@for (l of ['Tamil','English','Hindi','Malayalam','Telugu','Kannada','Other']; track l) { <mat-option [value]="l">{{ l }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field><mat-label>Purchase price</mat-label><input matInput type="number" min="0" step="0.01" formControlName="purchasePrice" /></mat-form-field>
        <mat-form-field><mat-label>Selling price</mat-label><input matInput type="number" min="0" step="0.01" formControlName="sellingPrice" required /></mat-form-field>
        <mat-form-field><mat-label>MRP</mat-label><input matInput type="number" min="0" step="0.01" formControlName="mrp" /></mat-form-field>
        <mat-form-field><mat-label>HSN</mat-label><input matInput formControlName="hsnCode" /><mat-hint>4901 = printed books</mat-hint></mat-form-field>
        <mat-form-field><mat-label>GST %</mat-label><mat-select formControlName="gstRate">@for (g of [0,5,12,18,28]; track g) { <mat-option [value]="g">{{ g }}%</mat-option> }</mat-select></mat-form-field>
        <mat-form-field><mat-label>Reorder level</mat-label><input matInput type="number" min="0" formControlName="reorderLevel" /></mat-form-field>
        @if (!data) { <mat-form-field><mat-label>Opening stock</mat-label><input matInput type="number" min="0" formControlName="openingStock" /></mat-form-field> }
        <mat-form-field><mat-label>Pages</mat-label><input matInput type="number" min="0" formControlName="pages" /></mat-form-field>
        @if (data) { <mat-checkbox formControlName="isActive">Active (visible in POS)</mat-checkbox> }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-flat-button color="primary" form="bookForm" type="submit" [disabled]="form.invalid || busy()">Save</button></mat-dialog-actions>`,
  styles: ['.span3 { grid-column: span 3; } .form-grid { grid-template-columns: repeat(3, 1fr); padding-top: 4px; } @media (max-width: 700px) { .form-grid { grid-template-columns: 1fr 1fr; } .span3 { grid-column: span 2; } }'],
})
export class BookDialogComponent {
  readonly data = inject<Book | null>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<BookDialogComponent>>(MatDialogRef);
  private fb = inject(FormBuilder); private api = inject(CatalogApi); private notify = inject(NotifyService);
  busy = signal(false);
  authors: Named[] = []; publishers: Named[] = []; categories: Named[] = [];
  form = this.fb.nonNullable.group({
    title: [this.data?.title ?? '', Validators.required], isbn: [this.data?.isbn ?? ''], barcode: [this.data?.barcode ?? ''], edition: [this.data?.edition ?? ''],
    authorName: [this.data?.authorName ?? ''], publisherName: [this.data?.publisherName ?? ''], categoryName: [this.data?.categoryName ?? ''], language: [this.data?.language ?? 'Tamil'],
    purchasePrice: [this.data?.purchasePrice ?? 0], sellingPrice: [this.data?.sellingPrice ?? 0, [Validators.required, Validators.min(0)]], mrp: [this.data?.mrp ?? 0],
    hsnCode: [this.data?.hsnCode ?? '4901', Validators.required], gstRate: [this.data?.gstRate ?? 0], reorderLevel: [this.data?.reorderLevel ?? 2], openingStock: [0],
    pages: [this.data?.pages ?? null as number | null], isActive: [this.data?.isActive ?? true],
  });
  constructor() {
    forkJoin([this.api.authors(), this.api.publishers(), this.api.categories()]).subscribe(([a, p, c]) => { this.authors = a; this.publishers = p; this.categories = c; });
  }
  save() {
    if (this.form.invalid) return;
    this.busy.set(true);
    const v = this.form.getRawValue();
    const body = { ...v, isbn: v.isbn || null, barcode: v.barcode || null, edition: v.edition || null, authorName: v.authorName || null, publisherName: v.publisherName || null,
      categoryName: v.categoryName || null, pages: v.pages || null, openingStock: this.data ? null : (v.openingStock || null), rowVersion: this.data?.rowVersion ?? null } as unknown as Partial<Book> & { openingStock?: number | null };
    (this.data ? this.api.update(this.data.id, body) : this.api.create(body)).subscribe({
      next: b => { this.notify.success('Book saved'); this.ref.close(b); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
}

// ---------------- Import dialog ----------------
@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>Import books from Excel</h2>
    <mat-dialog-content>
      <p>Download the template, fill one book per row (max 5,000), then upload it. Books are matched on ISBN or barcode; existing books are updated.</p>
      <button mat-stroked-button (click)="template()"><mat-icon>download</mat-icon> Download template</button>
      <div class="drop">
        <input type="file" accept=".xlsx" (change)="file = $any($event.target).files[0]" />
        <mat-checkbox [(ngModel)]="overwriteStock">Overwrite stock with the OpeningStock column for existing books</mat-checkbox>
      </div>
      @if (busy()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }
      @if (result(); as r) {
        <div class="card result"><strong>{{ r.created }}</strong> created · <strong>{{ r.updated }}</strong> updated · <strong>{{ r.skipped }}</strong> skipped
          @if (r.errors.length) { <ul>@for (e of r.errors; track $index) { <li>{{ e }}</li> }</ul> }</div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button [mat-dialog-close]="!!result()">Close</button><button mat-flat-button color="primary" (click)="upload()" [disabled]="!file || busy()">Upload</button></mat-dialog-actions>`,
  styles: ['.drop { margin: 16px 0; display: grid; gap: 12px; } .result { margin-top: 12px; } ul { max-height: 160px; overflow: auto; font-size: 12px; }'],
})
export class ImportDialogComponent {
  private api = inject(CatalogApi); private notify = inject(NotifyService);
  file: File | null = null; overwriteStock = false; busy = signal(false); result = signal<ImportResult | null>(null);
  template() { this.api.template().subscribe({ next: b => saveBlob(b, 'bookflow-books-template.xlsx'), error: e => this.notify.error(e) }); }
  upload() {
    if (!this.file) return; this.busy.set(true);
    this.api.import(this.file, this.overwriteStock).subscribe({ next: r => { this.result.set(r); this.busy.set(false); }, error: e => { this.busy.set(false); this.notify.error(e); } });
  }
}

// ---------------- Master data dialog ----------------
@Component({
  standalone: true,
  imports: [...MATERIAL],
  template: `
    <h2 mat-dialog-title>Manage {{ data }}</h2>
    <mat-dialog-content>
      <div class="toolbar-row"><mat-form-field class="w-full"><mat-label>New {{ singular }}</mat-label><input matInput [(ngModel)]="name" (keydown.enter)="save(null)" /></mat-form-field><button mat-flat-button color="primary" (click)="save(null)" [disabled]="!name.trim()">Add</button></div>
      <table mat-table [dataSource]="items()">
        <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>Name</th><td mat-cell *matCellDef="let x">{{ x.name }}</td></ng-container>
        <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let x" class="num">
          <button mat-icon-button (click)="rename(x)"><mat-icon>edit</mat-icon></button><button mat-icon-button (click)="remove(x)"><mat-icon>delete</mat-icon></button></td></ng-container>
        <tr mat-header-row *matHeaderRowDef="['name','actions']"></tr><tr mat-row *matRowDef="let row; columns: ['name','actions']"></tr>
      </table>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Close</button></mat-dialog-actions>`,
})
export class MasterDataDialogComponent {
  readonly data = inject<'authors' | 'publishers' | 'categories'>(MAT_DIALOG_DATA);
  private api = inject(CatalogApi); private notify = inject(NotifyService);
  items = signal<Named[]>([]); name = '';
  get singular() { return this.data.replace(/ies$/, 'y').replace(/s$/, ''); }
  constructor() { this.load(); }
  load() { this.api[this.data]().subscribe(x => this.items.set(x)); }
  save(id: string | null, name = this.name) {
    if (!name.trim()) return;
    this.api.saveNamed(this.data, id, name.trim()).subscribe({ next: () => { this.name = ''; this.load(); }, error: e => this.notify.error(e) });
  }
  rename(x: Named) { const n = prompt(`Rename ${this.singular}`, x.name); if (n && n !== x.name) this.save(x.id, n); }
  remove(x: Named) { this.api.deleteNamed(this.data, x.id).subscribe({ next: () => this.load(), error: e => this.notify.error(e, 'Cannot delete: still used by books') }); }
}

// ---------------- Books list ----------------
@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe, PageComponent],
  template: `
    <bf-page title="Books" subtitle="Catalog, prices, GST and stock levels">
      <div actions>
        @if (auth.canManageInventory) {
          <button mat-button [matMenuTriggerFor]="more"><mat-icon>tune</mat-icon> Master data</button>
          <mat-menu #more="matMenu"><button mat-menu-item (click)="master('authors')">Authors</button><button mat-menu-item (click)="master('publishers')">Publishers</button><button mat-menu-item (click)="master('categories')">Categories</button></mat-menu>
          <button mat-stroked-button (click)="import()"><mat-icon>upload_file</mat-icon> Import Excel</button>
          <button mat-flat-button color="primary" (click)="edit(null)"><mat-icon>add</mat-icon> Add book</button>
        }
      </div>
    </bf-page>
    <div class="toolbar-row">
      <mat-form-field class="grow"><mat-label>Search title, ISBN, barcode, author</mat-label><mat-icon matPrefix>search</mat-icon><input matInput (input)="search$.next($any($event.target).value)" /></mat-form-field>
      <mat-form-field><mat-label>Category</mat-label><mat-select [value]="categoryId" (valueChange)="categoryId = $event; pageIndex = 0; load()"><mat-option [value]="null">All</mat-option>@for (c of categories(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select></mat-form-field>
      <mat-checkbox [checked]="lowStock" (change)="lowStock = $event.checked; load()">Low stock only</mat-checkbox>
      <mat-checkbox [checked]="includeInactive" (change)="includeInactive = $event.checked; load()">Show inactive</mat-checkbox>
    </div>
    @if (loading()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }
    <div class="table-card">
      <table mat-table [dataSource]="page()?.items ?? []">
        <ng-container matColumnDef="title"><th mat-header-cell *matHeaderCellDef>Title</th><td mat-cell *matCellDef="let b"><div class="t">{{ b.title }} @if (!b.isActive) { <span class="chip">Inactive</span> }</div><div class="muted">{{ b.authorName }}{{ b.edition ? ' · ' + b.edition : '' }}{{ b.publisherName ? ' · ' + b.publisherName : '' }}</div></td></ng-container>
        <ng-container matColumnDef="isbn"><th mat-header-cell *matHeaderCellDef>ISBN / barcode</th><td mat-cell *matCellDef="let b" class="mono">{{ b.isbn || b.barcode || '—' }}</td></ng-container>
        <ng-container matColumnDef="category"><th mat-header-cell *matHeaderCellDef>Category</th><td mat-cell *matCellDef="let b">{{ b.categoryName }}</td></ng-container>
        <ng-container matColumnDef="price"><th mat-header-cell *matHeaderCellDef class="num">Price</th><td mat-cell *matCellDef="let b" class="num">{{ b.sellingPrice | inr }}<div class="muted">cost {{ b.purchasePrice | inr }}</div></td></ng-container>
        <ng-container matColumnDef="gst"><th mat-header-cell *matHeaderCellDef class="num">GST</th><td mat-cell *matCellDef="let b" class="num">{{ b.gstRate }}%</td></ng-container>
        <ng-container matColumnDef="stock"><th mat-header-cell *matHeaderCellDef class="num">Stock</th><td mat-cell *matCellDef="let b" class="num"><span [class]="'chip ' + (b.quantityOnHand <= 0 ? 'bad' : b.quantityOnHand <= b.reorderLevel ? 'warn' : 'ok')">{{ b.quantityOnHand }}</span></td></ng-container>
        <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let b" class="num">
          @if (auth.canManageInventory) { <button mat-icon-button (click)="edit(b)" matTooltip="Edit"><mat-icon>edit</mat-icon></button>
          @if (b.isActive) { <button mat-icon-button (click)="deactivate(b)" matTooltip="Deactivate"><mat-icon>visibility_off</mat-icon></button> } }</td></ng-container>
        <tr mat-header-row *matHeaderRowDef="cols"></tr>
        <tr mat-row *matRowDef="let row; columns: cols"></tr>
      </table>
      @if (page() && !page()!.items.length) { <div class="empty">No books match. Add one or import from Excel.</div> }
      <mat-paginator [length]="page()?.totalCount ?? 0" [pageSize]="pageSize" [pageIndex]="pageIndex" [pageSizeOptions]="[25, 50, 100, 200]" (page)="onPage($event)"></mat-paginator>
    </div>`,
  styles: ['.grow { flex: 1; min-width: 260px; } .t { font-weight: 500; }'],
})
export class BooksComponent {
  readonly auth = inject(AuthService);
  private api = inject(CatalogApi); private notify = inject(NotifyService); private dialog = inject(MatDialog);
  cols = ['title', 'isbn', 'category', 'price', 'gst', 'stock', 'actions'];
  page = signal<PagedResult<Book> | null>(null); loading = signal(false); categories = signal<Named[]>([]);
  search = ''; categoryId: string | null = null; lowStock = false; includeInactive = false; pageIndex = 0; pageSize = 25; search$ = new Subject<string>();
  constructor() {
    this.search$.pipe(debounceTime(300), distinctUntilChanged()).subscribe(s => { this.search = s; this.pageIndex = 0; this.load(); });
    this.api.categories().subscribe(c => this.categories.set(c));
    this.load();
  }
  load() {
    this.loading.set(true);
    this.api.books({ search: this.search, categoryId: this.categoryId, lowStock: this.lowStock || null, includeInactive: this.includeInactive, page: this.pageIndex + 1, pageSize: this.pageSize })
      .subscribe({ next: p => { this.page.set(p); this.loading.set(false); }, error: e => { this.loading.set(false); this.notify.error(e); } });
  }
  onPage(e: PageEvent) { this.pageIndex = e.pageIndex; this.pageSize = e.pageSize; this.load(); }
  edit(b: Book | null) { this.dialog.open(BookDialogComponent, { width: '820px', data: b }).afterClosed().subscribe(r => { if (r) this.load(); }); }
  import() { this.dialog.open(ImportDialogComponent, { width: '560px' }).afterClosed().subscribe(r => { if (r) this.load(); }); }
  master(kind: 'authors' | 'publishers' | 'categories') { this.dialog.open(MasterDataDialogComponent, { width: '520px', data: kind }).afterClosed().subscribe(() => { this.api.categories().subscribe(c => this.categories.set(c)); this.load(); }); }
  deactivate(b: Book) {
    this.dialog.open(ConfirmDialogComponent, { data: { title: 'Deactivate book', message: `"${b.title}" will be hidden from the POS and lists. Stock history is kept.`, confirmText: 'Deactivate', danger: true } })
      .afterClosed().subscribe(ok => { if (ok) this.api.deactivate(b.id).subscribe({ next: () => this.load(), error: e => this.notify.error(e) }); });
  }
}
