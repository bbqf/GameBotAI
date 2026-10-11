# Sorts changed files into path groups and prints the job switches for the CI
# workflow. A path that no group names sets both switches to true, so a new
# kind of file never skips a check by mistake.
#
# Output lines:
#   dotnet=<true|false>
#   webui=<true|false>
#   reason=<text>
#
# Input: -Files, or -Base and -Head (git commits). An empty list runs all jobs.
# With -GithubOutput the lines are also appended to the file in GITHUB_OUTPUT.
param(
  [string[]]$Files = @(),
  [string]$Base = "",
  [string]$Head = "HEAD",
  [switch]$GithubOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Order matters: the first group that matches a path decides its switches.
$groups = @(
  [PSCustomObject]@{ Name = "shared"; Dotnet = $true; Webui = $true; Patterns = @(
      '^\.github/workflows/',
      '^scripts/',
      '^[^/]+\.(sln|props|targets|json|config|ruleset|editorconfig)$',
      '^\.editorconfig$',
      '^\.globalconfig$',
      '(^|/)[^/]+\.(csproj|vbproj|fsproj)$',
      '(^|/)(package-lock\.json|packages\.lock\.json|yarn\.lock|pnpm-lock\.yaml)$'
    ) },
  [PSCustomObject]@{ Name = "docs"; Dotnet = $false; Webui = $false; Patterns = @(
      '^specs/',
      '^docs/',
      '\.md$',
      '^\.specify/',
      '^\.claude/',
      '^\.github/agents/',
      '^\.github/prompts/'
    ) },
  [PSCustomObject]@{ Name = "webui"; Dotnet = $false; Webui = $true; Patterns = @(
      '^src/web-ui/'
    ) },
  [PSCustomObject]@{ Name = "backend"; Dotnet = $true; Webui = $false; Patterns = @(
      '^src/GameBot\.',
      '^tests/',
      '^installer/'
    ) }
)

if ($Files.Count -eq 0 -and $Base) {
  # A new branch has an all-zero base: compare to nothing, so run all jobs.
  if ($Base -notmatch '^0+$') {
    $Files = @(& git diff --name-only "$Base" "$Head" 2>$null)
    if ($LASTEXITCODE -ne 0) { $Files = @() }
  }
}

# "-File" passes a comma list as one string, so split each entry on commas.
$Files = @($Files | ForEach-Object { $_ -split ',' })
$Files = @($Files | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim().Replace('\', '/') })

$dotnet = $false
$webui = $false
$found = [System.Collections.Generic.List[string]]::new()

if ($Files.Count -eq 0) {
  $dotnet = $true
  $webui = $true
  $found.Add("no-file-list")
}
else {
  foreach ($file in $Files) {
    $groupName = "unknown"
    $groupDotnet = $true
    $groupWebui = $true
    foreach ($group in $groups) {
      $hit = $false
      foreach ($pattern in $group.Patterns) {
        if ($file -match $pattern) { $hit = $true; break }
      }
      if ($hit) {
        $groupName = $group.Name
        $groupDotnet = $group.Dotnet
        $groupWebui = $group.Webui
        break
      }
    }
    if ($groupDotnet) { $dotnet = $true }
    if ($groupWebui) { $webui = $true }
    if (-not $found.Contains($groupName)) { $found.Add($groupName) }
  }
}

$lines = @(
  "dotnet=$($dotnet.ToString().ToLowerInvariant())",
  "webui=$($webui.ToString().ToLowerInvariant())",
  "reason=groups: $($found -join ', ')"
)

$lines | ForEach-Object { Write-Output $_ }

if ($GithubOutput -and $env:GITHUB_OUTPUT) {
  $lines | Add-Content -Path $env:GITHUB_OUTPUT -Encoding utf8
}
