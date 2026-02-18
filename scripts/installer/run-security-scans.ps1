Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Write-Host "Running security and secret scan checks"
if ($null -ne (Get-Command -Name "git" -ErrorAction SilentlyContinue)) {
	$gitArgs = @("grep", "-n", "-I", "-e", "AKIA", "-e", "PRIVATE KEY", "-e", "password=")
	$rawMatches = (& git @gitArgs 2>$null)
	$matches = @($rawMatches | Where-Object { $_ -notmatch '^scripts/installer/run-security-scans\.ps1:' })
	if ($matches.Count -gt 0) {
		throw "Potential secret pattern detected in repository files."
	}
}
Write-Host "Security and secret scan checks completed"
