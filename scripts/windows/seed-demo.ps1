# Seeds a complete demo bookstore through the public API (exercises every module).
# Usage:  .\scripts\windows\seed-demo.ps1              (API at http://localhost:5000)
#         .\scripts\windows\seed-demo.ps1 -ApiUrl http://localhost:8080/api/v1
# Re-runnable: each run creates a NEW tenant (unique email) so nothing collides.
param([string]$ApiUrl = "http://localhost:5000/api/v1")
$ErrorActionPreference = "Stop"
$stamp = Get-Date -Format "yyyyMMddHHmm"
$root  = Resolve-Path "$PSScriptRoot\..\.."

function Post($path, $body, $headers = @{}) {
  $json = $body | ConvertTo-Json -Depth 8
  Invoke-RestMethod -Method Post -Uri "$ApiUrl$path" -ContentType "application/json" -Body $json -Headers $headers
}
function Get-Api($path) { Invoke-RestMethod -Method Get -Uri "$ApiUrl$path" -Headers $auth }

Write-Host "`n== 1. Tenant + owner ==" -ForegroundColor Cyan
$ownerEmail = "owner$stamp@demo.bookflow"
$reg = Post "/auth/register-tenant" @{
  businessName = "Anbu Book Centre"; ownerName = "Anbu Selvan"; email = $ownerEmail; password = "Demo@12345"
  phone = "9876543210"; gstin = "33ABCDE1234F1Z5"; city = "Madurai"; stateCode = "33"
}
$auth = @{ Authorization = "Bearer $($reg.accessToken)" }
Write-Host "Owner login: $ownerEmail / Demo@12345"

Invoke-RestMethod -Method Put -Uri "$ApiUrl/tenant" -Headers $auth -ContentType "application/json" -Body (@{
  businessName = "Anbu Book Centre"; legalName = "Anbu Book Centre (Prop. Anbu Selvan)"; gstin = "33ABCDE1234F1Z5"; stateCode = "33"
  addressLine1 = "12, West Masi Street"; addressLine2 = "Near Meenakshi Temple"; city = "Madurai"; state = "Tamil Nadu"; pincode = "625001"
  phone = "9876543210"; invoicePrefix = "ABC"; invoiceFooter = "Goods once sold can be exchanged within 7 days with bill. நன்றி!"
} | ConvertTo-Json) | Out-Null

Write-Host "`n== 2. Employees ==" -ForegroundColor Cyan
foreach ($u in @(
  @{ fullName = "Kavitha R";  email = "kavitha$stamp@demo.bookflow"; password = "Demo@12345"; role = "BillingEmployee";  phone = "9000000001" },
  @{ fullName = "Murugan S";  email = "murugan$stamp@demo.bookflow"; password = "Demo@12345"; role = "InventoryManager"; phone = "9000000002" },
  @{ fullName = "Priya V";    email = "priya$stamp@demo.bookflow";   password = "Demo@12345"; role = "StoreManager";     phone = "9000000003" })) {
  Post "/users" $u $auth | Out-Null; Write-Host "  $($u.role): $($u.email) / Demo@12345"
}

Write-Host "`n== 3. Books (Excel import with opening stock) ==" -ForegroundColor Cyan
$xlsx = Join-Path $root "testdata\sample-books.xlsx"
# Multipart upload that works on Windows PowerShell 5.1 as well as PowerShell 7.
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $reg.accessToken)
$content = New-Object System.Net.Http.MultipartFormDataContent
$fileContent = New-Object System.Net.Http.ByteArrayContent(, [System.IO.File]::ReadAllBytes($xlsx))
$fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
$content.Add($fileContent, "file", "sample-books.xlsx")
$resp = $client.PostAsync("$ApiUrl/books/import", $content).Result
if (-not $resp.IsSuccessStatusCode) { throw "Import failed: $($resp.StatusCode) $($resp.Content.ReadAsStringAsync().Result)" }
$imp = $resp.Content.ReadAsStringAsync().Result | ConvertFrom-Json
Write-Host "  created $($imp.created), updated $($imp.updated), skipped $($imp.skipped)"
$books = (Get-Api "/books?pageSize=100").items
function Book($title) { $b = $books | Where-Object { $_.title -like "$title*" } | Select-Object -First 1; if (-not $b) { throw "book not found: $title" }; $b }

Write-Host "`n== 4. Customers ==" -ForegroundColor Cyan
$customers = @{}
foreach ($c in @(
  @{ name = "Ramesh Kumar";                         phone = "9444011111"; type = "Retail";        creditLimit = 0;     address = "KK Nagar, Madurai" },
  @{ name = "Lakshmi Devi";                         phone = "9444022222"; type = "Retail";        creditLimit = 2000;  address = "Anna Nagar, Madurai" },
  @{ name = "St. Mary's Matriculation School";      phone = "0452-2345678"; type = "Institutional"; creditLimit = 50000; gstin = "33AAACS1234G1Z2"; stateCode = "33"; address = "Tallakulam, Madurai" },
  @{ name = "District Central Library";             phone = "0452-2567890"; type = "Institutional"; creditLimit = 25000; address = "Tamukkam, Madurai" },
  @{ name = "Bengaluru Book Club (inter-state)";    phone = "9880011223"; type = "Institutional"; creditLimit = 10000; gstin = "29AABCB1234H1Z7"; stateCode = "29"; address = "Koramangala, Bengaluru" })) {
  $c.isActive = $true; $c.email = $null; $r = Post "/customers" $c $auth; $customers[$c.name] = $r; Write-Host "  $($c.name)"
}

Write-Host "`n== 5. Suppliers ==" -ForegroundColor Cyan
$suppliers = @{}
foreach ($s in @(
  @{ name = "Vanathi Pathippagam"; contactPerson = "Mr. Ravi";    phone = "044-24340555"; email = "sales@vanathi.example"; gstin = "33AAACV1111A1Z1"; address = "T. Nagar, Chennai" },
  @{ name = "Kalachuvadu Publications"; contactPerson = "Ms. Meena"; phone = "04365-243100"; address = "Nagercoil" },
  @{ name = "Chennai Book Distributors"; contactPerson = "Mr. Faizal"; phone = "9840012345"; gstin = "33AABCC2222B1Z9"; address = "Anna Salai, Chennai" })) {
  $s.isActive = $true; $r = Post "/suppliers" $s $auth; $suppliers[$s.name] = $r; Write-Host "  $($s.name)"
}

Write-Host "`n== 6. Purchasing: PO -> partial receipt, direct receipt ==" -ForegroundColor Cyan
$po = Post "/purchase-orders" @{
  supplierId = $suppliers["Vanathi Pathippagam"].id; notes = "Monthly restock"; submit = $true
  lines = @(
    @{ bookId = (Book "Ponniyin Selvan - Part 1").id; quantity = 20; unitCost = 175 },
    @{ bookId = (Book "Yavana Rani").id;              quantity = 10; unitCost = 205 },
    @{ bookId = (Book "Sivagamiyin Sabatham").id;     quantity = 10; unitCost = 195 })
} $auth
Write-Host "  PO $($po.poNumber) total $($po.total)"
$grn = Post "/purchase-orders/$($po.id)/receipts" @{
  supplierInvoiceNumber = "VP/2026/1187"; supplierInvoiceDate = (Get-Date).ToString("yyyy-MM-dd")
  lines = @( @{ bookId = (Book "Ponniyin Selvan - Part 1").id; quantity = 20 }, @{ bookId = (Book "Yavana Rani").id; quantity = 6 } )
} $auth
Write-Host "  GRN $($grn.grnNumber) (partial: Sivagamiyin Sabatham still pending) total $($grn.total)"
$grn2 = Post "/goods-receipts" @{
  supplierId = $suppliers["Chennai Book Distributors"].id; supplierInvoiceNumber = "CBD-5521"
  lines = @( @{ bookId = (Book "Atomic Habits").id; quantity = 10; unitCost = 440 }, @{ bookId = (Book "The Alchemist").id; quantity = 12; unitCost = 240 } )
} $auth
Write-Host "  Direct GRN $($grn2.grnNumber) total $($grn2.total)"
Post "/suppliers/$($suppliers['Vanathi Pathippagam'].id)/payments" @{ amount = 3000; method = "BankTransfer"; reference = "NEFT-88112"; goodsReceiptId = $grn.id } $auth | Out-Null
Write-Host "  Paid Vanathi 3000 by NEFT (balance remains)"

Write-Host "`n== 7. Inventory adjustment (damaged + count correction) ==" -ForegroundColor Cyan
Post "/inventory/adjustments" @{
  reason = "Physical count correction"; notes = "Rain damage on bottom shelf"
  lines = @( @{ bookId = (Book "Mogamul").id; quantityDelta = -1; condition = "Damaged" }, @{ bookId = (Book "Classmate").id; quantityDelta = 3; condition = "Resalable" } )
} $auth | Out-Null
Write-Host "  1 Mogamul -> damaged, +3 notebooks found"

Write-Host "`n== 8. Sales (POS) ==" -ForegroundColor Cyan
function Sale($body) { Post "/sales/invoices" $body ($auth + @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }) }
# 8a walk-in cash, two books, line discount
$inv1 = Sale @{ discountType = "None"; discountValue = 0
  lines = @( @{ bookId = (Book "Ponniyin Selvan - Part 1").id; quantity = 2; discountPercent = 10 }, @{ bookId = (Book "Thirukkural").id; quantity = 1; discountPercent = 0 } )
  payment = @{ method = "Cash"; amount = 670 } }
Write-Host "  $($inv1.invoiceNumber) walk-in cash  total $($inv1.grandTotal)"
# 8b named customer, stationery (GST 12/18%), 5% invoice discount, then paid in full by UPI
$inv2 = Sale @{ customerId = $customers["Ramesh Kumar"].id; discountType = "Percentage"; discountValue = 5
  lines = @( @{ bookId = (Book "Classmate").id; quantity = 6; discountPercent = 0 }, @{ bookId = (Book "Reynolds").id; quantity = 1; discountPercent = 0 }, @{ bookId = (Book "Atomic Habits").id; quantity = 1; discountPercent = 0 } ) }
Post "/payments" @{ customerId = $customers["Ramesh Kumar"].id; method = "Upi"; amount = $inv2.amountDue; reference = "UPI-2201"; allocations = @( @{ invoiceId = $inv2.id; amount = $inv2.amountDue } ) } ($auth + @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }) | Out-Null
Write-Host "  $($inv2.invoiceNumber) Ramesh UPI  total $($inv2.grandTotal) (GST $($inv2.cgstTotal + $inv2.sgstTotal))"
# 8c institutional credit sale (school) — unpaid
$inv3 = Sale @{ customerId = $customers["St. Mary's Matriculation School"].id; discountType = "Fixed"; discountValue = 200
  lines = @( @{ bookId = (Book "Samacheer Kalvi Class 10 Maths").id; quantity = 25; discountPercent = 0 }, @{ bookId = (Book "Samacheer Kalvi Class 10 Science").id; quantity = 25; discountPercent = 0 } ) }
Write-Host "  $($inv3.invoiceNumber) school credit sale  total $($inv3.grandTotal) due $($inv3.amountDue)"
# 8d partial payment credit sale
$inv4 = Sale @{ customerId = $customers["Lakshmi Devi"].id; discountType = "None"; discountValue = 0
  lines = @( @{ bookId = (Book "Wings of Fire").id; quantity = 2; discountPercent = 0 }, @{ bookId = (Book "Oxford").id; quantity = 1; discountPercent = 0 } )
  payment = @{ method = "Card"; amount = 500; reference = "POS-7781" } }
Write-Host "  $($inv4.invoiceNumber) Lakshmi partial  total $($inv4.grandTotal) due $($inv4.amountDue)"
# 8e inter-state customer -> IGST, paid later by bank transfer
$inv5 = Sale @{ customerId = $customers["Bengaluru Book Club (inter-state)"].id; discountType = "None"; discountValue = 0
  lines = @( @{ bookId = (Book "Camlin").id; quantity = 2; discountPercent = 0 }, @{ bookId = (Book "The Alchemist").id; quantity = 3; discountPercent = 0 } ) }
Post "/payments" @{ customerId = $customers["Bengaluru Book Club (inter-state)"].id; method = "BankTransfer"; amount = $inv5.amountDue; reference = "IMPS-5566"; allocations = @( @{ invoiceId = $inv5.id; amount = $inv5.amountDue } ) } ($auth + @{ "Idempotency-Key" = [guid]::NewGuid().ToString() }) | Out-Null
Write-Host "  $($inv5.invoiceNumber) inter-state (IGST $($inv5.igstTotal)) total $($inv5.grandTotal)"
# 8f one to cancel
$inv6 = Sale @{ discountType = "None"; discountValue = 0; lines = @( @{ bookId = (Book "Yavana Rani").id; quantity = 1; discountPercent = 0 } ); payment = @{ method = "Cash"; amount = 300 } }
Post "/sales/invoices/$($inv6.id)/cancel" @{ reason = "Customer changed mind at counter" } $auth | Out-Null
Write-Host "  $($inv6.invoiceNumber) cancelled (stock restored, cash refunded)"

Write-Host "`n== 9. Payments against dues ==" -ForegroundColor Cyan
$pay = Post "/payments" @{ customerId = $customers["St. Mary's Matriculation School"].id; method = "BankTransfer"; amount = 3000; reference = "CHQ 004512"
  allocations = @( @{ invoiceId = $inv3.id; amount = 3000 } ) } ($auth + @{ "Idempotency-Key" = [guid]::NewGuid().ToString() })
Write-Host "  $($pay.paymentNumber) school paid 3000 (partial)"
$pay2 = Post "/payments" @{ customerId = $customers["Lakshmi Devi"].id; method = "Upi"; amount = $inv4.amountDue; reference = "UPI-91XX"
  allocations = @( @{ invoiceId = $inv4.id; amount = $inv4.amountDue } ) } ($auth + @{ "Idempotency-Key" = [guid]::NewGuid().ToString() })
Write-Host "  $($pay2.paymentNumber) Lakshmi cleared her due"

Write-Host "`n== 10. Sales return (1 resalable + credit note) ==" -ForegroundColor Cyan
$item = $inv4.items | Where-Object { $_.title -like "Wings of Fire*" }
$ret = Post "/sales/returns" @{ salesInvoiceId = $inv4.id; reason = "Duplicate copy"; refundMode = "CreditNote"
  lines = @( @{ salesInvoiceItemId = $item.id; quantity = 1; condition = "Resalable" } ) } $auth
Write-Host "  $($ret.returnNumber) -> credit note $($ret.creditNoteNumber) for $($ret.total)"

Write-Host "`n== Done ==" -ForegroundColor Green
$dash = Get-Api "/reports/dashboard"
Write-Host ("  Today's sales {0}  invoices {1}  low-stock books {2}  customer dues {3}" -f $dash.todaySales, $dash.todayInvoices, $dash.lowStockCount, $dash.customerOutstanding)
Write-Host "  Sign in at http://localhost:4200 as $ownerEmail / Demo@12345"
