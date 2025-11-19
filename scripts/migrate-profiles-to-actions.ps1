<#
.SYNOPSIS
    Migrates legacy profile JSON files to new action and trigger repositories.

.DESCRIPTION
    For each profile JSON under data\profiles, creates a corresponding action JSON under data\actions
    (dropping the legacy 'triggers' array) and writes each embedded trigger to an individual
    JSON file under data\triggers (by trigger Id). The original profile files are retained unless
    -DeleteOriginal is specified.

.PARAMETER DataDir
    Root data directory. Defaults to $env:GAMEBOT_DATA_DIR or ./data if unset.

.PARAMETER DryRun
    If set, no files are written; planned operations are printed.

.PARAMETER DeleteOriginal
    If set, deletes original profile JSON after successful migration.

.EXAMPLE
    pwsh ./scripts/migrate-profiles-to-actions.ps1 -DryRun

.EXAMPLE
    pwsh ./scripts/migrate-profiles-to-actions.ps1 -DeleteOriginal

#>
param(
    [string]$DataDir = $(if ($env:GAMEBOT_DATA_DIR) { $env:GAMEBOT_DATA_DIR } else { Join-Path (Get-Location) 'data' }),
    [switch]$DryRun,
    [switch]$DeleteOriginal
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $DataDir)) {
    Write-Error "Data directory '$DataDir' does not exist."; exit 1
}

$profilesDir = Join-Path $DataDir 'profiles'
if (-not (Test-Path -LiteralPath $profilesDir)) {
    Write-Host "No profiles directory found at '$profilesDir'. Nothing to migrate."; exit 0
}

$actionsDir = Join-Path $DataDir 'actions'
$triggersDir = Join-Path $DataDir 'triggers'
New-Item -ItemType Directory -Force -Path $actionsDir | Out-Null
New-Item -ItemType Directory -Force -Path $triggersDir | Out-Null

$profileFiles = Get-ChildItem -LiteralPath $profilesDir -Filter '*.json'
# Normalize to array to avoid Count property errors under StrictMode when a single FileInfo is returned
$profileFiles = @($profileFiles)
if ($profileFiles.Length -eq 0) {
    Write-Host "No profile JSON files found under '$profilesDir'."; exit 0
}

$migratedCount = 0
$triggerCount = 0
$errors = 0

foreach ($pf in $profileFiles) {
    try {
        $jsonText = Get-Content -LiteralPath $pf.FullName -Raw
        # ConvertFrom-Json -Depth is only supported in newer PowerShell; fall back if unsupported
        try {
            $profileObj = $jsonText | ConvertFrom-Json -Depth 100
        } catch {
            $profileObj = $jsonText | ConvertFrom-Json
        }
        if (-not $profileObj.id) { Write-Warning "Skipping file '$($pf.Name)' (missing id)."; continue }

        # Build action object (drop triggers)
        $action = [ordered]@{
            id = $profileObj.id
            name = $profileObj.name
            gameId = $profileObj.gameId
            steps = $profileObj.steps
            checkpoints = $profileObj.checkpoints
        }
        $actionPath = Join-Path $actionsDir ("$($profileObj.id).json")

        if ($DryRun) {
            Write-Host "[DRY-RUN] Would create action: $actionPath" -ForegroundColor Cyan
        } else {
                    ($action | ConvertTo-Json -Depth 100) | Out-File -FilePath $actionPath -Encoding UTF8
        }
        $migratedCount++

        # Extract triggers
        if ($profileObj.triggers) {
            foreach ($tr in $profileObj.triggers) {
                if (-not $tr.id) { Write-Warning "Trigger in profile '$($profileObj.id)' missing id; skipping."; continue }
                $triggerPath = Join-Path $triggersDir ("$($tr.id).json")
                if ($DryRun) {
                    Write-Host "[DRY-RUN] Would create trigger: $triggerPath" -ForegroundColor DarkCyan
                } else {
                    ($tr | ConvertTo-Json -Depth 100) | Out-File -FilePath $triggerPath -Encoding UTF8
                }
                $triggerCount++
            }
        }

        if ($DeleteOriginal -and -not $DryRun) {
            Remove-Item -LiteralPath $pf.FullName -Force
        }
    }
    catch {
        Write-Error "Failed to migrate '$($pf.Name)': $($_.Exception.Message)"
        $errors++
    }
}

Write-Host "Migration summary:" -ForegroundColor Green
Write-Host "  Actions created: $migratedCount"
Write-Host "  Triggers created: $triggerCount"
Write-Host "  Errors: $errors"
if ($DryRun) { Write-Host "  (Dry run: no files written)" }
if ($DeleteOriginal -and -not $DryRun) { Write-Host "  Original profile files deleted." }

if ($errors -gt 0) { exit 1 } else { exit 0 }