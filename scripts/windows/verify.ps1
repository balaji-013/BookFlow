# One-shot verification on Windows without Docker: build, unit tests, migration, integration tests (LocalDB).
$ErrorActionPreference = "Stop"
Set-Location "$PSScriptRoot\..\..\backend"

Write-Host "`n[1/5] Restore + build" -ForegroundColor Cyan
dotnet restore BookFlow.sln
dotnet build BookFlow.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed — paste the errors above to Claude." -ForegroundColor Red; exit 1 }

Write-Host "`n[2/5] Unit tests" -ForegroundColor Cyan
dotnet test tests/BookFlow.UnitTests -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "`n[3/5] EF Core migration" -ForegroundColor Cyan
if (-not (Get-Command dotnet-ef -ErrorAction SilentlyContinue)) { dotnet tool install -g dotnet-ef --version 8.* }
if (-not (Test-Path "src/BookFlow.Infrastructure/Persistence/Migrations")) {
  $env:ConnectionStrings__Default = "Server=(localdb)\MSSQLLocalDB;Database=BookFlow;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
  dotnet ef migrations add InitialCreate -p src/BookFlow.Infrastructure -s src/BookFlow.API -o Persistence/Migrations
  dotnet build BookFlow.sln -c Release
} else { Write-Host "Migrations folder already exists, skipping." }

Write-Host "`n[4/5] Integration tests against LocalDB" -ForegroundColor Cyan
$env:BOOKFLOW_TEST_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=BookFlowTests;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
dotnet test tests/BookFlow.IntegrationTests -c Release
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "`n[5/5] All green. Start the app with scripts\windows\run-dev.ps1" -ForegroundColor Green
