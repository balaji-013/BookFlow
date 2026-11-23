# ADR-0003 GST handling

Status: Accepted

## Decision
GstRate and HsnCode stored per book (default HSN 4901, 0 %). Tenant stores its GST state code. If the customer GSTIN state code differs from the tenant's, IGST applies; otherwise CGST/SGST split equally. Rates and HSN are editable, no external GST API in v1. Invoice numbering restarts per financial year (April–March): INV/2026-27/000001.

## Consequences
Covers printed books (exempt) and stationery/other taxable goods a bookstore sells. GST return filing (GSTR-1 JSON) is a P1 report built on the same stored splits.
