[CmdletBinding()]
param(
    [string]$Endpoint = "http://127.0.0.1:5057",
    [int]$Seed = 24117,
    [ValidateRange(1, 50)]
    [int]$TelemetryRateHz = 20
)

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repositoryRoot "src/LayupPulse.Simulator/LayupPulse.Simulator.csproj"

Push-Location $repositoryRoot
try {
    dotnet run `
        --project $project `
        --configuration Debug `
        --no-launch-profile `
        -- `
        --environment=Development `
        --Simulator:Endpoint=$Endpoint `
        --Simulator:Seed=$Seed `
        --Simulator:TelemetryRateHz=$TelemetryRateHz
}
finally {
    Pop-Location
}
