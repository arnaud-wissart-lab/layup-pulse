# État de préparation à la publication 0.3.0

## Statut

- Date de la passe locale : 23 juillet 2026, fuseau Europe/Paris.
- Dépôt public : `arnaud-wissart-lab/layup-pulse`.
- Branche candidate :
  `feature/production-run-report-code-framework`.
- Version candidate : `0.3.0`, définie dans `Directory.Build.props`.
- Dernière version publique de référence : `v0.2.2`.
- SHA candidat : à figer après fusion dans `main` et validation de la CI.
- Tag, release GitHub et actif public `v0.3.0` : non créés.

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

## Périmètre candidat

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

## Validation locale

| Commande ou contrôle | État |
| --- | --- |
| `dotnet restore LayupPulse.sln` | Réussi ; seul l’avertissement `NU1701` connu subsiste |
| `dotnet format LayupPulse.sln --verify-no-changes --no-restore` | Réussi ; aucun changement requis |
| `dotnet build LayupPulse.sln -c Release --no-restore` | Réussi ; 0 erreur et 3 occurrences de `NU1701`, dont le projet WPF temporaire |
| `dotnet test LayupPulse.sln -c Release --no-build` | Réussi ; 131 tests, 0 échec, 0 test ignoré |
| Tests ciblés `ProductionRunReport` | Réussis ; 6 tests, 0 échec, 0 test ignoré |
| `scripts/package-demo.ps1` | Réussi ; publication autonome, smoke test de 5 secondes et archive ZIP |
| `git diff --check` | Réussi ; aucun défaut d’espace blanc |

Le test WPF dédié crée un XPS temporaire, vérifie qu’il est non vide puis le
supprime. Aucun package de test supplémentaire n’est utilisé pour le contexte
STA.

### Package local de contrôle

- Archive : `artifacts/LayupPulse-win-x64.zip`.
- Taille : 128 609 059 octets, soit 122,651 Mio.
- SHA-256 :
  `29c8581043bf824c414d173416d963555951e764f6980df257aaa92d085d0a62`.
- Versions de fichier Desktop et Simulator : `0.3.0.0`.
- Versions produit Desktop et Simulator : `0.3.0+a7c4606972bf`.
- Smoke test : Desktop et Simulator autonomes actifs pendant 5 secondes, puis
  arrêtés proprement.

Ce package prouve la compatibilité locale du contenu de travail, mais il a été
créé avant le commit de préparation `0.3.0`. Son SHA-256 n’est donc pas destiné
à la publication. L’archive devra être reconstruite depuis le SHA final validé
par la CI.

## Conditions avant publication

1. fusionner la branche validée dans `main` sans modifier le périmètre ;
2. obtenir une CI verte sur le SHA destiné au tag ;
3. reconstruire le package Windows autonome depuis ce SHA ;
4. vérifier les versions Desktop et Simulator ainsi que le smoke test packagé ;
5. figer la taille et le SHA-256 de l’archive ;
6. déplacer les notes de `Non publié` vers la section `0.3.0` du changelog ;
7. créer ensuite seulement le tag annoté et la release GitHub `v0.3.0`.

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
