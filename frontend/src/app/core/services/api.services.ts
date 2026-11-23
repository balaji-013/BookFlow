import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AuditLog, Book, BookLookup, CreateInvoiceRequest, CreditNote, Customer, Dashboard, GoodsReceipt, ImportResult, Invoice, InvoiceListItem, Movement,
  Named, OutstandingInvoice, PagedResult, Payment, PurchaseOrder, SalesReturn, Statement, StockRow, Supplier, Tenant, User, Valuation
} from '../models';

type Query = Record<string, string | number | boolean | null | undefined>;

function params(q?: Query): HttpParams {
  let p = new HttpParams();
  if (q) for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== null && v !== '') p = p.set(k, String(v));
  return p;
}

export function newIdempotencyKey(): string {
  return (crypto as Crypto & { randomUUID?: () => string }).randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

@Injectable({ providedIn: 'root' })
export class ApiClient {
  readonly http = inject(HttpClient);
  readonly base = environment.apiUrl;
  get<T>(path: string, q?: Query) { return this.http.get<T>(`${this.base}${path}`, { params: params(q) }); }
  post<T>(path: string, body: unknown, headers?: Record<string, string>) { return this.http.post<T>(`${this.base}${path}`, body, { headers: new HttpHeaders(headers ?? {}) }); }
  put<T>(path: string, body: unknown) { return this.http.put<T>(`${this.base}${path}`, body); }
  delete<T>(path: string) { return this.http.delete<T>(`${this.base}${path}`); }
  blob(path: string, q?: Query) { return this.http.get(`${this.base}${path}`, { params: params(q), responseType: 'blob' }); }
}

@Injectable({ providedIn: 'root' })
export class CatalogApi {
  private api = inject(ApiClient);
  books(q: Query) { return this.api.get<PagedResult<Book>>('/books', q); }
  book(id: string) { return this.api.get<Book>(`/books/${id}`); }
  lookup(code: string) { return this.api.get<BookLookup>('/books/lookup', { code }); }
  quickSearch(term: string, take = 10) { return this.api.get<BookLookup[]>('/books/quick-search', { term, take }); }
  create(body: Partial<Book> & { openingStock?: number | null }) { return this.api.post<Book>('/books', body); }
  update(id: string, body: Partial<Book> & { openingStock?: number | null }) { return this.api.put<Book>(`/books/${id}`, body); }
  deactivate(id: string) { return this.api.delete<void>(`/books/${id}`); }
  template() { return this.api.blob('/books/import/template'); }
  import(file: File, overwriteStock: boolean) {
    const form = new FormData(); form.append('file', file);
    return this.api.http.post<ImportResult>(`${this.api.base}/books/import`, form, { params: params({ overwriteStock }) });
  }
  authors(search?: string) { return this.api.get<Named[]>('/authors', { search }); }
  publishers(search?: string) { return this.api.get<Named[]>('/publishers', { search }); }
  categories(search?: string) { return this.api.get<Named[]>('/categories', { search }); }
  saveNamed(kind: 'authors' | 'publishers' | 'categories', id: string | null, name: string) {
    return id ? this.api.put<Named>(`/${kind}/${id}`, { name }) : this.api.post<Named>(`/${kind}`, { name });
  }
  deleteNamed(kind: 'authors' | 'publishers' | 'categories', id: string) { return this.api.delete<void>(`/${kind}/${id}`); }
}

@Injectable({ providedIn: 'root' })
export class PartiesApi {
  private api = inject(ApiClient);
  customers(q: Query) { return this.api.get<PagedResult<Customer>>('/customers', q); }
  customer(id: string) { return this.api.get<Customer>(`/customers/${id}`); }
  saveCustomer(id: string | null, body: Partial<Customer>) { return id ? this.api.put<Customer>(`/customers/${id}`, body) : this.api.post<Customer>('/customers', body); }
  customerStatement(id: string, from?: string, to?: string) { return this.api.get<Statement>(`/customers/${id}/statement`, { from, to }); }
  outstanding(customerId: string) { return this.api.get<OutstandingInvoice[]>(`/customers/${customerId}/outstanding`); }

  suppliers(q: Query) { return this.api.get<PagedResult<Supplier>>('/suppliers', q); }
  supplier(id: string) { return this.api.get<Supplier>(`/suppliers/${id}`); }
  saveSupplier(id: string | null, body: Partial<Supplier>) { return id ? this.api.put<Supplier>(`/suppliers/${id}`, body) : this.api.post<Supplier>('/suppliers', body); }
  supplierStatement(id: string, from?: string, to?: string) { return this.api.get<Statement>(`/suppliers/${id}/statement`, { from, to }); }
  paySupplier(id: string, body: { amount: number; method: string; paidAt?: string | null; reference?: string | null; notes?: string | null; goodsReceiptId?: string | null }) {
    return this.api.post<{ id: string }>(`/suppliers/${id}/payments`, body);
  }
}

@Injectable({ providedIn: 'root' })
export class SalesApi {
  private api = inject(ApiClient);
  createInvoice(body: CreateInvoiceRequest, idempotencyKey: string) { return this.api.post<Invoice>('/sales/invoices', body, { 'Idempotency-Key': idempotencyKey }); }
  invoices(q: Query) { return this.api.get<PagedResult<InvoiceListItem>>('/sales/invoices', q); }
  invoice(id: string) { return this.api.get<Invoice>(`/sales/invoices/${id}`); }
  printUrl(id: string) { return `${this.api.base}/sales/invoices/${id}/print`; }
  printHtml(id: string) { return this.api.http.get(`${this.api.base}/sales/invoices/${id}/print`, { responseType: 'text' }); }
  cancel(id: string, reason: string) { return this.api.post<Invoice>(`/sales/invoices/${id}/cancel`, { reason }); }
  createReturn(body: unknown) { return this.api.post<SalesReturn>('/sales/returns', body); }
  returns(q: Query) { return this.api.get<PagedResult<SalesReturn>>('/sales/returns', q); }
  salesReturn(id: string) { return this.api.get<SalesReturn>(`/sales/returns/${id}`); }
}

@Injectable({ providedIn: 'root' })
export class PaymentsApi {
  private api = inject(ApiClient);
  create(body: unknown, idempotencyKey: string) { return this.api.post<Payment>('/payments', body, { 'Idempotency-Key': idempotencyKey }); }
  list(q: Query) { return this.api.get<PagedResult<Payment>>('/payments', q); }
  creditNotes(q: Query) { return this.api.get<PagedResult<CreditNote>>('/credit-notes', q); }
}

@Injectable({ providedIn: 'root' })
export class PurchasingApi {
  private api = inject(ApiClient);
  orders(q: Query) { return this.api.get<PagedResult<PurchaseOrder>>('/purchase-orders', q); }
  order(id: string) { return this.api.get<PurchaseOrder>(`/purchase-orders/${id}`); }
  createOrder(body: unknown) { return this.api.post<PurchaseOrder>('/purchase-orders', body); }
  submit(id: string) { return this.api.post<PurchaseOrder>(`/purchase-orders/${id}/submit`, {}); }
  cancel(id: string) { return this.api.post<PurchaseOrder>(`/purchase-orders/${id}/cancel`, {}); }
  receive(id: string, body: unknown) { return this.api.post<GoodsReceipt>(`/purchase-orders/${id}/receipts`, body); }
  directReceipt(body: unknown) { return this.api.post<GoodsReceipt>('/goods-receipts', body); }
  receipts(q: Query) { return this.api.get<PagedResult<GoodsReceipt>>('/goods-receipts', q); }
  createReturn(body: unknown) { return this.api.post<unknown>('/purchase-returns', body); }
}

@Injectable({ providedIn: 'root' })
export class InventoryApi {
  private api = inject(ApiClient);
  stock(q: Query) { return this.api.get<PagedResult<StockRow>>('/inventory/stock', q); }
  movements(q: Query) { return this.api.get<PagedResult<Movement>>('/inventory/movements', q); }
  lowStock() { return this.api.get<StockRow[]>('/inventory/low-stock'); }
  valuation() { return this.api.get<Valuation>('/inventory/valuation'); }
  adjust(body: unknown) { return this.api.post<unknown>('/inventory/adjustments', body); }
}

@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private api = inject(ApiClient);
  dashboard(from?: string, to?: string) { return this.api.get<Dashboard>('/reports/dashboard', { from, to }); }
  report<T>(path: string, q: Query): Observable<T[]> { return this.api.get<T[]>(`/reports/${path}`, q); }
  download(path: string, q: Query) { return this.api.blob(`/reports/${path}`, { ...q, format: 'xlsx' }); }
}

@Injectable({ providedIn: 'root' })
export class AdminApi {
  private api = inject(ApiClient);
  tenant() { return this.api.get<Tenant>('/tenant'); }
  updateTenant(body: Partial<Tenant>) { return this.api.put<Tenant>('/tenant', body); }
  users(q: Query) { return this.api.get<PagedResult<User>>('/users', q); }
  createUser(body: unknown) { return this.api.post<User>('/users', body); }
  updateUser(id: string, body: unknown) { return this.api.put<User>(`/users/${id}`, body); }
  setActive(id: string, active: boolean) { return this.api.post<void>(`/users/${id}/${active ? 'activate' : 'deactivate'}`, {}); }
  resetPassword(id: string, newPassword: string) { return this.api.post<void>(`/users/${id}/reset-password`, { newPassword }); }
  audit(q: Query) { return this.api.get<PagedResult<AuditLog>>('/audit', q); }
}

export function saveBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = filename; a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
