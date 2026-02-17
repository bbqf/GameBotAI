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
$startupPath = Join-Path $RepoRoot "installer/wix/Fragments/StartupRegistration.wxs"

Write-Host "Running installer scope smoke checks"

$product = Get-Content -Path $productPath -Raw
$props = Get-Content -Path $propsPath -Raw
$dirs = Get-Content -Path $dirsPath -Raw
$bundle = Get-Content -Path $bundlePath -Raw
$startup = Get-Content -Path $startupPath -Raw

$checks = @(
  @{ Name = "WixUI per-user support enabled"; Ok = $product.Contains('Property Id="WixUISupportPerUser" Value="1"') },
  @{ Name = "Package default folder mode is per-user"; Ok = $product.Contains('Property Id="WixAppFolder" Value="WixPerUserFolder"') },
  @{ Name = "Bundle scope variable removed"; Ok = -not $bundle.Contains('Variable Name="SCOPE"') },
  @{ Name = "Bundle mode variable removed"; Ok = -not $bundle.Contains('Variable Name="MODE"') },
  @{ Name = "Bundle uses single web port variable"; Ok = -not $bundle.Contains('Variable Name="BACKEND_PORT"') -and $bundle.Contains('Variable Name="WEB_PORT" Type="string" Value="8080"') },
  @{ Name = "Per-user folder enforced"; Ok = $props.Contains('SetProperty Id="WixAppFolder" Value="WixPerUserFolder"') },
  @{ Name = "Per-machine scope mapping removed"; Ok = -not $props.Contains('SetProperty Id="SCOPE" Value="perMachine"') },
  @{ Name = "Per-user install path defaults to LocalAppData"; Ok = $props.Contains('CustomAction Id="SetApplicationFolderPerUser" Property="APPLICATIONFOLDER" Value="[LocalAppDataFolder]GameBot"') -and $props.Contains('Custom Action="SetApplicationFolderPerUser" Before="CostFinalize"') },
  @{ Name = "Per-machine install path action removed"; Ok = -not $props.Contains('SetApplicationFolderPerMachine') },
  @{ Name = "Windows service registration removed"; Ok = -not $startup.Contains('WindowsServiceRegistrationComponent') },
  @{ Name = "Service mode launch constraint removed"; Ok = -not $product.Contains('Service mode requires per-machine scope') },
  @{ Name = "Install root uses APPLICATIONFOLDER"; Ok = $dirs.Contains('Directory Id="APPLICATIONFOLDER" Name="GameBot"') },
  @{ Name = "Start menu shortcut uses URL protocol handler"; Ok = $dirs.Contains('Target="[SystemFolder]rundll32.exe"') -and $dirs.Contains('url.dll,FileProtocolHandler http://localhost:[WEB_PORT]/') },
  @{ Name = "Start menu shortcut points to fixed http with dynamic web port"; Ok = $dirs.Contains('http://localhost:[WEB_PORT]/') }
)

$failed = @($checks | Where-Object { -not $_.Ok })
if ($failed.Count -gt 0) {
  foreach ($item in $failed) {
    Write-Error "FAILED: $($item.Name)"
  }
  throw "Installer scope smoke checks failed."
}

Write-Host "Installer scope smoke checks completed."
