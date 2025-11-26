Param()

Write-Host "Verifying Tesseract availability..."
$exe = $env:GAMEBOT_TESSERACT_PATH
if ([string]::IsNullOrWhiteSpace($exe)) { $exe = "tesseract" }
$found = $false
try {
  $version = & $exe --version 2>$null | Select-Object -First 1
  if ($LASTEXITCODE -eq 0 -and $version) { $found = $true }
} catch { }
if (-not $found) {
  Write-Host "Tesseract NOT found (checked '$exe'). Set GAMEBOT_TESSERACT_PATH or install tesseract."; exit 1
}
Write-Host "Tesseract found: $version"
$vars = @('GAMEBOT_TESSERACT_PATH','GAMEBOT_TESSERACT_LANG','GAMEBOT_TESSERACT_PSM','GAMEBOT_TESSERACT_OEM')
foreach ($v in $vars) { $val = (Get-Item env:$v -ErrorAction SilentlyContinue).Value; Write-Host "$v=$val" }
exit 0