param(
    [string]$ResultsDir = '.',
    [switch]$LatestOnly,
    [switch]$Quiet,
    [switch]$VerifyCoverage,
    [string]$CoverageFile = 'coverage.cobertura.xml',
    [double]$MinLineCoverage = 80,
    [double]$MinBranchCoverage = 70,
    [switch]$VerifySecurity
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ResultsDir)) {
    Write-Host "No test results directory '$ResultsDir' found."; exit 0
}

function Test-CoverageGate {
    param(
        [string]$CoveragePath,
        [double]$RequiredLine,
        [double]$RequiredBranch,
        [switch]$Silent
    )

    if (-not (Test-Path $CoveragePath)) {
        throw "Coverage gate failed: coverage file '$CoveragePath' not found."
    }

    [xml]$coverageDoc = Get-Content $CoveragePath
    if (-not $coverageDoc.coverage) {
        throw "Coverage gate failed: unable to parse coverage root node in '$CoveragePath'."
    }

    $lineRateRaw = $coverageDoc.coverage.'line-rate'
    $branchRateRaw = $coverageDoc.coverage.'branch-rate'
    $linePct = [Math]::Round(([double]$lineRateRaw) * 100, 2)
    $branchPct = [Math]::Round(([double]$branchRateRaw) * 100, 2)

    if (-not $Silent) {
        Write-Host "COVERAGE: line=$linePct% branch=$branchPct% (required line>=$RequiredLine% branch>=$RequiredBranch%)"
    }

    if ($linePct -lt $RequiredLine -or $branchPct -lt $RequiredBranch) {
        throw "Coverage gate failed: line=$linePct% branch=$branchPct%"
    }
}

function Test-SecurityGate {
    param([switch]$Silent)

    if (-not $Silent) {
        Write-Host "Running security verification checks..."
    }

    dotnet list GameBot.sln package --vulnerable --include-transitive | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Security gate failed: .NET vulnerability scan returned a non-zero exit code."
    }

    if (Test-Path "src/web-ui/package.json") {
        Push-Location "src/web-ui"
        try {
            npm audit --omit=dev --audit-level=high | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Security gate failed: npm audit reported high severity vulnerabilities."
            }
        }
        finally {
            Pop-Location
        }
    }

    if (Test-Path "scripts/installer/run-security-scans.ps1") {
        & powershell -NoProfile -File "scripts/installer/run-security-scans.ps1"
        if ($LASTEXITCODE -ne 0) {
            throw "Security gate failed: installer secret scan script reported issues."
        }
    }

    if (-not $Silent) {
        Write-Host "SECURITY: all configured scans passed."
    }
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

    try {
        if ($VerifyCoverage) {
            Test-CoverageGate -CoveragePath $CoverageFile -RequiredLine $MinLineCoverage -RequiredBranch $MinBranchCoverage -Silent:$Quiet
        }

        if ($VerifySecurity) {
            Test-SecurityGate -Silent:$Quiet
        }
    }
    catch {
        Write-Host ("TESTERROR:QUALITY_GATE:failed:{0}" -f $_.Exception.Message) -ForegroundColor Red
        exit 1
    }

    exit 0
}

if (-not $Quiet) { Write-Host "Detected $($failedAll.Count) failing test(s) across $($trxFiles.Count) TRX file(s)." -ForegroundColor Yellow }
foreach ($f in $failedAll) {
    Write-Host ("TESTERROR:{0}:{1}:{2}:{3}" -f $f.File, $f.TestName, $f.Outcome, $f.Message) -ForegroundColor Red
}
exit 1
