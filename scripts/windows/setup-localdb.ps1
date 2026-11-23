# Creates the LocalDB instance BookFlow uses in Development (no Docker needed).
# LocalDB comes with Visual Studio / SQL Server Express installer ("LocalDB" option).
$ErrorActionPreference = "Stop"
if (-not (Get-Command sqllocaldb -ErrorAction SilentlyContinue)) {
  Write-Host "sqllocaldb not found. Install SQL Server 2022 Express (choose 'LocalDB' or 'Basic'):" -ForegroundColor Yellow
  Write-Host "  winget install Microsoft.SQLServer.2022.Express" -ForegroundColor Yellow
  Write-Host "or download https://www.microsoft.com/sql-server/sql-server-downloads then re-run this script."
  exit 1
}
sqllocaldb create MSSQLLocalDB -s | Out-Null
sqllocaldb start MSSQLLocalDB | Out-Null
sqllocaldb info MSSQLLocalDB
Write-Host "`nLocalDB is running. Connection string used by appsettings.Development.json:" -ForegroundColor Green
Write-Host "  Server=(localdb)\MSSQLLocalDB;Database=BookFlow;Trusted_Connection=True;TrustServerCertificate=True"
