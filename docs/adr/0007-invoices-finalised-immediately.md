# ADR-0007 Invoices are finalised immediately

Status: Accepted

## Decision
No draft invoice state on the server; the cart is client-side state. POST creates an Issued invoice atomically. Corrections are by Cancel (reversal movements, only when nothing allocated) or Sales Return (partial, with credit note or cash refund). Issued invoices are immutable.
