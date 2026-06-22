# One-off: normalize the Status line of every spec.md from the audit classification.
# Updates the existing "**Status**: ..." line if present, otherwise inserts one right after the H1 title.
$ErrorActionPreference = 'Stop'
$specsRoot = 'c:\src\GameBot\specs'

$status = [ordered]@{
  '001-save-config'                        = 'Implemented'
  '002-config-logging-hardening'           = 'Implemented'
  '004-command-trigger-tests'              = 'Implemented'
  '006-tesseract-logging'                  = 'Implemented'
  '007-runtime-logging-control'            = 'Implemented'
  '008-fix-trigger-evaluate'               = 'Implemented'
  '009-ocr-confidence-refactor'            = 'Implemented'
  '010-image-storage'                      = 'Implemented'
  '011-image-match-detections'             = 'Implemented'
  '012-image-detect-command'               = 'Implemented'
  '013-command-sequences'                  = 'Implemented'
  '014-sequence-logic'                     = 'Superseded by 031-034'
  '015-web-ui-authoring'                   = 'Superseded by 018'
  '016-authoring-crud-ui'                  = 'Superseded by 018, 020, 039'
  '017-semantic-actions-ui'                = 'Superseded by 039, 064'
  '018-unify-authoring-ui'                 = 'Implemented'
  '019-api-refactor'                       = 'Implemented'
  '020-web-ui-nav'                         = 'Implemented'
  '021-connect-game-action'                = 'Implemented (re-homed as a primitive action by 039)'
  '022-images-authoring-ui'                = 'Implemented'
  '023-emulator-image-crop'                = 'Implemented'
  '024-authoring-execution-ui'             = 'Implemented'
  '025-backend-webui-installer'            = 'Superseded by 026'
  '026-standalone-windows-installer'       = 'Implemented'
  '027-installer-semver-upgrade'           = 'Implemented'
  '028-add-primitive-tap-action'           = 'Implemented (iterated by 064)'
  '029-execution-log'                      = 'Implemented'
  '030-execution-logs-tab'                 = 'Implemented (iterated by 063, 050)'
  '031-sequence-conditional-logic'         = 'Superseded by 032, 033'
  '032-per-step-conditions'                = 'Implemented'
  '033-sequence-conditional-steps'         = 'Implemented'
  '034-command-loops'                      = 'Implemented'
  '035-background-screenshot-service'      = 'Implemented'
  '036-ui-config-editor'                   = 'Implemented'
  '037-tap-wait-retry'                     = 'Implemented'
  '038-sequence-random-delay'              = 'Implemented'
  '039-primitive-actions-refactor'         = 'Implemented'
  '040-wait-for-image'                     = 'Implemented'
  '041-fix-sequence-step-names'            = 'Implemented'
  '042-loop-step-management'               = 'Implemented'
  '043-reorder-spec-folders'               = 'Meta (housekeeping)'
  '044-commands-drag-drop'                 = 'Implemented'
  '045-image-selector-dropdown'            = 'Implemented'
  '046-emulator-execution-queue'           = 'Implemented'
  '047-queue-templates'                    = 'Implemented'
  '048-edit-queue-layout'                  = 'Implemented (iterated by 061, 062)'
  '049-queue-template-link'                = 'Implemented'
  '050-execution-log-grid'                 = 'Implemented'
  '051-queue-execution-runtime'            = 'Implemented'
  '052-ensure-game-running'                = 'Implemented'
  '053-schedulable-sequences'              = 'Implemented (iterated by 059, 060, 061)'
  '054-key-swipe-actions'                  = 'Implemented'
  '055-record-command'                     = 'Implemented'
  '056-simulate-recorded-step'             = 'Implemented'
  '057-authoring-backup-restore'           = 'Implemented'
  '058-tap-point-jitter'                   = 'Implemented'
  '059-relative-schedule-time'             = 'Implemented'
  '060-queue-start-after-every-scheduling' = 'Implemented'
  '061-queue-scheduling-areas'             = 'Implemented'
  '062-queue-management-usability'         = 'Draft (active)'
  '063-execution-logs-hierarchy'           = 'Implemented (iterated by 050)'
  '064-command-editor-rework'              = 'Implemented (iterated by 054)'
}

# Explicit UTF-8 (no BOM) for both read and write; PS 5.1's default cmdlet encoding
# would mangle multibyte characters (em-dashes, arrows) in these files.
$utf8 = New-Object System.Text.UTF8Encoding($false)

foreach ($key in $status.Keys) {
  $path = Join-Path $specsRoot "$key\spec.md"
  if (-not (Test-Path $path)) { Write-Warning "missing: $path"; continue }
  $line = "**Status**: $($status[$key])"
  $lines = [System.Collections.Generic.List[string]]([System.IO.File]::ReadAllLines($path, $utf8))
  $statusIdx = -1
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*\*{0,2}Status\*{0,2}\s*:') { $statusIdx = $i; break }
  }
  if ($statusIdx -ge 0) {
    $lines[$statusIdx] = $line
  } else {
    $h1 = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^#\s') { $h1 = $i; break } }
    if ($h1 -lt 0) { $h1 = 0 }
    $lines.Insert($h1 + 1, '')
    $lines.Insert($h1 + 2, $line)
  }
  [System.IO.File]::WriteAllLines($path, $lines, $utf8)
  Write-Host "set $key -> $($status[$key])"
}
Write-Host "Done: $($status.Count) specs processed."
