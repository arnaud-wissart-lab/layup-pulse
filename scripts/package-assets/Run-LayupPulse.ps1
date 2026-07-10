[CmdletBinding()]
param(
    [string]$Endpoint = "http://127.0.0.1:5057",
    [ValidateRange(1, 120)]
    [int]$StartupTimeoutSeconds = 20,
    [switch]$SmokeTest,
    [ValidateRange(1, 60)]
    [int]$SmokeTestDurationSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$packageRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$simulatorExecutable = Join-Path $packageRoot "Simulator/LayupPulse.Simulator.exe"
$desktopExecutable = Join-Path $packageRoot "Desktop/LayupPulse.Desktop.exe"
$simulatorProcess = $null
$desktopProcess = $null
$exitCode = 0

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

        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $connect = $client.BeginConnect($Uri.Host, $port, $null, $null)
            if ($connect.AsyncWaitHandle.WaitOne(250)) {
                $client.EndConnect($connect)
                Start-Sleep -Milliseconds 200
                $Process.Refresh()
                if ($Process.HasExited) {
                    throw "Le simulateur s'est arrêté pendant la vérification du point d'écoute (code $($Process.ExitCode))."
                }

                return
            }
        }
        catch [System.Net.Sockets.SocketException] {
            # Le serveur est encore en cours de démarrage.
        }
        finally {
            $client.Dispose()
        }

        Start-Sleep -Milliseconds 150
    }

    throw "Le simulateur n'a pas ouvert $Uri dans le délai de $($Timeout.TotalSeconds) secondes."
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

try {
    foreach ($executable in @($simulatorExecutable, $desktopExecutable)) {
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw "Package incomplet : exécutable introuvable : $executable"
        }
    }

    $endpointUri = [Uri]$Endpoint
    if (-not $endpointUri.IsAbsoluteUri -or -not $endpointUri.IsLoopback -or
        $endpointUri.Scheme -notin 'http', 'https') {
        throw "L'endpoint de démonstration doit être une adresse HTTP(S) de bouclage."
    }

    $simulatorProcess = Start-Process -FilePath $simulatorExecutable `
        -ArgumentList "--Simulator:Endpoint=$Endpoint" `
        -WorkingDirectory (Split-Path $simulatorExecutable -Parent) `
        -PassThru
    Write-Host "Simulateur démarré : PID $($simulatorProcess.Id)"
    Write-Host "Endpoint gRPC : $Endpoint"
    Wait-EndpointReady -Uri $endpointUri -Process $simulatorProcess `
        -Timeout ([TimeSpan]::FromSeconds($StartupTimeoutSeconds))
    Write-Host "Simulateur prêt."

    $desktopProcess = Start-Process -FilePath $desktopExecutable `
        -ArgumentList "--Machine:Endpoint=$Endpoint" `
        -WorkingDirectory (Split-Path $desktopExecutable -Parent) `
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

        Write-Host "Smoke test réussi : les exécutables autonomes sont restés actifs pendant $SmokeTestDurationSeconds secondes."
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
catch {
    $exitCode = 1
    Write-Error $_
}
finally {
    Stop-ChildProcess -Process $desktopProcess
    Stop-ChildProcess -Process $simulatorProcess
}

exit $exitCode
