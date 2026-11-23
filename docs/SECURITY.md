# BookFlow — SECURITY.md

| Control | Implementation |
|---|---|
| Authentication | Email + password. Passwords hashed with ASP.NET Core `PasswordHasher` (PBKDF2-HMAC-SHA256, 100k iterations). JWT access token 15 min (HS256, 256-bit key from `Jwt__Key` env), refresh token 7 days, random 64 bytes, stored SHA-256 hashed, rotated on every use, family revoked on reuse. |
| Tenant isolation | `TenantId` claim in JWT is the only source of tenant identity. `ITenantContext` reads it from `HttpContext.User`. Every `ITenantEntity` has an EF global query filter; `TenantSaveChangesInterceptor` stamps and verifies TenantId on write. Integration tests `TenantIsolationTests` assert 404 across tenants for every aggregate. Optional SQL Row-Level Security script in `deploy/sql/rls.sql` (defence in depth, off by default). |
| Authorization | Role-based policies (`Policies.cs`): Owner ⊃ StoreManager ⊃ {BillingEmployee, InventoryManager}. `[Authorize(Policy = …)]` on every controller action; no anonymous endpoints except health, register, login, refresh. |
| Input validation | FluentValidation validators for every command; model binding errors → 400 problem details. Quantities > 0, money ≥ 0, max lengths, GSTIN regex, phone regex. |
| SQL injection | EF Core parameterised queries only; the two raw SQL statements (stock deduction, sequence lock) use `FormattableString` interpolation → parameters. |
| XSS | API returns JSON only; invoice print HTML is rendered server-side with HTML-encoding of all values. Angular escapes bindings by default; no `innerHTML` with user data. |
| CSRF | Bearer tokens in `Authorization` header (not cookies) → not CSRF-exposed. CORS restricted to `Cors__AllowedOrigins`. |
| Rate limiting | ASP.NET Core rate limiter: `auth` policy 10 req/min per IP on login/register/refresh; `search` 120 req/min per user; global 600 req/min per user. 429 with `Retry-After`. |
| Timeouts | Request timeout 30 s (`RequestTimeouts` middleware); SQL command timeout 30 s; Excel import 120 s. |
| Secrets | Only from environment variables / `.env` (never committed). `.env.example` documents names. CI uses GitHub secrets. |
| Transport | nginx terminates TLS (Let's Encrypt via certbot, see DEPLOYMENT.md); HSTS, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, CSP for the SPA. API also sets headers via `SecurityHeadersMiddleware`. |
| At rest | SQL Server TDE optional (Standard edition+); backups encrypted with `BACKUP ... WITH ENCRYPTION` when `BACKUP_CERT` configured. Disk encryption on the VPS recommended. |
| Database permissions | App connects as `bookflow_app` (db_datareader, db_datawriter, EXECUTE) — not `sa`. Migrations run with `bookflow_migrator` (db_ddladmin) in the `migrate` compose job. Scripts in `deploy/sql/create-logins.sql`. |
| Audit | `AuditLogs` row for every insert/update/delete of business entities (who, when, before/after JSON). Login success/failure logged (structured, no passwords). |
| Logging hygiene | Serilog with destructuring policies: password, token, refreshToken, authorization fields masked. No PII in URLs. |
| Dependency hygiene | `dotnet list package --vulnerable` and `npm audit --audit-level=high` in CI. |
| Idempotency | `Idempotency-Key` on invoice creation and payments; duplicate network submits cannot create two invoices. |

## Highest-risk scenarios and tests

| Scenario | Test |
|---|---|
| Tenant A reads Tenant B's invoice | `TenantIsolationTests.Invoice_from_other_tenant_returns_404` |
| Two cashiers sell the last copy | `ConcurrencyTests.Concurrent_sales_of_last_copy_only_one_succeeds` (Parallel 10 requests, exactly 1 × 201, 9 × 409) |
| Client retries invoice POST | `IdempotencyTests.Same_key_returns_same_invoice_no_duplicate` |
| Refresh token replay | `AuthTests.Reused_refresh_token_revokes_family` |
| Billing employee calls admin endpoint | `AuthorizationTests.BillingEmployee_cannot_create_user` |
| Negative quantity / price injection | `ValidationTests` |

## Incident response basics

Rotate `Jwt__Key` to invalidate all sessions; revoke all refresh tokens with `UPDATE RefreshTokens SET RevokedAt = SYSUTCDATETIME()`; restore from backup per DEPLOYMENT.md §Restore.
