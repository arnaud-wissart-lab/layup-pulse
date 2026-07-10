# LayupPulse

LayupPulse is an independent Windows desktop demonstrator for supervising a simulated automated composite layup cell. It is a portfolio project designed to demonstrate software architecture, asynchronous communication, deterministic simulation, diagnostics, persistence, testing, and industrial-style user experience.

LayupPulse is not affiliated with any industrial company and does not reproduce any existing product, visual identity, proprietary data, or presumed implementation. It is not designed, validated, or safe for controlling real industrial hardware.

## État actuel

Le dépôt contient le socle .NET, le modèle de domaine déterministe et un processus gRPC autonome simulant une cellule fictive. La persistance, le client gRPC du bureau, les graphiques, les alarmes applicatives et la visualisation 3D restent différés. Le simulateur n’est pas conçu pour du matériel industriel réel et ne revendique aucune compatibilité avec celui-ci.

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
| `LayupPulse.Infrastructure` | Future gRPC, EF Core, SQLite, and operating-system adapters |
| `LayupPulse.Simulator` | Separate simulated machine process |
| `LayupPulse.Desktop` | WPF operator application and composition root |
| `LayupPulse.Tests` | Automated tests and architecture boundary checks |

## Documentation

- [Product specification](docs/product-spec.md)
- [Architecture](docs/architecture.md)
- [Implementation plan](docs/implementation-plan.md)
- [UI specification](docs/ui-specification.md)
- [Architecture decisions](docs/decisions/README.md)

## Build and validation

The pinned .NET 10 SDK must be installed. On Windows, run from the repository root:

```powershell
dotnet restore LayupPulse.sln
dotnet format LayupPulse.sln --verify-no-changes --no-restore
dotnet build LayupPulse.sln -c Release --no-restore
dotnet test LayupPulse.sln -c Release --no-build
git diff --check
```

The WPF application can later be launched with:

```powershell
dotnet run --project src/LayupPulse.Desktop/LayupPulse.Desktop.csproj
```

The current window is only a minimal application shell. It does not connect to or control a machine.

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

## License

LayupPulse is available under the [MIT License](LICENSE).
