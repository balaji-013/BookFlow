# ADR-0005 No offline POS in v1

Status: Accepted

## Decision
POS requires connectivity. The Angular app shows a clear offline banner and blocks checkout. Offline queueing is P1 and would require an idempotent sync protocol (Idempotency-Key already present on invoice creation to make that feasible).
