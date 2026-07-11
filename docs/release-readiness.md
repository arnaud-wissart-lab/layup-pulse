# État de préparation à la publication

## Périmètre audité

- Date de l’audit : 11 juillet 2026, fuseau Europe/Paris.
- Dépôt public : `arnaud-wissart-lab/layup-pulse`.
- Branche : `main`.
- `HEAD` de départ de `main` audité : `772add1956f9aaa05c510220d8bd12e4da4caa64` (`fix(ci): stabilise l’arrêt de session et le test de coupure gRPC`).
- Révision candidate finale : le commit qui porte ce document ; son SHA doit être celui du futur tag `v0.2.1` après réussite de sa CI.
- Version : `0.2.1`, définie une seule fois dans `Directory.Build.props`.
- Les tags `v0.1.0` et `v0.2.0` ne sont ni déplacés ni remplacés. Le nouveau tag `v0.2.1` ne sera créé qu’après réussite de la CI du commit final.

Le stockage durable reste limité au démonstrateur local : `%LOCALAPPDATA%\LayupPulse\layuppulse.db`, contextes EF Core courts créés par `IDbContextFactory`, migrations SQLite et file d’écriture bornée. Les échantillons bruts à 20 Hz ne sont pas persistés. Une erreur de base est journalisée et exposée comme diagnostic non fatal ; elle n’interrompt ni la télémétrie ni WPF.

## Cohérence de version

`Directory.Build.props` est la source de vérité de la version `0.2.1`. MSBuild l’applique aux builds Release Desktop et Simulator. `DiagnosticsViewModel` lit la version de l’assembly Desktop. `scripts/package-demo.ps1` lit la même propriété, refuse une valeur explicite divergente et l’utilise pour les métadonnées des deux publications. La CI appelle le script sans recopier de numéro de version.

Un test de régression vérifie la concordance entre la source MSBuild, les assemblies Desktop et Simulator, Diagnostics, le script de packaging, la CI, le changelog et le présent document.

## Validation locale de la passe de durcissement

Cette section décrit exclusivement les commandes exécutées dans l’arbre de travail local issu du `HEAD` ci-dessus. Elle ne constitue ni une validation GitHub Actions de modifications non commitées, ni une validation d’un actif de release déjà publié.

| Commande ou contrôle | Résultat |
| --- | --- |
| Tests ciblés d’orchestration et de concurrence | Réussi dix fois de suite : 9 tests par passage, 0 échec. |
| `dotnet restore LayupPulse.sln` | Réussi ; avertissement `NU1701` SkiaSharp connu. |
| `dotnet format LayupPulse.sln` | Réussi ; avertissement générique de chargement de l’espace de travail. |
| `dotnet format LayupPulse.sln --verify-no-changes --no-restore` | Réussi ; aucune modification de format restante. |
| `dotnet build LayupPulse.sln -c Release --no-restore` | Réussi : 0 erreur ; avertissement `NU1701` SkiaSharp connu. |
| `dotnet test LayupPulse.sln -c Release --no-build` | Réussi : 116 tests, 0 échec, 0 ignoré. |
| `.\scripts\run-demo.ps1 -SmokeTest -SmokeTestDurationSeconds 5 -Build` | Réussi : build, démarrage des deux processus, stabilité pendant cinq secondes et nettoyage. |
| `.\scripts\package-demo.ps1 -Version 0.2.1` | Réussi : publications autonomes, smoke test intégré et archive de 128 275 073 octets. |
| `.\artifacts\LayupPulse-win-x64\Run-LayupPulse.ps1 -SmokeTest -SmokeTestDurationSeconds 5` | Réussi : second smoke test direct du package reconstruit. |
| Contrôle des versions des assemblies packagées | Desktop et Simulator : assembly `0.2.1.0`, fichier `0.2.1.0`, produit candidat `0.2.1+772add1956f9`. Diagnostics affiche `0.2.1`. |
| SHA-256 de l’archive candidate locale | `f7d8904ce68197d413a0e2277c0ad12be903735d85ed903f3d0355c3aae89670`. L’archive de release sera reconstruite depuis le commit final vert. |
| `git diff --check` | Réussi : aucun défaut d’espace ni marqueur de conflit. |

## Validation GitHub Actions actuelle

La dernière exécution distante terminée pour le `HEAD` actuel est [GitHub Actions 29140837545](https://github.com/arnaud-wissart-lab/layup-pulse/actions/runs/29140837545). Elle est réussie pour le commit `772add1956f9aaa05c510220d8bd12e4da4caa64` et couvre restauration, formatage, build Release, tests, packaging, smoke test et téléversement de l’artefact.

Cette réussite ne couvre pas les modifications locales non commitées de la présente passe. Aucune réussite GitHub Actions ne doit leur être attribuée tant qu’un éventuel commit final n’a pas sa propre exécution distante terminée avec succès.

## Validation des actifs de release publiés

### `v0.2.0`

- Le tag existant pointe sur `772add1956f9aaa05c510220d8bd12e4da4caa64`.
- L’actif publié `LayupPulse-win-x64.zip` mesure 128 274 576 octets.
- Son SHA-256 publié est `fa30018629557b128a995338ca55782b05ef7185fa8e292630c86a85e947d4dc`.
- Cet actif est antérieur à la passe de durcissement locale. Sa validation ne prouve donc pas le comportement des changements locaux et il ne doit pas être présenté comme leur package reconstruit.

### `v0.1.0`

- Le tag et l’actif restent inchangés.
- L’actif publié `LayupPulse-win-x64.zip` mesure 125 137 069 octets.
- Son SHA-256 publié et revérifié est `877f1b67ec6dd7b3e47ca4ffd9a8732e8b763c8b02902e71a115f703c3e39361`.
- Le `README.txt` de cette archive publiée inchangée a été inspecté pendant l’audit. Il ne décrit pas de limitation relative à un historique durable. Aucune affirmation inverse ne doit lui être attribuée.
- L’absence d’historique durable dans `v0.1.0` est établie par le contenu logiciel et l’historique du dépôt, pas par une mention inexistante dans le `README.txt` de l’archive.

## Scénarios de durcissement couverts

- Pour un défaut de procédé continuant à produire de la télémétrie, le run local attend le premier échantillon terminal avant sa finalisation. Cet échantillon contribue aux moyennes, au minimum de santé, à la progression et au dernier agrégat durable.
- `CommunicationDrop` est traité explicitement, car aucun échantillon terminal n’est garanti.
- Lorsqu’un nouveau contexte de simulateur est attaché en état `Ready` ou `Disconnected`, le run local précédent est finalisé en `Aborted`, son association au pipeline est retirée et la télémétrie `Ready` suivante n’est pas rattachée à l’ancien run.
- Un snapshot de remplacement encore `Running` ou `Paused` conserve le run local existant.
- Les filtres et sélections de l’Historique utilisent des générations de requêtes. Une réponse obsolète ne peut ni remplacer la liste récente, ni effacer la sélection récente, ni terminer prématurément l’indicateur de chargement.

## Architecture et risques résiduels

- Domain ne dépend d’aucune technologie d’infrastructure ; Application ne dépend que de Domain.
- Les ViewModels n’accèdent pas au `DbContext` et les requêtes d’historique restent asynchrones et bornées.
- Le build conserve l’avertissement `NU1701` connu lié à `SkiaSharp.Views.WPF 3.119.0`, dépendance transitive de la bibliothèque de graphiques.
- Le package Windows x64 n’est pas signé et peut déclencher SmartScreen.
- SQLite demeure un stockage local de démonstration, sans rétention, export, authentification, réplication ni garantie de traçabilité industrielle.
- LayupPulse ne doit jamais être présenté comme adapté au pilotage d’un équipement industriel réel ou comme une fonction de sûreté.

## Conclusion

La passe de durcissement `0.2.1` est validée localement. Le `HEAD` distant de départ possède une CI verte, mais cette preuve est volontairement séparée des modifications locales et des actifs de release déjà publiés. Le commit final doit obtenir sa propre CI verte, puis son package doit être reconstruit et smoke-testé avant la création du tag.
