# ADR-0002 Editions are separate Book records

Status: Accepted

## Decision
Each edition/ISBN is its own Books row carrying its own price, HSN and stock. Rows that share a Title/Author are grouped in search results by title. No separate Editions table in v1.

## Consequences
Simple POS and stock model; a 'Work' grouping table can be added later without touching stock/sales.
