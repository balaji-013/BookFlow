# BUILD_VERIFY — first build on your machine (≈15 minutes)

The backend could not be compiled in the environment that produced this repository (NuGet was blocked). Do this once, in order. Prerequisites: .NET SDK 8.0, Node 22, Docker Desktop (for SQL Server).

## 1. Compile

```bash
cd backend
dotnet restore BookFlow.sln
dotnet build BookFlow.sln -c Release
```
If the compiler reports errors, they will be API-shape issues against third-party packages (EF Core / FluentValidation / Serilog / ClosedXML), each a one-line fix. Paste them back to Claude or fix inline; the architecture does not depend on them.

## 2. Unit tests (no database needed)

```bash
dotnet test tests/BookFlow.UnitTests -c Release
```
Expected: 23 passed (pricing engine, GST split, rounding, validators).

## 3. Generate the initial migration

```bash
dotnet tool install -g dotnet-ef           # once
dotnet ef migrations add InitialCreate -p src/BookFlow.Infrastructure -s src/BookFlow.API -o Persistence/Migrations
dotnet build BookFlow.sln -c Release
git add src/BookFlow.Infrastructure/Persistence/Migrations && git commit -m "Add InitialCreate migration"
```
Review the generated migration once: all tenant tables must have `TenantId`, `StockBalances` must carry the CHECK constraint, the filtered unique indexes on `Books(TenantId, Isbn/Barcode)` must be present.

## 4. Integration tests against SQL Server

```bash
docker run --name bf-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Your_password123' -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
export BOOKFLOW_TEST_CONNECTION="Server=localhost,1433;Database=BookFlowTests;User Id=sa;Password=Your_password123;TrustServerCertificate=True;Encrypt=True"
dotnet test tests/BookFlow.IntegrationTests -c Release
```
Expected: 16 passed, including `Concurrent_sales_of_last_copy_only_one_succeeds` and `Tenant_B_cannot_read_update_or_sell_tenant_A_data`. (Set the same variable in PowerShell with `$env:BOOKFLOW_TEST_CONNECTION=...`.)

## 5. Run the whole stack

```bash
cd ..
cp deploy/.env.example deploy/.env      # edit SA_PASSWORD, JWT_KEY
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
# open http://localhost:8080 → Register your business
```

## 6. End-to-end

```bash
cd e2e && npm i && npx playwright install chromium && BASE_URL=http://localhost:8080 npx playwright test
```

## 7. Push

Pushing to GitHub runs `.github/workflows/ci.yml`, which repeats steps 1, 2 and 4 with a SQL Server service container and publishes the images to GHCR on `main`/`develop`.
