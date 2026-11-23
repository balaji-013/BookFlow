// DTOs mirroring backend/docs/API.md. Enums are serialized as strings by the API.

export type UserRole = 'SuperAdmin' | 'Owner' | 'StoreManager' | 'BillingEmployee' | 'InventoryManager';
export type PaymentMethod = 'Cash' | 'Upi' | 'Card' | 'BankTransfer' | 'Credit' | 'CreditNote';
export type DiscountType = 'None' | 'Percentage' | 'Fixed';
export type InvoiceStatus = 'Issued' | 'Cancelled' | 'PartiallyReturned' | 'Returned';
export type PaymentStatus = 'Unpaid' | 'Partial' | 'Paid';
export type CustomerType = 'WalkIn' | 'Retail' | 'Institutional';
export type PurchaseOrderStatus = 'Draft' | 'Submitted' | 'PartiallyReceived' | 'Received' | 'Cancelled';
export type ReturnCondition = 'Resalable' | 'Damaged';
export type RefundMode = 'Cash' | 'CreditNote' | 'AdjustAgainstDue';
export type MovementType = 'OpeningStock' | 'Purchase' | 'Sale' | 'SalesReturn' | 'PurchaseReturn' | 'Damaged' | 'Adjustment' | 'TransferIn' | 'TransferOut' | 'SaleCancellation';

export interface PagedResult<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface UserSummary { id: string; email: string; fullName: string; role: UserRole; storeId?: string; isActive: boolean; }
export interface TenantSummary { id: string; businessName: string; gstin?: string; stateCode: string; invoicePrefix: string; invoiceFooter?: string; }
export interface AuthResponse { accessToken: string; expiresAt: string; refreshToken: string; user: UserSummary; tenant: TenantSummary; }

export interface Tenant {
  id: string; businessName: string; legalName?: string; gstin?: string; stateCode: string; addressLine1?: string; addressLine2?: string; city?: string;
  state: string; pincode?: string; phone?: string; email: string; invoiceFooter?: string; invoicePrefix: string; subscriptionPlan: string; createdAt: string;
}

export interface User { id: string; email: string; fullName: string; role: UserRole; phone?: string; isActive: boolean; lastLoginAt?: string; createdAt: string; }

export interface Named { id: string; name: string; parentId?: string; }

export interface Book {
  id: string; isbn?: string; barcode?: string; title: string; subtitle?: string; authorId?: string; authorName?: string; publisherId?: string; publisherName?: string;
  categoryId?: string; categoryName?: string; edition?: string; language: string; pages?: number; purchasePrice: number; sellingPrice: number; mrp: number;
  hsnCode: string; gstRate: number; reorderLevel: number; isActive: boolean; quantityOnHand: number; rowVersion: string;
}
export interface BookLookup { id: string; title: string; isbn?: string; barcode?: string; authorName?: string; sellingPrice: number; mrp: number; hsnCode: string; gstRate: number; quantityOnHand: number; }
export interface ImportResult { created: number; updated: number; skipped: number; errors: string[]; }

export interface Customer {
  id: string; name: string; phone?: string; email?: string; gstin?: string; stateCode?: string; type: CustomerType; creditLimit: number; address?: string;
  isActive: boolean; outstanding: number; rowVersion: string;
}
export interface Supplier { id: string; name: string; contactPerson?: string; phone?: string; email?: string; gstin?: string; address?: string; isActive: boolean; outstanding: number; rowVersion: string; }
export interface StatementLine { date: string; type: string; number: string; debit: number; credit: number; balance: number; referenceId: string; }
export interface Statement { partyId: string; partyName: string; openingBalance: number; closingBalance: number; lines: StatementLine[]; }

export interface InvoiceItem {
  id: string; bookId: string; title: string; isbn?: string; hsnCode: string; quantity: number; unitPrice: number; discountPercent: number; discountAmount: number;
  taxableAmount: number; gstRate: number; cgstAmount: number; sgstAmount: number; igstAmount: number; lineTotal: number; quantityReturned: number;
}
export interface InvoicePayment { paymentId: string; paymentNumber: string; method: PaymentMethod; amount: number; paidAt: string; reference?: string; }
export interface Invoice {
  id: string; invoiceNumber: string; invoiceDate: string; customerId?: string; customerName: string; customerPhone?: string; customerGstin?: string; isInterState: boolean;
  status: InvoiceStatus; discountType: DiscountType; discountValue: number; subTotal: number; discountTotal: number; taxableTotal: number; cgstTotal: number; sgstTotal: number;
  igstTotal: number; roundOff: number; grandTotal: number; amountPaid: number; amountDue: number; paymentStatus: PaymentStatus; notes?: string; createdAt: string;
  createdByName?: string; cancelledAt?: string; cancelReason?: string; items: InvoiceItem[]; payments: InvoicePayment[];
}
export interface InvoiceListItem {
  id: string; invoiceNumber: string; invoiceDate: string; customerName: string; status: InvoiceStatus; grandTotal: number; amountPaid: number; amountDue: number;
  paymentStatus: PaymentStatus; itemCount: number; createdAt: string;
}
export interface CreateInvoiceRequest {
  customerId?: string | null; customerName?: string | null; customerPhone?: string | null; invoiceDate?: string | null; discountType: DiscountType; discountValue: number;
  notes?: string | null; lines: { bookId: string; quantity: number; unitPrice?: number | null; discountPercent: number }[];
  payment?: { method: PaymentMethod; amount: number; reference?: string | null } | null;
}

export interface SalesReturnItem { id: string; bookId: string; title: string; quantity: number; condition: ReturnCondition; unitPrice: number; taxableAmount: number; taxAmount: number; lineTotal: number; }
export interface SalesReturn {
  id: string; returnNumber: string; salesInvoiceId: string; invoiceNumber: string; customerName: string; returnDate: string; reason?: string; refundMode: RefundMode;
  subTotal: number; taxTotal: number; total: number; creditNoteId?: string; creditNoteNumber?: string; createdAt: string; items: SalesReturnItem[];
}

export interface Allocation { invoiceId: string; invoiceNumber: string; amount: number; }
export interface Payment {
  id: string; paymentNumber: string; customerId?: string; customerName?: string; direction: 'In' | 'Out'; method: PaymentMethod; amount: number; paidAt: string;
  reference?: string; notes?: string; allocated: number; unallocated: number; allocations: Allocation[];
}
export interface OutstandingInvoice { id: string; invoiceNumber: string; invoiceDate: string; grandTotal: number; amountPaid: number; amountDue: number; }
export interface CreditNote { id: string; creditNoteNumber: string; customerId?: string; customerName?: string; amount: number; balance: number; issuedAt: string; status: string; }

export interface PoItem { id: string; bookId: string; title: string; isbn?: string; quantityOrdered: number; quantityReceived: number; unitCost: number; gstRate: number; lineTotal: number; }
export interface PurchaseOrder {
  id: string; poNumber: string; supplierId: string; supplierName: string; orderDate: string; expectedDate?: string; status: PurchaseOrderStatus; notes?: string;
  subTotal: number; taxTotal: number; total: number; createdAt: string; items: PoItem[];
}
export interface GrnItem { bookId: string; title: string; quantity: number; unitCost: number; gstRate: number; lineTotal: number; }
export interface GoodsReceipt {
  id: string; grnNumber: string; purchaseOrderId?: string; poNumber?: string; supplierId: string; supplierName: string; receivedDate: string; supplierInvoiceNumber?: string;
  supplierInvoiceDate?: string; subTotal: number; taxTotal: number; total: number; amountPaid: number; notes?: string; items: GrnItem[];
}

export interface StockRow {
  bookId: string; title: string; isbn?: string; barcode?: string; categoryName?: string; quantityOnHand: number; quantityDamaged: number; reorderLevel: number;
  averageCost: number; sellingPrice: number; stockValue: number; isLow: boolean;
}
export interface Movement {
  id: string; occurredAt: string; bookId: string; title: string; movementType: MovementType; quantity: number; unitCost: number; referenceType: string;
  referenceId: string; referenceNumber?: string; notes?: string;
}
export interface Valuation { distinctBooks: number; totalUnits: number; damagedUnits: number; valueAtCost: number; valueAtSellingPrice: number; }

export interface Dashboard {
  from: string; to: string; todaySales: number; todayInvoices: number; monthSales: number; monthInvoices: number; grossSales: number; discounts: number; taxes: number;
  netSales: number; costOfGoods: number; grossProfit: number; grossMarginPercent: number; purchases: number; salesReturns: number; inventoryValuation: number;
  lowStockCount: number; customerOutstanding: number; supplierOutstanding: number; invoiceCount: number; averageInvoice: number;
  trend: { date: string; sales: number; invoices: number }[]; topBooks: { bookId: string; title: string; quantity: number; revenue: number }[];
  paymentMix: { method: PaymentMethod; amount: number; count: number }[];
}

export interface AuditLog { id: number; userId?: string; userName?: string; entityName: string; entityId: string; action: string; changes?: string; occurredAt: string; }

export interface ProblemDetails { status?: number; title?: string; detail?: string; type?: string; code?: string; errors?: Record<string, string[]>; traceId?: string; bookIds?: string[]; }
