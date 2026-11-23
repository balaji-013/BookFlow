namespace BookFlow.Domain.Enums;

public enum UserRole { SuperAdmin = 0, Owner = 1, StoreManager = 2, BillingEmployee = 3, InventoryManager = 4 }
public enum TenantStatus { Active = 0, Suspended = 1, Closed = 2 }
public enum CustomerType { WalkIn = 0, Retail = 1, Institutional = 2 }
public enum PurchaseOrderStatus { Draft = 0, Submitted = 1, PartiallyReceived = 2, Received = 3, Cancelled = 4 }
public enum MovementType { OpeningStock = 0, Purchase = 1, Sale = 2, SalesReturn = 3, PurchaseReturn = 4, Damaged = 5, Adjustment = 6, TransferIn = 7, TransferOut = 8, SaleCancellation = 9 }
public enum InvoiceStatus { Issued = 0, Cancelled = 1, PartiallyReturned = 2, Returned = 3 }
public enum PaymentStatus { Unpaid = 0, Partial = 1, Paid = 2 }
public enum PaymentMethod { Cash = 0, Upi = 1, Card = 2, BankTransfer = 3, Credit = 4, CreditNote = 5 }
public enum PaymentDirection { In = 0, Out = 1 }
public enum DiscountType { None = 0, Percentage = 1, Fixed = 2 }
public enum ReturnCondition { Resalable = 0, Damaged = 1 }
public enum RefundMode { Cash = 0, CreditNote = 1, AdjustAgainstDue = 2 }
public enum CreditNoteStatus { Open = 0, PartiallyUsed = 1, Used = 2, Cancelled = 3 }
public enum AuditAction { Insert = 0, Update = 1, Delete = 2 }
