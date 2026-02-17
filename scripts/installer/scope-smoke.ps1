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
  @{ Name = "Bundle default scope is per-user"; Ok = $bundle.Contains('Variable Name="SCOPE" Type="string" Value="perUser"') },
  @{ Name = "Bundle default mode is backgroundApp"; Ok = $bundle.Contains('Variable Name="MODE" Type="string" Value="backgroundApp"') },
  @{ Name = "Bundle default backend port is 8080"; Ok = $bundle.Contains('Variable Name="BACKEND_PORT" Type="string" Value="8080"') },
  @{ Name = "Interactive choice maps to SCOPE per-user"; Ok = $props.Contains('SetProperty Id="SCOPE" Value="perUser"') -and $props.Contains('WixAppFolder = &quot;WixPerUserFolder&quot;') },
  @{ Name = "Per-machine scope mapping removed"; Ok = -not $props.Contains('SetProperty Id="SCOPE" Value="perMachine"') },
  @{ Name = "Per-user install path defaults to LocalAppData"; Ok = $props.Contains('CustomAction Id="SetApplicationFolderPerUser" Property="APPLICATIONFOLDER" Value="[LocalAppDataFolder]GameBot"') -and $props.Contains('Custom Action="SetApplicationFolderPerUser" Before="CostFinalize" Condition="SCOPE = &quot;perUser&quot;"') },
  @{ Name = "Per-machine install path action removed"; Ok = -not $props.Contains('SetApplicationFolderPerMachine') },
  @{ Name = "Windows service registration removed"; Ok = -not $startup.Contains('WindowsServiceRegistrationComponent') },
  @{ Name = "Service mode launch constraint removed"; Ok = -not $product.Contains('Service mode requires per-machine scope') },
  @{ Name = "Install root uses APPLICATIONFOLDER"; Ok = $dirs.Contains('Directory Id="APPLICATIONFOLDER" Name="GameBot"') },
  @{ Name = "Start menu shortcut uses URL protocol handler"; Ok = $dirs.Contains('Target="[SystemFolder]rundll32.exe"') -and $dirs.Contains('url.dll,FileProtocolHandler [PROTOCOL]://localhost:[WEB_PORT]/') },
  @{ Name = "Start menu shortcut includes protocol and web port"; Ok = $dirs.Contains('[PROTOCOL]://localhost:[WEB_PORT]/') }
)

$failed = @($checks | Where-Object { -not $_.Ok })
if ($failed.Count -gt 0) {
  foreach ($item in $failed) {
    Write-Error "FAILED: $($item.Name)"
  }
  throw "Installer scope smoke checks failed."
}

Write-Host "Installer scope smoke checks completed."
