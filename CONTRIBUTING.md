# Contribuer à LayupPulse

LayupPulse est un démonstrateur indépendant pour une cellule fictive. Toute contribution doit préserver cette portée et ne doit jamais présenter le projet comme apte au contrôle d’une machine réelle ou à l’implémentation d’une fonction de sûreté.

## Préparation locale

- utiliser Windows 10 ou Windows 11 ;
- installer le SDK .NET 10 résolu par `global.json` ;
- conserver les dépendances Domain et Application indépendantes de WPF, gRPC, EF Core et SQLite ;
- ajouter une ADR dans `docs/decisions/` pour tout choix d’architecture significatif.

## Validation obligatoire

Exécuter depuis la racine du dépôt :

```powershell
dotnet restore LayupPulse.sln
dotnet format LayupPulse.sln --verify-no-changes --no-restore
dotnet build LayupPulse.sln -c Release --no-restore
dotnet test LayupPulse.sln -c Release --no-build
./scripts/run-demo.ps1 -SmokeTest
./scripts/package-demo.ps1
git diff --check
```

Si une commande ne peut pas être exécutée, l’indiquer précisément dans la pull request. Ne jamais versionner `bin`, `obj`, `artifacts`, bases SQLite, logs, sorties de publication ou paramètres propres à une machine.

## Pull requests

Garder un périmètre petit et cohérent, décrire les décisions et les risques, et ajouter des tests pour toute règle métier ou défaillance observable. Les modifications de fonctionnalités doivent rester distinctes des changements de documentation, de CI ou de packaging lorsqu’un découpage atomique est possible.
