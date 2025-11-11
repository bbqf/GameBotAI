param(
    [string]$ResultsDir = '.'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ResultsDir)) {
    Write-Host "No test results directory '$ResultsDir' found."
    exit 0
}

$trxFiles = Get-ChildItem -Recurse -Filter *.trx -Path $ResultsDir | Sort-Object LastWriteTime -Descending
if (-not $trxFiles) {
    Write-Host "No TRX files found under $ResultsDir."
    exit 0
}

$latest = $trxFiles | Select-Object -First 1
[xml]$doc = Get-Content $latest.FullName
$failed = @($doc.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -ne 'Passed' })
if ($failed.Count -eq 0) {
    Write-Host "All tests passed in $($latest.Name)."
    exit 0
}

Write-Host "Detected $($failed.Count) failing test(s) in $($latest.Name)." -ForegroundColor Yellow
foreach ($f in $failed) {
    $name = $f.testName
    $outcome = $f.outcome
    $message = $f.Output.ErrorInfo.Message -replace "`r`n", ' ' -replace "`n", ' '
    Write-Host ("TESTERROR:{0}:{1}:{2}" -f $name, $outcome, $message) -ForegroundColor Red
}

# Non-zero exit to flag pipeline / parent task
exit 1
