Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "Running static analysis checks"

dotnet build -c Debug -warnaserror

Write-Host "Static analysis checks completed"
