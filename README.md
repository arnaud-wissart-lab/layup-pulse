# LayupPulse

LayupPulse is an independent Windows desktop demonstrator for supervising a simulated automated composite layup cell. It is a portfolio project designed to demonstrate software architecture, asynchronous communication, deterministic simulation, diagnostics, persistence, testing, and industrial-style user experience.

LayupPulse is not affiliated with any industrial company and does not reproduce any existing product, visual identity, proprietary data, or presumed implementation. It is not designed, validated, or safe for controlling real industrial hardware.

## Current status

This repository currently contains the buildable project foundation and the initial product and architecture documentation. Telemetry, gRPC, persistence, charts, machine simulation, alarms, and 3D visualization are intentionally deferred to later implementation phases.

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

## License

LayupPulse is available under the [MIT License](LICENSE).
