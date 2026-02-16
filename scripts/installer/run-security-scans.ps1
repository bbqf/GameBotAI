Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Write-Host "Running security and secret scan checks"
if ($null -ne (Get-Command -Name "git" -ErrorAction SilentlyContinue)) { $gitArgs = @("grep", "-n", "-I", "-e", "AKIA", "-e", "PRIVATE KEY", "-e", "password="); $matches = (& git @gitArgs 2>$null) | Out-String; if ($matches.Trim().Length -gt 0) { throw "Potential secret pattern detected in repository files." } }
Write-Host "Security and secret scan checks completed"
