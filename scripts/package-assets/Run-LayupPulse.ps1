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

$utf8Encoding = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8Encoding
[Console]::OutputEncoding = $utf8Encoding
$OutputEncoding = $utf8Encoding
if (Get-Command chcp.com -ErrorAction SilentlyContinue) {
    & chcp.com 65001 | Out-Null
}

$packageRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$simulatorExecutable = Join-Path $packageRoot "Simulator/LayupPulse.Simulator.exe"
$desktopExecutable = Join-Path $packageRoot "Desktop/LayupPulse.Desktop.exe"
$desktopMutexName = "Local\LayupPulse.Desktop.SingleInstance.v1"
$launcherMutexName = "Local\LayupPulse.PackageLauncher.v1"
$launcherMutex = $null
$ownsLauncherMutex = $false
$simulatorProcess = $null
$desktopProcess = $null
$exitCode = 0

function Get-EndpointPort {
    param([Parameter(Mandatory)][Uri]$Uri)

    if (-not $Uri.IsDefaultPort) {
        return $Uri.Port
    }

    if ($Uri.Scheme -eq "https") {
        return 443
    }

    return 80
}

function Get-EndpointListeners {
    param([Parameter(Mandatory)][Uri]$Uri)

    $port = Get-EndpointPort -Uri $Uri
    $targetAddresses = @(
        [System.Net.Dns]::GetHostAddresses($Uri.DnsSafeHost) |
            ForEach-Object { $_.ToString() }
    )
    $wildcardAddresses = @("0.0.0.0", "::", "[::]")

    return @(
        Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
            Where-Object {
                $_.LocalAddress -in $targetAddresses -or
                $_.LocalAddress -in $wildcardAddresses
            }
    )
}

function Get-ListenerDescription {
    param([Parameter(Mandatory)]$Listener)

    $processName = "processus inconnu"
    $executablePath = $null
    try {
        $process = Get-Process -Id $Listener.OwningProcess -ErrorAction Stop
        $processName = $process.ProcessName
        try {
            $executablePath = $process.Path
        }
        catch {
            $executablePath = $null
        }
    }
    catch {
        # Le PID peut disparaître entre la lecture du socket et celle du processus.
    }

    return [pscustomobject]@{
        ProcessId = [int]$Listener.OwningProcess
        ProcessName = $processName
        ExecutablePath = $executablePath
    }
}

function Test-NamedMutexOwned {
    param([Parameter(Mandatory)][string]$Name)

    $createdNew = $false
    $mutex = New-Object System.Threading.Mutex($true, $Name, [ref]$createdNew)
    $acquired = $createdNew
    try {
        if (-not $createdNew) {
            try {
                $acquired = $mutex.WaitOne(0)
            }
            catch [System.Threading.AbandonedMutexException] {
                $acquired = $true
            }
        }

        if ($acquired) {
            $mutex.ReleaseMutex()
            return $false
        }

        return $true
    }
    finally {
        $mutex.Dispose()
    }
}

function Test-DesktopActive {
    if (Test-NamedMutexOwned -Name $desktopMutexName) {
        return $true
    }

    $expectedPath = [System.IO.Path]::GetFullPath($desktopExecutable)
    $desktopProcesses = @(
        Get-CimInstance Win32_Process -Filter "Name = 'LayupPulse.Desktop.exe'" `
            -ErrorAction SilentlyContinue
    )
    foreach ($process in $desktopProcesses) {
        if (-not [string]::IsNullOrWhiteSpace($process.ExecutablePath) -and
            [System.IO.Path]::GetFullPath($process.ExecutablePath).Equals(
                $expectedPath,
                [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Wait-EndpointReady {
    param(
        [Parameter(Mandatory)][Uri]$Uri,
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][TimeSpan]$Timeout
    )

    $port = Get-EndpointPort -Uri $Uri
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    while ($stopwatch.Elapsed -lt $Timeout) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Le simulateur s’est arrêté avant d’accepter les connexions (code $($Process.ExitCode))."
        }

        $listeners = @(Get-EndpointListeners -Uri $Uri)
        $ownedListener = $listeners |
            Where-Object { $_.OwningProcess -eq $Process.Id } |
            Select-Object -First 1
        $foreignListener = $listeners |
            Where-Object { $_.OwningProcess -ne $Process.Id } |
            Select-Object -First 1

        if ($null -ne $foreignListener) {
            throw "Le point d’écoute $Uri a été pris par un autre processus pendant le démarrage."
        }

        if ($null -ne $ownedListener) {
            $client = New-Object System.Net.Sockets.TcpClient
            try {
                $connect = $client.BeginConnect($Uri.Host, $port, $null, $null)
                if ($connect.AsyncWaitHandle.WaitOne(250)) {
                    $client.EndConnect($connect)
                    $Process.Refresh()
                    if ($Process.HasExited) {
                        throw "Le simulateur s’est arrêté pendant la vérification du point d’écoute (code $($Process.ExitCode))."
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
                # Le socket appartient au processus attendu mais n’accepte pas encore les connexions.
            }
            finally {
                $client.Dispose()
            }
        }

        Start-Sleep -Milliseconds 150
    }

    throw "Le simulateur n’a pas ouvert $Uri dans le délai de $($Timeout.TotalSeconds) secondes."
}

function Stop-OwnedProcess {
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
        # Le processus possédé s’est terminé entre les deux vérifications.
    }
    catch {
        Write-Warning "Impossible d’arrêter le processus possédé $($Process.Id) : $($_.Exception.Message)"
    }
}

try {
    $launcherCreatedNew = $false
    $launcherMutex = New-Object System.Threading.Mutex(
        $true,
        $launcherMutexName,
        [ref]$launcherCreatedNew)
    $ownsLauncherMutex = $launcherCreatedNew
    if (-not $launcherCreatedNew) {
        try {
            $ownsLauncherMutex = $launcherMutex.WaitOne(0)
        }
        catch [System.Threading.AbandonedMutexException] {
            $ownsLauncherMutex = $true
        }
    }

    if (-not $ownsLauncherMutex) {
        Write-Host "Un lancement de LayupPulse est déjà en cours dans cette session Windows."
        return
    }

    foreach ($executable in @($simulatorExecutable, $desktopExecutable)) {
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw "Package incomplet : exécutable introuvable : $executable"
        }
    }

    try {
        $endpointUri = [Uri]$Endpoint
    }
    catch {
        throw "Point d’écoute invalide : $Endpoint"
    }
    if (-not $endpointUri.IsAbsoluteUri -or -not $endpointUri.IsLoopback -or
        $endpointUri.Scheme -notin "http", "https") {
        throw "Le point d’écoute de démonstration doit être une adresse HTTP(S) de bouclage."
    }

    if (Test-DesktopActive) {
        Write-Host "LayupPulse est déjà ouvert dans cette session Windows."
        return
    }

    $existingListener = Get-EndpointListeners -Uri $endpointUri | Select-Object -First 1
    if ($null -ne $existingListener) {
        $listener = Get-ListenerDescription -Listener $existingListener
        if ($listener.ProcessName -eq "LayupPulse.Simulator" -or
            (-not [string]::IsNullOrWhiteSpace($listener.ExecutablePath) -and
             [System.IO.Path]::GetFullPath($listener.ExecutablePath).Equals(
                [System.IO.Path]::GetFullPath($simulatorExecutable),
                [StringComparison]::OrdinalIgnoreCase))) {
            throw "Un simulateur LayupPulse est déjà actif sur $Endpoint (PID $($listener.ProcessId)). Fermez-le avant de relancer le package."
        }

        throw "Le point d’écoute $Endpoint est déjà occupé par $($listener.ProcessName) (PID $($listener.ProcessId))."
    }

    $simulatorProcess = Start-Process -FilePath $simulatorExecutable `
        -ArgumentList "--Simulator:Endpoint=$Endpoint" `
        -WorkingDirectory (Split-Path $simulatorExecutable -Parent) `
        -PassThru
    Write-Host "Simulateur démarré : PID $($simulatorProcess.Id)"
    Write-Host "Point d’écoute gRPC : $Endpoint"
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
                throw "L’application de bureau s’est arrêtée pendant le smoke test (code $($desktopProcess.ExitCode))."
            }

            $simulatorProcess.Refresh()
            if ($simulatorProcess.HasExited) {
                throw "Le simulateur s’est arrêté pendant le smoke test (code $($simulatorProcess.ExitCode))."
            }

            Start-Sleep -Milliseconds 250
        }

        Write-Host "Smoke test réussi : les exécutables autonomes sont restés actifs pendant $SmokeTestDurationSeconds secondes."
    }
    else {
        Write-Host "Fermez LayupPulse pour arrêter automatiquement le simulateur."
        while (-not $desktopProcess.WaitForExit(500)) {
            # Cette attente courte permet à l’interruption PowerShell de déclencher finally.
        }

        if ($desktopProcess.ExitCode -ne 0) {
            throw "L’application de bureau s’est arrêtée avec le code $($desktopProcess.ExitCode)."
        }
    }
}
catch {
    $exitCode = 1
    Write-Host "Erreur : $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Stop-OwnedProcess -Process $desktopProcess
    Stop-OwnedProcess -Process $simulatorProcess

    if ($ownsLauncherMutex -and $null -ne $launcherMutex) {
        $launcherMutex.ReleaseMutex()
        $ownsLauncherMutex = $false
    }

    if ($null -ne $launcherMutex) {
        $launcherMutex.Dispose()
    }
}

exit $exitCode
