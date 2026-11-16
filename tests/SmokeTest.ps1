# Run after Archer is started with `dotnet run` or Visual Studio.
# This script only tests local non-AI endpoints. No API credits are consumed.
$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:5087'
$health = Invoke-RestMethod "$base/api/health"
if ($health.status -ne 'ok') { throw 'Health endpoint failed.' }
$body = @{
    title = 'Smoke test mission'
    objective = 'Verify that local mission creation and JSON persistence work correctly.'
} | ConvertTo-Json
$mission = Invoke-RestMethod -Method Post -Uri "$base/api/missions" -ContentType 'application/json' -Body $body
$state = Invoke-RestMethod "$base/api/state"
if (-not ($state.missions | Where-Object { $_.id -eq $mission.id })) {
    throw 'Created mission was not found in state.'
}
Write-Host "PASS: Health check and local mission persistence ($($mission.id))" -ForegroundColor Green
Write-Host 'Note: This created a test mission in your local Archer data.'
