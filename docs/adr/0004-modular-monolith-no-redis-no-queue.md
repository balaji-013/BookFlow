# ADR-0004 Modular monolith, no Redis/queue/Hangfire in v1

Status: Accepted

## Context
Estimated peak load is ~25 rps (GUESS, SYSTEM.md §2); team is 2–3 developers.

## Decision
Single ASP.NET Core deployable, single SQL Server. No Redis, no message broker, no Hangfire. Slow operations (Excel import ≤ 5,000 rows, Excel export ≤ 100k rows) run synchronously with a 120 s timeout.

## Consequences
Lower cost and operational surface. Seams kept: ICache (memory impl), IEmailSender (no-op impl). Revisit when measured p95 or import sizes exceed targets.
