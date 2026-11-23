# Starts the API (http://localhost:5000/swagger) and the Angular dev server (http://localhost:4200) in two windows.
$root = Resolve-Path "$PSScriptRoot\..\.."
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\backend'; `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --project src/BookFlow.API"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\frontend'; if (-not (Test-Path node_modules)) { npm ci }; npx ng serve --open"
