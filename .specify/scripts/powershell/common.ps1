#!/usr/bin/env pwsh
# Common PowerShell functions analogous to common.sh

function Get-RepoRoot {
    try {
        $result = git rev-parse --show-toplevel 2>$null
        if ($LASTEXITCODE -eq 0) {
            return $result
        }
    } catch {
        # Git command failed
    }
    
    # Fall back to script location for non-git repos
    return (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
}

function Get-CurrentBranch {
    # First check if SPECIFY_FEATURE environment variable is set
    if ($env:SPECIFY_FEATURE) {
        return $env:SPECIFY_FEATURE
    }
    
    # Then check git if available
    try {
        $result = git rev-parse --abbrev-ref HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            return $result
        }
    } catch {
        # Git command failed
    }
    
    # For non-git repos, try to find the latest feature directory
    $repoRoot = Get-RepoRoot
    $specsDir = Join-Path $repoRoot "specs"
    
    if (Test-Path $specsDir) {
        $latestFeature = ""
        $highest = 0
        
        Get-ChildItem -Path $specsDir -Directory | ForEach-Object {
            if ($_.Name -match '^(\d{3})-') {
                $num = [int]$matches[1]
                if ($num -gt $highest) {
                    $highest = $num
                    $latestFeature = $_.Name
                }
            }
        }
        
        if ($latestFeature) {
            return $latestFeature
        }
    }
    
    # Final fallback
    return "main"
}

function Test-HasGit {
    try {
        git rev-parse --show-toplevel 2>$null | Out-Null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Test-FeatureBranch {
    param(
        [string]$Branch,
        [bool]$HasGit = $true
    )
    
    # For non-git repos, we can't enforce branch naming but still provide output
    if (-not $HasGit) {
        Write-Warning "[specify] Warning: Git repository not detected; skipped branch validation"
        return $true
    }
    
    if ($Branch -notmatch '^[0-9]{3}-') {
        Write-Output "ERROR: Not on a feature branch. Current branch: $Branch"
        Write-Output "Feature branches should be named like: 001-feature-name"
        return $false
    }
    return $true
}

function Get-FeatureDir {
    param([string]$RepoRoot, [string]$Branch)
    Join-Path $RepoRoot "specs/$Branch"
}

# Returns $true when .specify/feature.json pins an existing feature directory.
function Test-FeatureJsonMatchesFeatureDir {
    param([string]$RepoRoot, [string]$ActiveFeatureDir)
    $featureJsonPath = Join-Path $RepoRoot '.specify/feature.json'
    if (-not (Test-Path $featureJsonPath -PathType Leaf)) { return $false }
    try {
        $featureJson = Get-Content $featureJsonPath -Raw | ConvertFrom-Json
        if (-not $featureJson -or -not $featureJson.feature_directory) { return $false }
        $pinnedDir = Join-Path $RepoRoot $featureJson.feature_directory
        return (Test-Path $pinnedDir -PathType Container)
    } catch {
        return $false
    }
}

# Resolves a template by name through: overrides → core
function Resolve-Template {
    param([string]$TemplateName, [string]$RepoRoot)
    $override = Join-Path $RepoRoot ".specify/templates/overrides/$TemplateName.md"
    if (Test-Path $override -PathType Leaf) { return $override }
    $core = Join-Path $RepoRoot ".specify/templates/$TemplateName.md"
    if (Test-Path $core -PathType Leaf) { return $core }
    return $null
}

function Get-FeaturePathsEnv {
    $repoRoot = Get-RepoRoot
    $currentBranch = Get-CurrentBranch
    $hasGit = Test-HasGit

    # Prefer the directory pinned in .specify/feature.json when it exists
    $featureDir = $null
    $featureJsonPath = Join-Path $repoRoot '.specify/feature.json'
    if (Test-Path $featureJsonPath -PathType Leaf) {
        try {
            $featureJson = Get-Content $featureJsonPath -Raw | ConvertFrom-Json
            if ($featureJson -and $featureJson.feature_directory) {
                $candidate = Join-Path $repoRoot $featureJson.feature_directory
                if (Test-Path $candidate -PathType Container) {
                    $featureDir = $candidate
                }
            }
        } catch { }
    }

    # Fall back to branch-based directory
    if (-not $featureDir) {
        $featureDir = Get-FeatureDir -RepoRoot $repoRoot -Branch $currentBranch
    }

    [PSCustomObject]@{
        REPO_ROOT     = $repoRoot
        CURRENT_BRANCH = $currentBranch
        HAS_GIT       = $hasGit
        FEATURE_DIR   = $featureDir
        FEATURE_SPEC  = Join-Path $featureDir 'spec.md'
        IMPL_PLAN     = Join-Path $featureDir 'plan.md'
        TASKS         = Join-Path $featureDir 'tasks.md'
        RESEARCH      = Join-Path $featureDir 'research.md'
        DATA_MODEL    = Join-Path $featureDir 'data-model.md'
        QUICKSTART    = Join-Path $featureDir 'quickstart.md'
        CONTRACTS_DIR = Join-Path $featureDir 'contracts'
    }
}

function Test-FileExists {
    param([string]$Path, [string]$Description)
    if (Test-Path -Path $Path -PathType Leaf) {
        Write-Output "  ✓ $Description"
        return $true
    } else {
        Write-Output "  ✗ $Description"
        return $false
    }
}

function Test-DirHasFiles {
    param([string]$Path, [string]$Description)
    if ((Test-Path -Path $Path -PathType Container) -and (Get-ChildItem -Path $Path -ErrorAction SilentlyContinue | Where-Object { -not $_.PSIsContainer } | Select-Object -First 1)) {
        Write-Output "  ✓ $Description"
        return $true
    } else {
        Write-Output "  ✗ $Description"
        return $false
    }
}

