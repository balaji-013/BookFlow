-- Optional defence in depth: SQL Server Row-Level Security keyed on SESSION_CONTEXT('TenantId').
-- The API enforces tenancy itself (EF query filters + interceptor); enable this only after setting
-- SESSION_CONTEXT in a connection interceptor. Shown for SalesInvoices; repeat per tenant table.
CREATE SCHEMA rls;
GO
CREATE FUNCTION rls.fn_tenant_predicate(@TenantId uniqueidentifier)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS ok WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
                         OR IS_MEMBER('db_owner') = 1;
GO
CREATE SECURITY POLICY rls.TenantPolicy
  ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.SalesInvoices,
  ADD BLOCK  PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.SalesInvoices
WITH (STATE = ON);
GO
