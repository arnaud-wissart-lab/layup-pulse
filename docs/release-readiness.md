# Publication 0.3.0

## Statut

- Date de publication : 23 juillet 2026, fuseau Europe/Paris.
- Dépôt public : `arnaud-wissart-lab/layup-pulse`.
- Branche source : `main`.
- Version publiée : `0.3.0`, définie dans `Directory.Build.props`.
- Version publique précédente : `v0.2.2`.
- Source et actif Windows :
  [release GitHub v0.3.0](https://github.com/arnaud-wissart-lab/layup-pulse/releases/tag/v0.3.0).

Les preuves historiques de la version précédente restent disponibles dans
[l’état de préparation 0.2.2](release-readiness-0.2.2.md).

## Justification de version

La version `0.3.0` constitue un incrément fonctionnel par rapport à `0.2.2`.
Elle ajoute deux dépendances d’exécution CODE Framework 6.0.0, un modèle de
rapport, une projection statistique bornée, un document imprimable et une
sérialisation XPS. Il ne s’agit donc pas d’une correction compatible de type
`0.2.3`.

La capacité reste préparatoire : elle n’est pas encore raccordée à
`HistoryView` et ne modifie ni le shell, ni le thème, ni la navigation, ni
l’architecture MVVM existante.

## Périmètre publié

- adoption directe de `CODE.Framework.Wpf.Documents` 6.0.0 dans Desktop ;
- épinglage transitif de `CODE.Framework.Wpf` 6.0.0 ;
- modèle de rapport immuable sans type WPF ;
- projection pure de `ProductionRunHistoryDetails` avec 100 alarmes détaillées
  au maximum ;
- synthèse des buckets sans copie des 3 600 agrégats dans le rapport ;
- `FlowDocumentEx` avec avertissement, en-tête, pied de page, numérotation et
  filigrane ;
- impression WPF et export XPS uniquement, sans promesse de PDF natif ;
- ADR, notices tierces, changelog et scénario de validation technique.

## Validation

| Commande ou contrôle | Résultat |
| --- | --- |
| `dotnet restore LayupPulse.sln` | Réussi ; seul l’avertissement `NU1701` connu subsiste |
| `dotnet format LayupPulse.sln --verify-no-changes --no-restore` | Réussi ; aucun changement requis |
| `dotnet build LayupPulse.sln -c Release --no-restore` | Réussi ; aucune erreur |
| `dotnet test LayupPulse.sln -c Release --no-build` | Réussi ; 131 tests |
| Tests ciblés `ProductionRunReport` | Réussis ; 6 tests |
| `scripts/package-demo.ps1` | Réussi ; publication autonome, smoke test et archive ZIP |
| `git diff --check` | Réussi ; aucun défaut d’espace blanc |

La CI Windows exécute les mêmes contrôles de restauration, formatage, build,
tests et packaging. Le test WPF dédié crée un XPS temporaire, vérifie qu’il est
non vide puis le supprime. Aucun package de test supplémentaire n’est utilisé
pour le contexte STA.

L’archive `LayupPulse-win-x64.zip` est reconstruite depuis le commit du tag
`v0.3.0`. Sa taille et son empreinte SHA-256 sont consignées dans les notes de
la release GitHub afin de décrire exactement l’actif téléchargeable.

## Risques et limites résiduels

- aucune commande d’impression ou d’export n’est encore visible dans
  `HistoryView` ;
- l’export XPS est couvert par un test de sérialisation, mais aucune inspection
  visuelle opérateur n’est encore possible dans l’application ;
- aucun export PDF natif n’est fourni ou promis ;
- l’avertissement `NU1701` connu reste lié à
  `ScottPlot.WPF → SkiaSharp.Views.WPF`, indépendamment de CODE Framework ;
- le package Windows demeure non signé ;
- LayupPulse reste un démonstrateur logiciel utilisant des données simulées,
  impropre au pilotage d’une machine réelle et à toute fonction de sûreté.
