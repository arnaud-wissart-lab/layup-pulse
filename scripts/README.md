# Scripts

Ce dossier contient les automatisations reproductibles du dépôt.

## `run-demo.ps1`

Lance le simulateur et l’application de bureau, vérifie le readiness du point d’écoute, affiche les PID et nettoie les deux processus à l’arrêt ou lors d’une interruption.

```powershell
./scripts/run-demo.ps1
./scripts/run-demo.ps1 -Build
./scripts/run-demo.ps1 -SmokeTest
```

Le build n’est exécuté que sur demande ou lorsque les sorties sont absentes ou plus anciennes que les entrées de compilation.

## `package-demo.ps1`

Publie Desktop et Simulator en `win-x64` autonome, copie les instructions et licences, exclut les fichiers de développement, lance un smoke test depuis le dossier publié, puis crée `artifacts/LayupPulse-win-x64.zip`.

```powershell
./scripts/package-demo.ps1
```

Le script effectue la restauration spécifique à `win-x64`, nécessaire même après une restauration générique de la solution. `-NoRestore` ne doit être utilisé que si ces assets RID ont déjà été restaurés. `-SkipSmokeTest` est réservé aux environnements où le lancement WPF est impossible ; son utilisation doit être signalée explicitement.

## `run-simulator.ps1`

Lance uniquement le processus gRPC en environnement de développement. Le script accepte `Endpoint`, `Seed` et `TelemetryRateHz` ; ses valeurs par défaut correspondent à la configuration versionnée du simulateur.

Le sous-dossier `package-assets/` contient les fichiers copiés à la racine du package autonome. Ils ne doivent pas dépendre de chemins du dépôt ni d’un runtime .NET installé.
