Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "Running security and secret scan checks"

if (Get-Command git -ErrorAction SilentlyContinue) {
  git grep -n -I -E "(AKIA[0-9A-Z]{16}|-----BEGIN (RSA|EC|OPENSSH) PRIVATE KEY-----|password\s*=\s*\".+\")" -- ":!**/bin/**" ":!**/obj/**" | Out-String | ForEach-Object {
    if ($_.Trim().Length -gt 0) {
      throw "Potential secret pattern detected in repository files."
    }
  }
}

Write-Host "Security and secret scan checks completed"
