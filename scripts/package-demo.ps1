[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$')]
    [string]$Version = "0.2.0",
    [ValidateRange(1, 120)]
    [int]$StartupTimeoutSeconds = 20,
    [switch]$NoRestore,
    [switch]$SkipSmokeTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$packageRoot = Join-Path $artifactsRoot "LayupPulse-win-x64"
$desktopOutput = Join-Path $packageRoot "Desktop"
$simulatorOutput = Join-Path $packageRoot "Simulator"
$archivePath = Join-Path $artifactsRoot "LayupPulse-win-x64.zip"
$desktopProject = Join-Path $repositoryRoot "src/LayupPulse.Desktop/LayupPulse.Desktop.csproj"
$simulatorProject = Join-Path $repositoryRoot "src/LayupPulse.Simulator/LayupPulse.Simulator.csproj"
$assetsRoot = Join-Path $PSScriptRoot "package-assets"

function Assert-SafeArtifactPath {
    param([Parameter(Mandatory)][string]$Path)

    $resolvedArtifactsRoot = [System.IO.Path]::GetFullPath($artifactsRoot).TrimEnd('\', '/') +
        [System.IO.Path]::DirectorySeparatorChar
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Chemin d'artefact non sûr : $resolvedPath"
    }
}

function Invoke-DotNetPublish {
    param(
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$Output,
        [Parameter(Mandatory)][string]$InformationalVersion,
        [Parameter(Mandatory)][string]$Revision
    )

    $arguments = @(
        'publish',
        $Project,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $Output,
        '-p:PublishSingleFile=false',
        '-p:DebugSymbols=false',
        '-p:DebugType=None',
        '-p:ContinuousIntegrationBuild=true',
        '-p:IncludeSourceRevisionInInformationalVersion=false',
        "-p:Version=$Version",
        "-p:InformationalVersion=$InformationalVersion",
        "-p:SourceRevisionId=$Revision"
    )
    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "La publication de $Project a échoué avec le code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot "LayupPulse.sln") -PathType Leaf)) {
    throw "La racine du dépôt LayupPulse n'a pas pu être localisée depuis $PSScriptRoot."
}

Assert-SafeArtifactPath -Path $packageRoot
Assert-SafeArtifactPath -Path $archivePath

$dotnetVersion = & dotnet --version 2>&1
if ($LASTEXITCODE -ne 0 -or ($dotnetVersion | Select-Object -Last 1) -notmatch '^10\.') {
    throw "Le SDK .NET 10 demandé par global.json est requis pour produire le package."
}

$revision = (& git -C $repositoryRoot rev-parse --short=12 HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revision)) {
    $revision = "source-local"
}
else {
    $revision = $revision.Trim()
}
$informationalVersion = "$Version+$revision"

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

New-Item -ItemType Directory -Path $desktopOutput, $simulatorOutput -Force | Out-Null

Push-Location $repositoryRoot
try {
    Write-Host "Publication Desktop win-x64 autonome ($informationalVersion)..."
    Invoke-DotNetPublish -Project $desktopProject -Output $desktopOutput `
        -InformationalVersion $informationalVersion -Revision $revision

    Write-Host "Publication Simulator win-x64 autonome ($informationalVersion)..."
    Invoke-DotNetPublish -Project $simulatorProject -Output $simulatorOutput `
        -InformationalVersion $informationalVersion -Revision $revision
}
finally {
    Pop-Location
}

Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Include '*.pdb', 'appsettings.Development.json' |
    Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $assetsRoot "Run-LayupPulse.ps1") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $assetsRoot "Run-LayupPulse.cmd") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $assetsRoot "README.txt") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination (Join-Path $packageRoot "LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md") `
    -Destination (Join-Path $packageRoot "THIRD-PARTY-NOTICES.txt")

$forbiddenFiles = @(
    Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object {
        $_.Extension -in '.cs', '.csproj', '.sln', '.pdb', '.db', '.sqlite', '.sqlite3', '.log' -or
        $_.Name -eq 'appsettings.Development.json' -or
        $_.FullName -match '[\\/](TestResults|tests?|obj|bin|logs?)[\\/]'
    }
)
if ($forbiddenFiles.Count -gt 0) {
    $paths = $forbiddenFiles.FullName -join [Environment]::NewLine
    throw "Le package contient des fichiers interdits :`n$paths"
}

foreach ($requiredFile in @(
    (Join-Path $desktopOutput "LayupPulse.Desktop.exe"),
    (Join-Path $simulatorOutput "LayupPulse.Simulator.exe"),
    (Join-Path $packageRoot "Run-LayupPulse.cmd"),
    (Join-Path $packageRoot "README.txt")
)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Le package est incomplet : $requiredFile"
    }
}

if (-not $SkipSmokeTest) {
    Write-Host "Smoke test depuis le dossier autonome..."
    & (Join-Path $packageRoot "Run-LayupPulse.ps1") `
        -SmokeTest `
        -StartupTimeoutSeconds $StartupTimeoutSeconds
    if ($LASTEXITCODE -ne 0) {
        throw "Le smoke test du package a échoué avec le code $LASTEXITCODE."
    }
}
else {
    Write-Warning "Le smoke test du package a été explicitement ignoré."
}

Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal

$archive = Get-Item -LiteralPath $archivePath
Write-Host "Package : $packageRoot"
Write-Host "Archive : $archivePath ($([Math]::Round($archive.Length / 1MB, 1)) MiB)"
