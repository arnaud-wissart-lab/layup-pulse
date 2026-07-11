# Informations sur les dépendances tierces

LayupPulse est distribué sous licence MIT. Les dépendances directes conservent leurs propres licences. Ce relevé est établi à partir des métadonnées NuGet restaurées pour les versions centralisées dans `Directory.Packages.props` ; les textes complets restent ceux fournis par chaque package et dépôt source.

## Dépendances d’exécution

| Package | Version | Licence | Usage principal |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | ViewModels et commandes WPF |
| Google.Protobuf | 3.35.1 | BSD-3-Clause | Sérialisation des contrats |
| Grpc.AspNetCore | 2.80.0 | Apache-2.0 | Serveur gRPC du simulateur |
| Grpc.Core.Api | 2.80.0 | Apache-2.0 | API du contrat gRPC |
| Grpc.Net.Client | 2.80.0 | Apache-2.0 | Client gRPC Desktop |
| HelixToolkit.Wpf | 3.1.2 | MIT | Visualisation WPF 3D |
| Microsoft.Extensions.Configuration.Binder | 10.0.9 | MIT | Liaison de configuration |
| Microsoft.Extensions.Configuration.Json | 10.0.9 | MIT | Configuration JSON |
| Microsoft.Extensions.Hosting | 10.0.9 | MIT | Hébergement Desktop |
| Microsoft.Extensions.Logging.Abstractions | 10.0.9 | MIT | Journalisation des adaptateurs |
| Microsoft.Extensions.Logging.Debug | 10.0.9 | MIT | Sortie de diagnostic Desktop |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.9 | MIT | Persistance locale SQLite |
| ScottPlot.WPF | 5.1.59 | MIT | Tendances temps réel |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.3 | Apache-2.0 | Bibliothèque SQLite native corrigée |

## Dépendances de build et de test

| Package | Version | Licence | Usage principal |
| --- | --- | --- | --- |
| Grpc.Tools | 2.80.0 | Apache-2.0 | Génération C# depuis protobuf |
| Microsoft.EntityFrameworkCore.Design | 10.0.9 | MIT | Génération des migrations EF Core |
| Microsoft.NET.Test.Sdk | 18.7.0 | MIT | Hôte de tests |
| xunit | 2.9.3 | Apache-2.0 | Framework de tests |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | Adaptateur de tests |

Les dépendances transitives sont incluses dans le graphe restauré et peuvent évoluer lors d’une mise à jour d’un package direct. Avant une distribution, vérifier le graphe avec :

```powershell
dotnet list LayupPulse.sln package --include-transitive
dotnet list LayupPulse.sln package --vulnerable --include-transitive
```

Références : [NuGet](https://www.nuget.org/), [Community Toolkit](https://github.com/CommunityToolkit/dotnet), [Protocol Buffers](https://github.com/protocolbuffers/protobuf), [gRPC for .NET](https://github.com/grpc/grpc-dotnet), [Helix Toolkit](https://github.com/helix-toolkit/helix-toolkit), [Entity Framework Core](https://github.com/dotnet/efcore), [ScottPlot](https://github.com/ScottPlot/ScottPlot), [xUnit.net](https://github.com/xunit/xunit).
