# ADR-0010 Atomic conditional stock deduction

Status: Accepted

## Decision
Stock is deducted with a single UPDATE ... WHERE QuantityOnHand >= @qty inside the invoice transaction; zero rows affected raises InsufficientStockException (HTTP 409). No application-level read-then-write. A concurrent integration test proves exactly one of N simultaneous sales of the last copy succeeds.
