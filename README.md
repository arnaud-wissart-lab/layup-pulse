# LayupPulse

LayupPulse is an independent Windows desktop demonstrator for supervising a simulated automated composite layup cell. It is a portfolio project designed to demonstrate software architecture, asynchronous communication, deterministic simulation, diagnostics, persistence, testing, and industrial-style user experience.

LayupPulse is not affiliated with any industrial company and does not reproduce any existing product, visual identity, proprietary data, or presumed implementation. It is not designed, validated, or safe for controlling real industrial hardware.

## État actuel

Le dépôt contient désormais un premier démonstrateur utilisable de bout en bout :

- un simulateur gRPC autonome et déterministe ;
- une application WPF hébergée par le Generic Host ;
- une connexion configurable avec lecture d’instantané et télémétrie continue ;
- les commandes de connexion, chargement de la recette fictive, démarrage, pause, reprise, arrêt, reset et déconnexion ;
- une vue d’ensemble temps réel et une page de diagnostics ;
- des pages Alarmes et Historique qui indiquent explicitement leur report.

La persistance EF Core/SQLite, les alarmes applicatives, l’historique, les graphiques avancés et la visualisation 3D ne sont pas implémentés dans cet incrément. Le simulateur n’est pas conçu pour du matériel industriel réel et ne revendique aucune compatibilité avec celui-ci.

## Technology baseline

- .NET 10 and C# 14
- WPF targeting `net10.0-windows`
- SDK-style projects
- Nullable reference types and deterministic builds
- Central NuGet package management
- xUnit for automated tests

## Projects

| Project | Responsibility |
| --- | --- |
| `LayupPulse.Domain` | Technology-independent machine and production rules |
| `LayupPulse.Application` | Use cases and technology-neutral ports |
| `LayupPulse.Contracts` | Transport contracts shared across process boundaries |
| `LayupPulse.Infrastructure` | Client gRPC et futurs adaptateurs de persistance |
| `LayupPulse.Simulator` | Separate simulated machine process |
| `LayupPulse.Desktop` | WPF operator application and composition root |
| `LayupPulse.Tests` | Automated tests and architecture boundary checks |

## Documentation

- [Product specification](docs/product-spec.md)
- [Architecture](docs/architecture.md)
- [Implementation plan](docs/implementation-plan.md)
- [UI specification](docs/ui-specification.md)
- [Architecture decisions](docs/decisions/README.md)

## Prérequis

- Windows 10 ou Windows 11 pour l’application WPF ;
- SDK .NET 10 correspondant à la version épinglée dans `global.json` ;
- port local `5057` disponible, ou un autre point d’accès configuré pour les deux processus ;
- aucune base de données ni service externe.

Le transport local par défaut utilise HTTP/2 sans chiffrement sur `http://127.0.0.1:5057`. Cette configuration est réservée au démonstrateur local.

## Build et validation

The pinned .NET 10 SDK must be installed. On Windows, run from the repository root:

```powershell
dotnet restore LayupPulse.sln
dotnet format LayupPulse.sln --verify-no-changes --no-restore
dotnet build LayupPulse.sln -c Release --no-restore
dotnet test LayupPulse.sln -c Release --no-build
git diff --check
```

## Lancer le simulateur local

Le point d’écoute par défaut est `http://127.0.0.1:5057` en HTTP/2 local sans chiffrement. La graine par défaut est `24117` et la télémétrie est publiée à `20 Hz`.

```powershell
./scripts/run-simulator.ps1
```

Les trois paramètres sont configurables sans chemin propre à une machine :

```powershell
./scripts/run-simulator.ps1 `
  -Endpoint "http://127.0.0.1:5058" `
  -Seed 1729 `
  -TelemetryRateHz 25
```

La même configuration peut être fournie directement à ASP.NET Core :

```powershell
dotnet run --project src/LayupPulse.Simulator/LayupPulse.Simulator.csproj -- `
  --Simulator:Endpoint=http://127.0.0.1:5058 `
  --Simulator:Seed=1729 `
  --Simulator:TelemetryRateHz=25
```

La fréquence acceptée est comprise entre 1 et 50 Hz. Les valeurs par défaut sont versionnées dans `appsettings.json` et `appsettings.Development.json` du projet Simulator. La console affiche au démarrage le point d’écoute, la graine et la fréquence effectifs.

## Lancer l’application de bureau

Dans un second terminal PowerShell, depuis la racine du dépôt :

```powershell
dotnet run --project src/LayupPulse.Desktop/LayupPulse.Desktop.csproj
```

Le point d’accès est défini dans `src/LayupPulse.Desktop/appsettings.json`. Il peut être remplacé par la ligne de commande :

```powershell
dotnet run --project src/LayupPulse.Desktop/LayupPulse.Desktop.csproj -- `
  --Machine:Endpoint=http://127.0.0.1:5058
```

Séquence de démonstration disponible :

1. cliquer sur **Connecter** ;
2. charger **Wing Panel Demo** ;
3. démarrer le cycle ;
4. observer la télémétrie et la progression ;
5. mettre en pause, reprendre, puis arrêter ;
6. se déconnecter.

Les boutons ne sont activés que lorsque la règle métier correspondante est satisfaite. Une indisponibilité du simulateur ou un rejet de commande reste visible et récupérable dans l’interface.

## License

LayupPulse is available under the [MIT License](LICENSE).
