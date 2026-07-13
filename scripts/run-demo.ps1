[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Build,
    [string]$Endpoint = "http://127.0.0.1:5057",
    [int]$Seed = 24117,
    [ValidateRange(1, 50)]
    [int]$TelemetryRateHz = 20,
    [ValidateRange(1, 120)]
    [int]$StartupTimeoutSeconds = 20,
    [switch]$SmokeTest,
    [ValidateRange(1, 60)]
    [int]$SmokeTestDurationSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repositoryRoot "LayupPulse.sln"
$globalJsonPath = Join-Path $repositoryRoot "global.json"
$simulatorProject = Join-Path $repositoryRoot "src/LayupPulse.Simulator/LayupPulse.Simulator.csproj"
$desktopProject = Join-Path $repositoryRoot "src/LayupPulse.Desktop/LayupPulse.Desktop.csproj"
$simulatorOutput = Join-Path $repositoryRoot "src/LayupPulse.Simulator/bin/$Configuration/net10.0"
$desktopOutput = Join-Path $repositoryRoot "src/LayupPulse.Desktop/bin/$Configuration/net10.0-windows"
$simulatorExecutable = Join-Path $simulatorOutput "LayupPulse.Simulator.exe"
$desktopExecutable = Join-Path $desktopOutput "LayupPulse.Desktop.exe"
$simulatorProcess = $null
$desktopProcess = $null
$exitCode = 0
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("LayupPulse-" + [Guid]::NewGuid().ToString("N"))
$simulatorStandardOutput = Join-Path $temporaryDirectory "simulator.stdout.log"
$simulatorStandardError = Join-Path $temporaryDirectory "simulator.stderr.log"

function Test-DotNet10Sdk {
    $dotnetVersion = & dotnet --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Le SDK .NET demandé par global.json est introuvable. Installez le SDK .NET 10 puis réessayez.`n$dotnetVersion"
    }

    $version = ($dotnetVersion | Select-Object -Last 1).Trim()
    if ($version -notmatch '^10\.') {
        throw "LayupPulse nécessite le SDK .NET 10. Version résolue : $version."
    }

    Write-Host "SDK .NET : $version"
}

function Test-BuildRequired {
    $requiredOutputs = @(
        $simulatorExecutable,
        (Join-Path $simulatorOutput "LayupPulse.Simulator.deps.json"),
        (Join-Path $simulatorOutput "LayupPulse.Simulator.runtimeconfig.json"),
        (Join-Path $simulatorOutput "appsettings.json"),
        $desktopExecutable,
        (Join-Path $desktopOutput "LayupPulse.Desktop.deps.json"),
        (Join-Path $desktopOutput "LayupPulse.Desktop.runtimeconfig.json"),
        (Join-Path $desktopOutput "appsettings.json")
    )

    if ($requiredOutputs.Where({ -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -gt 0) {
        return $true
    }

    $oldestOutput = @($simulatorExecutable, $desktopExecutable) |
        ForEach-Object { Get-Item -LiteralPath $_ } |
        Sort-Object LastWriteTimeUtc |
        Select-Object -First 1
    $buildInputs = @(
        Get-Item -LiteralPath $solutionPath, $globalJsonPath,
            (Join-Path $repositoryRoot "Directory.Build.props"),
            (Join-Path $repositoryRoot "Directory.Packages.props")
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src") -Recurse -File |
            Where-Object {
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
                $_.Extension -in '.cs', '.csproj', '.json', '.proto', '.xaml'
            }
    )

    return $buildInputs.Where({ $_.LastWriteTimeUtc -gt $oldestOutput.LastWriteTimeUtc }).Count -gt 0
}

function Wait-EndpointReady {
    param(
        [Parameter(Mandatory)]
        [Uri]$Uri,
        [Parameter(Mandatory)]
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)]
        [TimeSpan]$Timeout
    )

    $port = if ($Uri.IsDefaultPort) {
        if ($Uri.Scheme -eq 'https') { 443 } else { 80 }
    }
    else {
        $Uri.Port
    }
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    while ($stopwatch.Elapsed -lt $Timeout) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Le simulateur s'est arrêté avant d'accepter les connexions (code $($Process.ExitCode))."
        }

        $listeners = @(Get-EndpointListeners -Uri $Uri)
        $ownedListener = $listeners |
            Where-Object { $_.OwningProcess -eq $Process.Id } |
            Select-Object -First 1
        $foreignListener = $listeners |
            Where-Object { $_.OwningProcess -ne $Process.Id } |
            Select-Object -First 1
        if ($null -ne $foreignListener) {
            throw "Le point d'écoute $Uri a été pris par un autre processus pendant le démarrage."
        }

        if ($null -ne $ownedListener) {
            $client = [System.Net.Sockets.TcpClient]::new()
            try {
                $connect = $client.BeginConnect($Uri.Host, $port, $null, $null)
                if ($connect.AsyncWaitHandle.WaitOne(250)) {
                    $client.EndConnect($connect)
                    $Process.Refresh()
                    if ($Process.HasExited) {
                        throw "Le simulateur s'est arrêté pendant la vérification du point d'écoute (code $($Process.ExitCode))."
                    }

                    $verifiedListener = Get-EndpointListeners -Uri $Uri |
                        Where-Object { $_.OwningProcess -eq $Process.Id } |
                        Select-Object -First 1
                    if ($null -ne $verifiedListener) {
                        return
                    }
                }
            }
            catch [System.Net.Sockets.SocketException] {
                # Le socket appartient au processus attendu mais n'accepte pas encore les connexions.
            }
            finally {
                $client.Dispose()
            }
        }

        Start-Sleep -Milliseconds 150
    }

    throw "Le simulateur n'a pas ouvert $Uri dans le délai de $($Timeout.TotalSeconds) secondes."
}

function Get-EndpointListeners {
    param([Parameter(Mandatory)][Uri]$Uri)

    $port = if ($Uri.IsDefaultPort) {
        if ($Uri.Scheme -eq 'https') { 443 } else { 80 }
    }
    else {
        $Uri.Port
    }
    $targetAddresses = @(
        [System.Net.Dns]::GetHostAddresses($Uri.DnsSafeHost) |
            ForEach-Object { $_.ToString() }
    )

    return @(
        Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
            Where-Object {
                $_.LocalAddress -in $targetAddresses -or
                $_.LocalAddress -in @('0.0.0.0', '::', '[::]')
            }
    )
}

function Stop-ChildProcess {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }

    try {
        $Process.Refresh()
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction Stop
            $Process.WaitForExit(5000) | Out-Null
        }
    }
    catch [System.InvalidOperationException] {
        # Le processus s'est terminé entre les deux vérifications.
    }
    catch {
        Write-Warning "Impossible d'arrêter le processus $($Process.Id) : $($_.Exception.Message)"
    }
}

function Write-SimulatorDiagnostics {
    foreach ($logPath in @($simulatorStandardOutput, $simulatorStandardError)) {
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            $content = Get-Content -LiteralPath $logPath -Raw
            if (-not [string]::IsNullOrWhiteSpace($content)) {
                Write-Host "--- $(Split-Path $logPath -Leaf) ---"
                Write-Host $content.TrimEnd()
            }
        }
    }
}

try {
    if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
        throw "La racine du dépôt LayupPulse n'a pas pu être localisée depuis $PSScriptRoot."
    }

    try {
        $endpointUri = [Uri]$Endpoint
    }
    catch {
        throw "Endpoint invalide : $Endpoint"
    }
    if (-not $endpointUri.IsLoopback -or $endpointUri.Scheme -notin 'http', 'https') {
        throw "L'endpoint de démonstration doit être une adresse HTTP(S) de bouclage."
    }

    Push-Location $repositoryRoot
    try {
        Test-DotNet10Sdk

        $buildRequired = Test-BuildRequired
        if ($Build -or $buildRequired) {
            $reason = if ($Build) { "demandé" } else { "sorties absentes ou obsolètes" }
            Write-Host "Build $Configuration ($reason)..."
            & dotnet build $solutionPath --configuration $Configuration
            if ($LASTEXITCODE -ne 0) {
                throw "Le build $Configuration a échoué avec le code $LASTEXITCODE."
            }
        }
        else {
            Write-Host "Build ignoré : les sorties $Configuration sont à jour."
        }

        New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
        $existingListener = Get-EndpointListeners -Uri $endpointUri | Select-Object -First 1
        if ($null -ne $existingListener) {
            throw "Le point d'écoute $Endpoint est déjà occupé par le PID $($existingListener.OwningProcess)."
        }

        $simulatorArguments = @(
            "--Simulator:Endpoint=$Endpoint",
            "--Simulator:Seed=$Seed",
            "--Simulator:TelemetryRateHz=$TelemetryRateHz"
        )
        $simulatorProcess = Start-Process -FilePath $simulatorExecutable `
            -ArgumentList $simulatorArguments `
            -WorkingDirectory $simulatorOutput `
            -RedirectStandardOutput $simulatorStandardOutput `
            -RedirectStandardError $simulatorStandardError `
            -PassThru

        Write-Host "Simulateur démarré : PID $($simulatorProcess.Id)"
        Write-Host "Endpoint gRPC : $Endpoint"
        Wait-EndpointReady -Uri $endpointUri -Process $simulatorProcess `
            -Timeout ([TimeSpan]::FromSeconds($StartupTimeoutSeconds))
        Write-Host "Simulateur prêt."

        $desktopProcess = Start-Process -FilePath $desktopExecutable `
            -ArgumentList "--Machine:Endpoint=$Endpoint" `
            -WorkingDirectory $desktopOutput `
            -PassThru
        Write-Host "Application de bureau démarrée : PID $($desktopProcess.Id)"

        if ($SmokeTest) {
            $smokeDeadline = [DateTimeOffset]::UtcNow.AddSeconds($SmokeTestDurationSeconds)
            while ([DateTimeOffset]::UtcNow -lt $smokeDeadline) {
                $desktopProcess.Refresh()
                if ($desktopProcess.HasExited) {
                    throw "L'application de bureau s'est arrêtée pendant le smoke test (code $($desktopProcess.ExitCode))."
                }

                $simulatorProcess.Refresh()
                if ($simulatorProcess.HasExited) {
                    throw "Le simulateur s'est arrêté pendant le smoke test (code $($simulatorProcess.ExitCode))."
                }

                Start-Sleep -Milliseconds 250
            }

            Write-Host "Smoke test réussi : les deux processus sont restés actifs pendant $SmokeTestDurationSeconds secondes."
        }
        else {
            Write-Host "Fermez LayupPulse pour arrêter automatiquement le simulateur."
            while (-not $desktopProcess.WaitForExit(500)) {
                # Cette attente courte permet à l'interruption PowerShell de déclencher finally.
            }

            if ($desktopProcess.ExitCode -ne 0) {
                throw "L'application de bureau s'est arrêtée avec le code $($desktopProcess.ExitCode)."
            }
        }
    }
    finally {
        Pop-Location
    }
}
catch {
    $exitCode = 1
    Write-Error $_
    Write-SimulatorDiagnostics
}
finally {
    Stop-ChildProcess -Process $desktopProcess
    Stop-ChildProcess -Process $simulatorProcess

    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

exit $exitCode
