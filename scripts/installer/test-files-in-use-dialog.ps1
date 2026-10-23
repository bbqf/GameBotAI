#requires -Version 5.1
<#
.SYNOPSIS
  Helps to check the "Files in use" dialog of an interactive upgrade (feature 163).

.DESCRIPTION
  Use it on an isolated test copy only (scripts/installer/new-test-variant.ps1). It never touches the production install.

  Action Lock:  starts the bot of a test install, so it holds GameBot.Service.exe open. Use -WindowMode to start it
                with no window (the way the real bot runs in the background), a minimized console window, or a normal
                console window. The script prints the process id.
  Action Count: reads a verbose installer log (msiexec /l*v, or a bundle log) and counts the dialog displays.
                One display is one client-side Restart Manager session ("MSI (c) ... RESTART MANAGER: Session opened")
                that is not a "no UI modes" session (a silent run).
                It also counts message 1611 and the "reboot will be required" choice. The result must be 0 or 1 dialog,
                no 1611, and no reboot choice.
  Action Stop:  stops the processes that run from the test install folder.
  Action Suspend / Resume: freezes or thaws the processes from the test install folder. A frozen bot cannot answer a
                close request from the Restart Manager. It models a bot that is slow to stop, for example with a
                running queue. The production log (feature 163) shows a lock after the first close.

.PARAMETER Action
  Lock, Count, or Stop.

.PARAMETER InstallFolder
  The APPLICATIONFOLDER of the test install. Required for Lock and Stop.

.PARAMETER DataFolder
  Lock only. The data folder for the test bot (GAMEBOT_DATA_DIR). Default: a "data" folder in a temporary folder.

.PARAMETER Port
  Lock only. A temporary port for the test bot. The script refuses port 8088 (the production port).

.PARAMETER WindowMode
  Lock only. Hidden (default), Minimized, or Normal.

.PARAMETER LogPath
  Count only. One or more log files.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Lock', 'Count', 'Stop', 'Suspend', 'Resume')]
  [string]$Action,
  [string]$InstallFolder,
  [string]$DataFolder,
  [int]$Port = 18163,
  [ValidateSet('Hidden', 'Minimized', 'Normal')]
  [string]$WindowMode = 'Hidden',
  [string[]]$LogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-TestExePath([string]$Folder) {
  if (-not $Folder) { throw '-InstallFolder is required.' }
  $full = [IO.Path]::GetFullPath($Folder)
  $production = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'GameBot'))
  if ($full.TrimEnd('\') -ieq $production.TrimEnd('\')) {
    throw "Refused: $full is the production install folder. Use a test install."
  }
  return @{ Folder = $full; Exe = (Join-Path $full 'GameBot.Service.exe') }
}

function Read-LogText([string]$Path) {
  # msiexec and Burn write UTF-16 with a byte order mark. A log with no mark is read as UTF-8.
  $reader = New-Object IO.StreamReader($Path, [Text.Encoding]::UTF8, $true)
  try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

switch ($Action) {
  'Lock' {
    $target = Get-TestExePath $InstallFolder
    if (-not (Test-Path $target.Exe)) { throw "Not found: $($target.Exe). Install the test copy first." }
    if ($Port -eq 8088) { throw 'Refused: port 8088 is the production port.' }
    if (-not $DataFolder) { $DataFolder = Join-Path ([IO.Path]::GetTempPath()) "gb163-data-$Port" }
    New-Item -ItemType Directory -Force -Path $DataFolder | Out-Null

    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $target.Exe
    $info.WorkingDirectory = $target.Folder
    $info.UseShellExecute = $false
    $info.EnvironmentVariables['GAMEBOT_DATA_DIR'] = $DataFolder
    $info.EnvironmentVariables['ASPNETCORE_URLS'] = "http://127.0.0.1:$Port"
    switch ($WindowMode) {
      'Hidden' { $info.CreateNoWindow = $true }
      'Minimized' { $info.CreateNoWindow = $false; $info.WindowStyle = 'Minimized' }
      'Normal' { $info.CreateNoWindow = $false }
    }
    $process = [Diagnostics.Process]::Start($info)
    Start-Sleep -Seconds 3
    if ($process.HasExited) { throw "The test bot ended at once (exit code $($process.ExitCode)). Check the data folder $DataFolder." }
    # The test bot must not listen on the production port. If it does, stop it at once.
    $onProduction = @(Get-NetTCPConnection -State Listen -LocalPort 8088 -OwningProcess $process.Id -ErrorAction SilentlyContinue)
    if ($onProduction.Count -gt 0) {
      Stop-Process -Id $process.Id -Force
      throw 'The test bot listened on port 8088 (production). It was stopped. Check the bot settings of the test copy.'
    }
    Write-Host "Locking process started: id $($process.Id), window mode $WindowMode, port $Port"
    Write-Host "It holds: $($target.Exe)"
  }

  'Stop' {
    $target = Get-TestExePath $InstallFolder
    $prefix = $target.Folder.TrimEnd('\') + '\'
    $stopped = 0
    foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
      $path = $null
      try { $path = $process.Path } catch { }
      if ($path -and $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Process -Id $process.Id -Force
        $stopped++
        Write-Host "Stopped: $($process.ProcessName) ($($process.Id))"
      }
    }
    Write-Host "Stopped $stopped process(es) from $($target.Folder)."
  }

  { $_ -in 'Suspend', 'Resume' } {
    $target = Get-TestExePath $InstallFolder
    $prefix = $target.Folder.TrimEnd('\') + '\'
    Add-Type -Namespace Gb163 -Name Nt -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("ntdll.dll")] public static extern int NtSuspendProcess(System.IntPtr handle);
[System.Runtime.InteropServices.DllImport("ntdll.dll")] public static extern int NtResumeProcess(System.IntPtr handle);
'@
    $count = 0
    foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
      $path = $null
      try { $path = $process.Path } catch { }
      if ($path -and $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        if ($Action -eq 'Suspend') { [void][Gb163.Nt]::NtSuspendProcess($process.Handle) } else { [void][Gb163.Nt]::NtResumeProcess($process.Handle) }
        $count++
        Write-Host "${Action}: $($process.ProcessName) ($($process.Id))"
      }
    }
    if ($count -eq 0) { Write-Host 'No process runs from the test install folder.' }
  }

  'Count' {
    if (-not $LogPath) { throw '-LogPath is required.' }
    $rows = @()
    foreach ($path in $LogPath) {
      if (-not (Test-Path $path)) { throw "Log not found: $path" }
      $text = Read-LogText $path
      # A silent run opens a client session too, but it logs "no UI modes" and shows no dialog.
      $dialogs = [regex]::Matches($text, 'MSI \(c\)[^\r\n]*RESTART MANAGER: Session opened').Count -
        [regex]::Matches($text, 'RESTART MANAGER: Will attempt to shut down and restart applications in no UI modes').Count
      $rows += [pscustomobject]@{
        Log          = Split-Path $path -Leaf
        Dialogs      = $dialogs
        Message1611  = [regex]::Matches($text, 'Note: 1: 1611').Count
        RebootChoice = [regex]::Matches($text, 'chose to go on with the installation, although a reboot will be required').Count
        ShutdownOk   = [regex]::Matches($text, 'RESTART MANAGER: Successfully shut down all applications').Count
        Restarted    = [regex]::Matches($text, 'RESTART MANAGER: Previously shut down applications have been restarted').Count
      }
    }
    $rows | Format-Table -AutoSize | Out-String | Write-Host
    $total = ($rows | Measure-Object Dialogs -Sum).Sum
    $bad1611 = ($rows | Measure-Object Message1611 -Sum).Sum
    $badReboot = ($rows | Measure-Object RebootChoice -Sum).Sum
    if ($total -le 1 -and $bad1611 -eq 0 -and $badReboot -eq 0) {
      Write-Host "PASS: $total dialog(s), no message 1611, no reboot choice."
    }
    else {
      Write-Host "FAIL: $total dialog(s), $bad1611 message(s) 1611, $badReboot reboot choice(s)."
      exit 1
    }
  }
}
