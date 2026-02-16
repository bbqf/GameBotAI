param(
  [Parameter(Mandatory = $false)]
  [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $RepoRoot) {
  $RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
}

$productPath = Join-Path $RepoRoot "installer/wix/Product.wxs"
$propsPath = Join-Path $RepoRoot "installer/wix/Fragments/InstallerProperties.wxs"
$dirsPath = Join-Path $RepoRoot "installer/wix/Fragments/Directories.wxs"
$bundlePath = Join-Path $RepoRoot "installer/wix/Bundle.wxs"

Write-Host "Running installer scope smoke checks"

$product = Get-Content -Path $productPath -Raw
$props = Get-Content -Path $propsPath -Raw
$dirs = Get-Content -Path $dirsPath -Raw
$bundle = Get-Content -Path $bundlePath -Raw

$checks = @(
  @{ Name = "WixUI per-user support enabled"; Ok = $product.Contains('Property Id="WixUISupportPerUser" Value="1"') },
  @{ Name = "Machine-wide remains default"; Ok = $product.Contains('Property Id="WixAppFolder" Value="WixPerMachineFolder"') },
  @{ Name = "Bundle default scope is per-user"; Ok = $bundle.Contains('Variable Name="SCOPE" Type="string" Value="perUser"') },
  @{ Name = "Interactive choice maps to SCOPE per-user"; Ok = $props.Contains('SetProperty Id="SCOPE" Value="perUser"') -and $props.Contains('WixAppFolder = &quot;WixPerUserFolder&quot;') },
  @{ Name = "Interactive choice maps to SCOPE per-machine"; Ok = $props.Contains('SetProperty Id="SCOPE" Value="perMachine"') -and $props.Contains('WixAppFolder = &quot;WixPerMachineFolder&quot;') },
  @{ Name = "Install root uses APPLICATIONFOLDER"; Ok = $dirs.Contains('Directory Id="APPLICATIONFOLDER" Name="GameBot"') },
  @{ Name = "Start menu shortcut uses 64-bit cmd"; Ok = $dirs.Contains('Target="[System64Folder]cmd.exe"') },
  @{ Name = "Start menu shortcut includes URL protocol and port"; Ok = $dirs.Contains('[PROTOCOL]://127.0.0.1:[WEB_PORT]/') }
)

$failed = @($checks | Where-Object { -not $_.Ok })
if ($failed.Count -gt 0) {
  foreach ($item in $failed) {
    Write-Error "FAILED: $($item.Name)"
  }
  throw "Installer scope smoke checks failed."
}

Write-Host "Installer scope smoke checks completed."
