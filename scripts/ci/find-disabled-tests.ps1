# Finds tests that the normal test run does not run (feature 194): a skip
# marker with no condition, a focus marker, or a test in comments. The CI runs
# it in the "changes" job on each push.
#
# Contract: specs/194-fix-disabled-tests/contracts/find-disabled-tests.md
#
# Parameter:
#   -Root <path>  The folder to search. The default is the repository root.
#
# Files read below the root (bin/, obj/, and node_modules/ are ignored):
#   tests/**/*.cs, src/web-ui/src/**/*.ts(x), src/web-ui/tests/**/*.ts
#
# Output: one line for each finding, sorted by path and line:
#   <relative path>:<line>: <kind>: <the trimmed source line>
# Then one summary line.
#
# Exit codes: 0 no finding, 1 one or more findings, 2 the script could not run.
param(
  [string]$Root = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $Root) {
  $Root = Join-Path (Join-Path $PSScriptRoot "..") ".."
}

if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
  [Console]::Error.WriteLine("find-disabled-tests: the folder '$Root' does not exist.")
  exit 2
}

$rootFull = (Resolve-Path -LiteralPath $Root).ProviderPath.TrimEnd('\', '/')

# Each pattern starts at the start of the line, after spaces or tabs only.
$csPatterns = @(
  [PSCustomObject]@{ Kind = "skip"; Regex = '^[ \t]*\[[ \t]*(?:[\w.]*\.)?\w*(?:Fact|Theory)\w*[ \t]*\(.*\bSkip[ \t]*=[^=]' },
  [PSCustomObject]@{ Kind = "commented"; Regex = '^[ \t]*//+[ \t]*\[[ \t]*(?:Fact|Theory)(?:[ \t]*\(.*\))?[ \t]*\][ \t]*$' }
)

$literal = '[''"`]'
$tsPatterns = @(
  [PSCustomObject]@{ Kind = "skip"; Regex = "^[ \t]*(?:it|test|describe|test\.describe)\.skip[ \t]*\([ \t]*$literal" },
  [PSCustomObject]@{ Kind = "skip"; Regex = "^[ \t]*(?:xit|xtest|xdescribe)[ \t]*\([ \t]*$literal" },
  [PSCustomObject]@{ Kind = "skip"; Regex = "^[ \t]*test\.fixme[ \t]*\([ \t]*$literal" },
  [PSCustomObject]@{ Kind = "focus"; Regex = '^[ \t]*(?:(?:it|test|describe|test\.describe)\.only|fit|fdescribe)[ \t]*\(' },
  [PSCustomObject]@{ Kind = "commented"; Regex = '^[ \t]*//+[ \t]*(?:it|test|describe)(?:\.(?:skip|only))?[ \t]*\(' }
)

$sources = @(
  [PSCustomObject]@{ Folder = "tests"; Extensions = @(".cs"); Patterns = $csPatterns },
  [PSCustomObject]@{ Folder = "src/web-ui/src"; Extensions = @(".ts", ".tsx"); Patterns = $tsPatterns },
  [PSCustomObject]@{ Folder = "src/web-ui/tests"; Extensions = @(".ts"); Patterns = $tsPatterns }
)

$ignored = '(^|/)(bin|obj|node_modules)(/|$)'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$findings = New-Object System.Collections.Generic.List[object]

foreach ($source in $sources) {
  $folder = Join-Path $rootFull $source.Folder
  if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
    continue
  }

  foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File) {
    if ($source.Extensions -notcontains $file.Extension.ToLowerInvariant()) {
      continue
    }

    $relative = $file.FullName.Substring($rootFull.Length).TrimStart('\', '/').Replace('\', '/')
    if ($relative -match $ignored) {
      continue
    }

    $lines = [System.IO.File]::ReadAllLines($file.FullName, $utf8)
    for ($i = 0; $i -lt $lines.Length; $i++) {
      foreach ($pattern in $source.Patterns) {
        if ($lines[$i] -cmatch $pattern.Regex) {
          $findings.Add([PSCustomObject]@{ Path = $relative; Line = $i + 1; Kind = $pattern.Kind; Text = $lines[$i].Trim() })
          break
        }
      }
    }
  }
}

$sorted = @($findings | Sort-Object -Property @{ Expression = { $_.Path } }, @{ Expression = { $_.Line } })
foreach ($item in $sorted) {
  Write-Output ("{0}:{1}: {2}: {3}" -f $item.Path, $item.Line, $item.Kind, $item.Text)
}

if ($sorted.Count -gt 0) {
  Write-Output "find-disabled-tests: $($sorted.Count) disabled test(s) found. Turn each test on, or delete it. A conditional skip needs a condition and a reason."
  exit 1
}

Write-Output "find-disabled-tests: no disabled test found."
exit 0
