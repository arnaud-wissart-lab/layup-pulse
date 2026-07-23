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

La capacité publiée reste préparatoire : elle n’est pas encore raccordée à
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

## Validation de la publication

| Commande ou contrôle | Résultat |
| --- | --- |
| `dotnet restore LayupPulse.sln` | Réussi ; seul l’avertissement `NU1701` connu subsiste |
| `dotnet format LayupPulse.sln --verify-no-changes --no-restore` | Réussi ; aucun changement requis |
| `dotnet build LayupPulse.sln -c Release --no-restore` | Réussi ; aucune erreur |
| `dotnet test LayupPulse.sln -c Release --no-build` | Réussi ; 131 tests |
| Tests ciblés `ProductionRunReport` | Réussis ; 6 tests |
| `scripts/package-demo.ps1` | Réussi ; publication autonome, smoke test et archive ZIP |
| `git diff --check` | Réussi ; aucun défaut d’espace blanc |

La CI Windows a exécuté les mêmes contrôles de restauration, formatage, build,
tests et packaging. Le test WPF dédié a créé un XPS temporaire, vérifié qu’il
était non vide puis l’a supprimé. Aucun package de test supplémentaire n’a été
utilisé pour le contexte STA.

L’archive `LayupPulse-win-x64.zip` a été reconstruite depuis le commit du tag
`v0.3.0`. Sa taille et son empreinte SHA-256 sont consignées dans les notes de
la release GitHub afin de décrire exactement l’actif téléchargeable.

## Risques et limites de la publication

- aucune commande d’impression ou d’export n’est visible dans `HistoryView` ;
- l’export XPS est couvert par un test de sérialisation, mais aucune inspection
  visuelle opérateur n’est possible dans la version publiée ;
- aucun export PDF natif n’est fourni ou promis ;
- l’avertissement `NU1701` connu reste lié à
  `ScottPlot.WPF → SkiaSharp.Views.WPF`, indépendamment de CODE Framework ;
- le package Windows demeure non signé ;
- LayupPulse reste un démonstrateur logiciel utilisant des données simulées,
  impropre au pilotage d’une machine réelle et à toute fonction de sûreté.

## Validation de la fonctionnalité de rapport non publiée

Les preuves ci-dessous concernent la branche
`feature/production-run-report-code-framework` après le tag `v0.3.0`. Elles ne
modifient pas les preuves historiques de la publication ci-dessus et ne
décrivent aucun nouvel actif public.

### Périmètre audité

- raccordement du rapport au cycle sélectionné dans **Historique** ;
- presenter Desktop testable, sans type WPF dans `HistoryViewModel` ;
- protection contre les réponses asynchrones périmées ou discordantes ;
- fenêtre d’aperçu propriétaire, redimensionnable et accessible au clavier ;
- impression par le dialogue Windows et export XPS par
  `PrintHelper.SaveAsXps` ;
- pinceaux WPF partagés gelés afin d’éviter leur affinité de thread ;
- aucun export PDF natif ; un PDF dépend d’une imprimante Windows telle que
  Microsoft Print to PDF ;
- aucune comparaison multi-cycles, aucune extension du shell et aucun usage de
  CODE Framework hors de `LayupPulse.Desktop/Reporting` dans le code de
  production.

### Validation locale

Toutes les commandes ci-dessous ont réellement retourné un code de sortie nul
sur le contenu de travail audité après la synchronisation avec `main` et avant
le commit de merge.

| Commande ou contrôle | Résultat |
| --- | --- |
| `dotnet restore LayupPulse.sln` | Réussi ; seul l’avertissement `NU1701` connu subsiste |
| `dotnet format LayupPulse.sln --verify-no-changes --no-restore` | Réussi ; aucun changement requis |
| `dotnet build LayupPulse.sln -c Release --no-restore` | Réussi ; 0 erreur et 3 occurrences de `NU1701`, dont le projet WPF temporaire |
| `dotnet test LayupPulse.sln -c Release --no-build` | Réussi ; 138 tests, 0 échec, 0 test ignoré |
| Tests ciblés rapport, concurrence, dépendances et XAML | Réussis ; 21 tests, 0 échec, 0 test ignoré |
| `scripts/run-demo.ps1 -SmokeTest -SmokeTestDurationSeconds 5` | Réussi ; Desktop et Simulator actifs pendant 5 secondes puis arrêtés |
| `scripts/package-demo.ps1` | Réussi ; publication autonome, smoke test packagé de 5 secondes et archive ZIP |
| `dotnet list LayupPulse.sln package --vulnerable --include-transitive` | Réussi ; aucun package vulnérable signalé par les sources actuelles |
| `git diff --check` | Réussi ; avertissement informatif de normalisation CRLF/LF pour `docs/ui-specification.md` |

Le test WPF dédié sérialise réellement un XPS temporaire non vide puis le
supprime. Les tests de concurrence maintiennent la commande désactivée sans
détails complets, rejettent une réponse périmée ou discordante et vérifient que
le presenter reçoit exactement l’instance attendue.

Le workflow GitHub Actions du commit `9dd22fe8f53f` a également réussi la
restauration, le formatage, le build, les 138 tests, le packaging Windows x64,
le smoke test du package et le téléversement de l’archive.

### Dépendances CODE Framework et notices

Le graphe restauré contient une seule référence directe
`CODE.Framework.Wpf.Documents` 6.0.0 dans `LayupPulse.Desktop`.
`CODE.Framework.Wpf` 6.0.0 est uniquement transitif. Un test d’architecture
vérifie les références de packages et recherche tout usage du namespace
`CODE.Framework` en dehors de `LayupPulse.Desktop/Reporting`.

Les fichiers `.nuspec` restaurés pour les deux packages déclarent la licence
MIT et ciblent `net10.0-windows7.0`. `THIRD-PARTY-NOTICES.md` reflète ces
versions, licences et usages.

### Package local de contrôle

- Archive : `artifacts/LayupPulse-win-x64.zip`.
- Taille : 128 612 016 octets, soit 122,654 Mio.
- SHA-256 :
  `218C271DF74FB0103C7098D8C6AD33941E8F6A939D9BB170C984E7B312FC31E0`.
- Versions de fichier Desktop et Simulator : `0.3.0.0`.
- Versions produit Desktop et Simulator : `0.3.0+9dd22fe8f53f`.
- Smoke test : Desktop et Simulator autonomes actifs pendant 5 secondes, puis
  arrêtés proprement.

Le package a été créé depuis le contenu de travail synchronisé, avant le commit
de merge. Son information de version identifie donc le commit parent
`9dd22fe8f53f`. Il constitue une preuve locale de packaging et de démarrage,
pas un actif publiable.

### Inspection WPF réellement effectuée

La session de contrôle expose 120 DPI, soit 125 %, et une surface logique
2048 × 1152. Dans la fenêtre maximisée, les contrôles suivants ont été
observés :

- **Historique** filtré sur **En cours**, sans résultat ni sélection ;
- message vide explicite et commande **Rapport du cycle** désactivée ;
- retour au filtre complet, sélection et chargement d’un cycle ;
- détails d’alarme visibles et commande de rapport réactivée ;
- noms d’automatisation indiquant explicitement les cycles et données simulés.

Une passe antérieure sur le commit d’aperçu `ca1a371` avait réellement vérifié
la fenêtre propriétaire, les deux pages du document, le focus clavier,
l’annulation du dialogue d’impression et l’annulation du dialogue
d’enregistrement XPS. Après le durcissement final, une activité utilisateur a
interrompu la reprise de l’aperçu. Aucune capture n’a été créée et les
contrôles interrompus ne sont pas déclarés réussis sur le contenu final.

### Validations manuelles restantes

- ouvrir l’aperçu final et parcourir ses deux pages ;
- annuler à nouveau le dialogue d’impression sur le contenu final ;
- enregistrer un fichier XPS depuis l’interface puis l’ouvrir avec un lecteur
  XPS installé ;
- vérifier le rendu à 1280 × 720 et sur des sessions Windows réellement
  configurées à 150 % et 200 % ;
- vérifier une impression physique ou Microsoft Print to PDF uniquement si
  cette sortie est nécessaire à la démonstration.

### Risques et limites

- l’export natif reste XPS ; aucun export PDF natif n’est fourni ou promis ;
- les rapports décrivent un seul cycle ; les comparaisons multi-cycles restent
  à réaliser ;
- la branche conserve la version d’assembly `0.3.0`, identique à la version
  publique actuelle, jusqu’à la décision sur la prochaine publication ;
- le package Windows demeure non signé ;
- l’avertissement `NU1701` connu reste lié à
  `ScottPlot.WPF → SkiaSharp.Views.WPF`, indépendamment de CODE Framework ;
- LayupPulse utilise uniquement des données simulées et reste impropre au
  pilotage d’une machine réelle ou à toute fonction de sûreté.
