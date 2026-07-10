# ADR 0005 — Utiliser .NET 10 comme socle versionné

## Statut

Acceptée le 11 juillet 2026.

## Contexte

LayupPulse doit fournir un build reproductible du client WPF, du serveur gRPC, des tests et des packages Windows autonomes. Une version implicite du SDK rendrait les builds locaux et hébergés sensibles à l’état de chaque machine. Le démonstrateur reste exclusivement Windows pour son interface WPF, tandis que le domaine, l’application, les contrats et le simulateur conservent des cibles .NET sans dépendance WPF.

## Décision

La solution cible .NET 10 et C# 14. `global.json` épingle le SDK `10.0.301` avec `rollForward: latestFeature`, sans préversion. `Directory.Build.props` active la compilation déterministe, la nullabilité et les analyseurs pour tous les projets.

La CI installe le SDK à partir de `global.json`. Les scripts de démonstration et de packaging vérifient explicitement qu’un SDK .NET 10 est résolu avant de compiler. Les packages de démonstration ciblent `win-x64` en mode autonome et ne supposent donc pas la présence d’un runtime .NET sur la machine de présentation.

## Conséquences

- Les développeurs doivent installer une version .NET 10 compatible avec la politique de roll-forward.
- Les sorties WPF et les packages de démonstration restent spécifiques à Windows x64.
- Une mise à niveau du SDK épinglé doit être testée avec le formatage, le build, les tests, la publication et le smoke test.
- Le mode autonome augmente la taille du ZIP mais réduit les prérequis sur la machine de démonstration.
- Le choix de .NET 10 ne change pas le statut du projet : aucune aptitude au contrôle d’un matériel réel ou à une fonction de sûreté n’en découle.
