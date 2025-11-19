param(
    [string]$ResultsDir = '.',
    [switch]$LatestOnly,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ResultsDir)) {
    Write-Host "No test results directory '$ResultsDir' found."; exit 0
}

$trxFiles = Get-ChildItem -Recurse -Filter *.trx -Path $ResultsDir | Sort-Object LastWriteTime -Descending
if (-not $trxFiles) { Write-Host "No TRX files found under $ResultsDir."; exit 0 }

if ($LatestOnly) { $trxFiles = $trxFiles | Select-Object -First 1 }

$failedAll = @()
foreach ($file in $trxFiles) {
    try {
        [xml]$doc = Get-Content $file.FullName
    } catch {
        Write-Warning "Failed to parse XML in $($file.FullName): $($_.Exception.Message)"; continue
    }
    if (-not $doc.TestRun.Results.UnitTestResult) { continue }
    foreach ($r in $doc.TestRun.Results.UnitTestResult) {
        if ($r.outcome -ne 'Passed') {
            $msg = ''
            if ($r.Output.ErrorInfo.Message) { $msg = ($r.Output.ErrorInfo.Message -replace "`r`n", ' ' -replace "`n", ' ') }
            elseif ($r.Output.StdOut) { $msg = ($r.Output.StdOut -replace "`r`n", ' ' -replace "`n", ' ') }
            $failedAll += [PSCustomObject]@{
                File      = $file.Name
                TestName  = $r.testName
                Outcome   = $r.outcome
                Message   = $msg
            }
        }
    }
}

if ($failedAll.Count -eq 0) {
    if (-not $Quiet) { Write-Host "All tests passed across $($trxFiles.Count) TRX file(s)." }
    exit 0
}

if (-not $Quiet) { Write-Host "Detected $($failedAll.Count) failing test(s) across $($trxFiles.Count) TRX file(s)." -ForegroundColor Yellow }
foreach ($f in $failedAll) {
    Write-Host ("TESTERROR:{0}:{1}:{2}:{3}" -f $f.File, $f.TestName, $f.Outcome, $f.Message) -ForegroundColor Red
}
exit 1
