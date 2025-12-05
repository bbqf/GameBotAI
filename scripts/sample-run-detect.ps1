Param(
    [Parameter(Mandatory=$true)] [string] $SessionId,
    [Parameter(Mandatory=$true)] [string] $ImagePath,
    [string] $BaseUrl = "http://localhost:5273"
)

Write-Host "Uploading image '$ImagePath' as id 'home_button'..."
if (-not (Test-Path -LiteralPath $ImagePath)) { Write-Error "File not found: $ImagePath"; exit 1 }
$bytes = [IO.File]::ReadAllBytes($ImagePath)
$b64 = [Convert]::ToBase64String($bytes)
$payload = @{ id = "home_button"; contentBase64 = $b64 } | ConvertTo-Json -Compress

try {
    $resp = Invoke-RestMethod -Uri "$BaseUrl/images" -Method POST -ContentType 'application/json' -Body $payload
    Write-Host "Image uploaded." | Out-String > $null
} catch {
    Write-Warning "Upload failed: $($_.Exception.Message). Continuing if already exists."
}

Write-Host "Force-executing sample detection command..."
$cmdId = "00000000000000000000000000000001"
$url = "$BaseUrl/commands/$cmdId/force-execute?sessionId=$SessionId"
try {
    $resp2 = Invoke-RestMethod -Uri $url -Method POST
    Write-Host ("Accepted inputs: {0}" -f $resp2.accepted)
} catch {
    Write-Error "Force-execute failed: $($_.Exception.Message)"; exit 1
}

Write-Host "Done."