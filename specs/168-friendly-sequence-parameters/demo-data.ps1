<#
.SYNOPSIS
  Creates the demo data for the manual checks of feature 168 (quickstart.md).

.DESCRIPTION
  The web UI has no field to put a parameter into a field of a command step. This script does it
  through the API. It creates:
    - the command "Friendly demo command" with three steps (tap on an image, swipe, key input);
    - the sequence "Friendly demo" that calls the command and has three friendly parameters:
      "Button to tap" (image), "Difficulty" (choice), "Emulator number" (number with pattern).
  Each run adds new items with a time stamp in the name. The script does not change or delete
  other items. Delete the demo items by hand in the UI when the checks are done.

.PARAMETER BaseUrl
  Address of the GameBot service. Default: http://127.0.0.1:8080

.PARAMETER Token
  Auth token. Leave empty when the service has no token.

.PARAMETER ImageIdA
  ID of the first allowed image. Default: the first image of the service.

.PARAMETER ImageIdB
  ID of the second allowed image. Default: the second image of the service.
#>
param(
  [string]$BaseUrl = 'http://10.0.0.130:8088',
  [string]$Token = '',
  [string]$ImageIdA = 'pns-alliance-tech-donate-exhausted',
  [string]$ImageIdB = 'pns-alliance-tech-donate-button'
)

$ErrorActionPreference = 'Stop'

$headers = @{}
if ($Token) { $headers['Authorization'] = "Bearer $Token" }

function Invoke-Api([string]$Method, [string]$Path, $Body = $null) {
  $arguments = @{ Method = $Method; Uri = "$BaseUrl$Path"; Headers = $headers; ContentType = 'application/json' }
  if ($null -ne $Body) { $arguments['Body'] = ($Body | ConvertTo-Json -Depth 20) }
  try {
    Invoke-RestMethod @arguments
  } catch {
    $detail = ''
    if ($_.Exception.Response) {
      $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
      $detail = $reader.ReadToEnd()
    }
    throw "$Method $Path failed: $($_.Exception.Message) $detail"
  }
}

if (-not $ImageIdA -or -not $ImageIdB) {
  $list = Invoke-Api 'GET' '/api/images'
  $ids = @()
  if ($list.items) { $ids = @($list.items | ForEach-Object { $_.id }) }
  elseif ($list.ids) { $ids = @($list.ids) }
  if ($ids.Count -lt 2) { throw 'The service has fewer than two images. Add two images on the Images page, or give -ImageIdA and -ImageIdB.' }
  if (-not $ImageIdA) { $ImageIdA = $ids[0] }
  if (-not $ImageIdB) { $ImageIdB = $ids[1] }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

# The command declares plain parameters. The sequence declares the friendly ones.
$command = Invoke-Api 'POST' '/api/commands' @{
  name = "Friendly demo command $stamp"
  parameters = @(
    @{ name = 'buttonToTap'; type = 'text' }
    @{ name = 'difficulty'; type = 'number' }
    @{ name = 'emulatorNumber'; type = 'text' }
  )
  steps = @(
    @{
      type = 'PrimitiveTap'; order = 0
      primitiveTap = @{ detectionTarget = @{ referenceImageId = $ImageIdA; confidence = 0.8 } }
      fieldTemplates = @{ 'primitiveTap.detectionTarget.referenceImageId' = '{{buttonToTap}}' }
    }
    @{
      type = 'Swipe'; order = 1
      swipe = @{ startX = 100; startY = 400; endX = 100; endY = 200 }
      fieldTemplates = @{ 'swipe.durationMs' = '{{difficulty}}' }
    }
    @{
      type = 'KeyInput'; order = 2
      keyInput = @{ key = '{{emulatorNumber}}' }
    }
  )
}
Write-Host "Command created: $($command.id)"

$sequence = Invoke-Api 'POST' '/api/sequences' @{
  name = "Friendly demo $stamp"
  version = 1
  parameters = @(
    @{
      name = 'buttonToTap'; label = 'Button to tap'; type = 'text'; kind = 'image'
      allowedImages = @($ImageIdA, $ImageIdB)
    }
    @{
      name = 'difficulty'; label = 'Difficulty'; type = 'number'; kind = 'choice'
      options = @(
        @{ label = 'Easy'; value = '1' }
        @{ label = 'Hard mode'; value = '3' }
      )
    }
    @{
      name = 'emulatorNumber'; label = 'Emulator number'; type = 'number'; kind = 'number'
      min = 1; max = 10; pattern = 'emulator-{value}'
    }
  )
  steps = @(
    @{
      stepId = 's1'; stepType = 'Action'
      primitiveAction = @{ type = 'command'; schemaVersion = 'v1'; payload = @{ commandId = $command.id } }
      parameterBindings = @(
        @{ name = 'buttonToTap'; value = '{{buttonToTap}}' }
        @{ name = 'difficulty'; value = '{{difficulty}}' }
        @{ name = 'emulatorNumber'; value = '{{emulatorNumber}}' }
      )
    }
  )
}
Write-Host "Sequence created: $($sequence.id) ($($sequence.name))"
if ($sequence.warnings) { Write-Host "Warnings: $($sequence.warnings | ConvertTo-Json -Depth 5 -Compress)" }
Write-Host 'Done. Use the sequence in a queue template for the checks.'
