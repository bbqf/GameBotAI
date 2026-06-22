# Repair UTF-8-decoded-as-Windows-1252 mojibake by reversing the exact bad round-trip:
#   read as UTF-8 -> re-encode as Windows-1252 -> decode as UTF-8.
# ASCII is identical in both encodings, so it passes through untouched; only the
# garbled multibyte sequences (â€"  â€¦  â†' âœ" âš ï¸ ðŸŽ¯ Â§ â‰¥ ...) are restored.
# The CP1252 encoder uses an exception fallback: if a file contains a genuine
# (non-mojibake) character that cannot have come from this corruption, it throws
# and that file is left untouched for manual review.
$ErrorActionPreference = 'Stop'

$files = @(
  'c:\src\GameBot\specs\052-ensure-game-running\tasks.md',
  'c:\src\GameBot\specs\056-simulate-recorded-step\tasks.md',
  'c:\src\GameBot\specs\060-queue-start-after-every-scheduling\tasks.md',
  'c:\src\GameBot\specs\064-command-editor-rework\tasks.md'
)

$utf8   = New-Object System.Text.UTF8Encoding($false)
$cp1252 = [System.Text.Encoding]::GetEncoding(1252,
            [System.Text.EncoderExceptionFallback]::new(),
            [System.Text.DecoderExceptionFallback]::new())

foreach ($f in $files) {
  if (-not (Test-Path $f)) { Write-Warning "missing: $f"; continue }
  $text = [System.IO.File]::ReadAllText($f, $utf8)
  try {
    $bytes = $cp1252.GetBytes($text)          # throws if a char isn't a CP1252 round-trip
    $fixed = $utf8.GetString($bytes)
  } catch {
    Write-Warning "SKIPPED (genuine non-mojibake char present): $f -- $($_.Exception.Message)"
    continue
  }
  if ($fixed -ne $text) {
    [System.IO.File]::WriteAllText($f, $fixed, $utf8)
    Write-Host "fixed:   $f"
  } else {
    Write-Host "no-op:   $f"
  }
}
Write-Host 'Done.'
