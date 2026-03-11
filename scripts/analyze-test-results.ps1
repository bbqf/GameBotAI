param(
    [string]$ResultsDir = '.',
    [switch]$LatestOnly,
    [switch]$Quiet,
    [switch]$VerifyCoverage,
    [string]$CoverageFile = 'coverage.cobertura.xml',
    [double]$MinLineCoverage = 80,
    [double]$MinBranchCoverage = 70,
    [switch]$VerifySecurity,
    [switch]$VerifyLintFormat,
    [switch]$VerifyStaticAnalysis
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

    $changedSourceFiles = @()
    $changedProbe = git diff --name-only --diff-filter=ACMR HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $changedProbe) {
        $changedSourceFiles = @($changedProbe | Where-Object { $_ -like 'src/*.cs' -or $_ -like 'src/**/*.cs' })
    }

    if ($changedSourceFiles.Count -eq 0) {
        if (-not $Silent) {
            Write-Host "COVERAGE: skipped threshold enforcement (no changed src/*.cs files detected)."
        }
        return
    }

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

    # Enforce per-file thresholds for touched source files when coverage metadata provides class filenames.
    $classNodes = @($coverageDoc.SelectNodes('//class[@filename]'))
    if ($classNodes.Count -eq 0) {
        throw "Coverage gate failed: cobertura class filename metadata is missing; cannot validate touched files."
    }

    $fileCoverageMap = @{}
    foreach ($classNode in $classNodes) {
        $filename = [string]$classNode.GetAttribute('filename')
        if ([string]::IsNullOrWhiteSpace($filename)) {
            continue
        }

        if (-not $fileCoverageMap.ContainsKey($filename)) {
            $fileCoverageMap[$filename] = [PSCustomObject]@{
                Filename = $filename
                LineRates = New-Object System.Collections.Generic.List[double]
                BranchRates = New-Object System.Collections.Generic.List[double]
            }
        }

        $lineRate = 0.0
        $branchRate = 0.0
        [void][double]::TryParse([string]$classNode.GetAttribute('line-rate'), [ref]$lineRate)
        [void][double]::TryParse([string]$classNode.GetAttribute('branch-rate'), [ref]$branchRate)
        $fileCoverageMap[$filename].LineRates.Add($lineRate)
        $fileCoverageMap[$filename].BranchRates.Add($branchRate)
    }

    foreach ($changedPath in $changedSourceFiles) {
        $normalizedChanged = ($changedPath -replace '\\', '/').TrimStart('./')
        $matches = @(
            $fileCoverageMap.Keys |
                Where-Object {
                    $normalizedCoverage = ($_ -replace '\\', '/').TrimStart('./')
                    $normalizedCoverage -eq $normalizedChanged -or $normalizedCoverage.EndsWith("/$normalizedChanged")
                }
        )

        if ($matches.Count -eq 0) {
            throw "Coverage gate failed: no coverage entry found for touched file '$changedPath'."
        }

        foreach ($match in $matches) {
            $lineAvg = (($fileCoverageMap[$match].LineRates | Measure-Object -Average).Average)
            $branchAvg = (($fileCoverageMap[$match].BranchRates | Measure-Object -Average).Average)
            $lineAvgPct = [Math]::Round(([double]$lineAvg) * 100, 2)
            $branchAvgPct = [Math]::Round(([double]$branchAvg) * 100, 2)

            if (-not $Silent) {
                Write-Host "COVERAGE: touched '$changedPath' => line=$lineAvgPct% branch=$branchAvgPct%"
            }

            if ($lineAvgPct -lt $RequiredLine -or $branchAvgPct -lt $RequiredBranch) {
                throw "Coverage gate failed: touched file '$changedPath' below threshold (line=$lineAvgPct% branch=$branchAvgPct%)."
            }
        }
    }

    if (-not $Silent) {
        Write-Host "COVERAGE: touched-file thresholds satisfied."
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
    if (-not $Silent) {
        Write-Host "SECURITY:SAST:dotnet-dependency-scan=passed"
    }

    if (Test-Path "src/web-ui/package.json") {
        Push-Location "src/web-ui"
        try {
            npm audit --omit=dev --audit-level=high | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Security gate failed: npm audit reported high severity vulnerabilities."
            }
            if (-not $Silent) {
                Write-Host "SECURITY:SAST:web-ui-npm-audit=passed"
            }
        }
        finally {
            Pop-Location
        }
    }

    if ($null -ne (Get-Command -Name "git" -ErrorAction SilentlyContinue)) {
        $gitSecretArgs = @(
            "grep",
            "-n",
            "-I",
            "-e", "AKIA",
            "-e", "PRIVATE KEY",
            "-e", "password=",
            "-e", "BEGIN RSA PRIVATE KEY"
        )
        $secretOutput = (& git @gitSecretArgs 2>$null)
        $secretMatches = @(
            $secretOutput |
                Where-Object {
                    $_ -notmatch '^scripts/installer/run-security-scans\.ps1:' -and
                    $_ -notmatch '^scripts/analyze-test-results\.ps1:'
                }
        )
        if ($secretMatches.Count -gt 0) {
            throw "Security gate failed: potential secret pattern detected by repository scan."
        }

        if (-not $Silent) {
            Write-Host "SECURITY:secret-scan=passed"
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

function Test-LintFormatGate {
    param([switch]$Silent)

    if (-not $Silent) {
        Write-Host "Running lint/format verification checks..."
    }

    $changedCsFiles = @()
    $changedFileProbe = git diff --name-only --diff-filter=ACMR HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $changedFileProbe) {
        $changedCsFiles = @($changedFileProbe | Where-Object { $_ -like '*.cs' })
    }

    if ($changedCsFiles.Count -gt 0) {
        dotnet format whitespace GameBot.sln --verify-no-changes --verbosity minimal --include $changedCsFiles | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Lint/format gate failed: dotnet format detected formatting issues in changed C# files."
        }

        if (-not $Silent) {
            Write-Host "LINTFORMAT:dotnet-format=passed"
        }
    }
    elseif (-not $Silent) {
        Write-Host "LINTFORMAT: no changed C# files detected for dotnet format verification."
    }

    $changedWebFiles = @()
    if ($LASTEXITCODE -eq 0 -and $changedFileProbe) {
        $changedWebFiles = @($changedFileProbe | Where-Object { $_ -like 'src/web-ui/*' })
    }

    if ((Test-Path "src/web-ui/package.json") -and $changedWebFiles.Count -gt 0) {
        Push-Location "src/web-ui"
        try {
            npm run lint -- --max-warnings=0 | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Lint/format gate failed: web-ui lint reported issues."
            }

            if (-not $Silent) {
                Write-Host "LINTFORMAT:web-ui-eslint=passed"
            }
        }
        finally {
            Pop-Location
        }
    }
    elseif ((Test-Path "src/web-ui/package.json") -and (-not $Silent)) {
        Write-Host "LINTFORMAT: no changed web-ui files detected for eslint verification."
    }

    if (-not $Silent) {
        Write-Host "LINTFORMAT: all configured checks passed."
    }
}

function Test-StaticAnalysisGate {
    param([switch]$Silent)

    if (-not $Silent) {
        Write-Host "Running static-analysis verification checks..."
    }

    dotnet build GameBot.sln -c Debug -p:RunAnalyzers=true -p:EnforceCodeStyleInBuild=true | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Static-analysis gate failed: baseline build did not pass."
    }

    if (-not $Silent) {
        Write-Host "STATIC_ANALYSIS:dotnet-build-analyzers=passed"
    }
}

$trxFiles = Get-ChildItem -Recurse -Filter *.trx -Path $ResultsDir | Sort-Object LastWriteTime -Descending
if (-not $trxFiles) { Write-Host "No TRX files found under $ResultsDir."; exit 0 }

if ($LatestOnly) { $trxFiles = $trxFiles | Select-Object -First 1 }

$failedAll = @()
foreach ($file in $trxFiles) {
    try {
        [xml]$doc = Get-Content -LiteralPath $file.FullName
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

        if ($VerifyLintFormat) {
            Test-LintFormatGate -Silent:$Quiet
        }

        if ($VerifyStaticAnalysis) {
            Test-StaticAnalysisGate -Silent:$Quiet
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
