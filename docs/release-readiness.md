# Publication 0.4.0

## Statut

- Date de publication : 23 juillet 2026, fuseau Europe/Paris.
- Dépôt public : `arnaud-wissart-lab/layup-pulse`.
- Branche source : `main`.
- Version publiée : `0.4.0`, définie dans `Directory.Build.props`.
- Version publique précédente : `v0.3.0`.
- SHA de merge validé : `58a25aac263e3a9cc5e5dc5d65764f3cf0f12a4b`.
- Source et actif Windows :
  [release GitHub v0.4.0](https://github.com/arnaud-wissart-lab/layup-pulse/releases/tag/v0.4.0).

Les preuves historiques restent disponibles dans les documents des
[versions 0.3.0](release-readiness-0.3.0.md) et
[0.2.2](release-readiness-0.2.2.md). Elles ne sont pas remplacées par cette
publication.

## Justification de version

La version `0.4.0` constitue un incrément fonctionnel par rapport à `0.3.0`.
Elle raccorde le socle de rapport à **Historique** et livre l’aperçu WPF
paginé, l’impression Windows ainsi que l’export XPS. Il ne s’agit donc pas
d’une correction de type `0.3.1`.

## Périmètre publié

- rapport du cycle simulé sélectionné depuis **Historique** ;
- presenter Desktop testable, sans type WPF dans `HistoryViewModel` ;
- protection contre les réponses asynchrones périmées ou discordantes ;
- fenêtre d’aperçu propriétaire, redimensionnable et accessible au clavier ;
- tables `FlowDocument`, sans `DataGrid` dans le rapport ;
- impression par le dialogue Windows et export XPS par
  `PrintHelper.SaveAsXps` ;
- pinceaux WPF partagés gelés afin d’éviter leur affinité de thread ;
- aucun export PDF natif ; un PDF dépend d’une imprimante Windows telle que
  Microsoft Print to PDF ;
- aucune comparaison multi-cycles, aucune extension du shell et aucun usage de
  CODE Framework hors de `LayupPulse.Desktop/Reporting` dans le code de
  production.

## Validation avant publication

Les contrôles ci-dessous ont été exécutés sur le contenu de travail après le
merge dans `main` et ont réellement retourné un code de sortie nul.

| Commande ou contrôle | Résultat |
| --- | --- |
| `dotnet restore LayupPulse.sln` | Réussi ; seul l’avertissement `NU1701` connu subsiste |
| `dotnet format LayupPulse.sln --verify-no-changes --no-restore` | Réussi ; aucun changement requis |
| `dotnet build LayupPulse.sln -c Release --no-restore` | Réussi ; 0 erreur et 3 occurrences de `NU1701`, dont le projet WPF temporaire |
| `dotnet test LayupPulse.sln -c Release --no-build` | Réussi ; 138 tests, 0 échec, 0 test ignoré |
| `scripts/run-demo.ps1 -SmokeTest -SmokeTestDurationSeconds 5` | Réussi ; Desktop et Simulator actifs pendant 5 secondes puis arrêtés |
| `scripts/package-demo.ps1` | Réussi ; publication autonome, smoke test packagé de 5 secondes et archive ZIP |
| `dotnet list LayupPulse.sln package --vulnerable --include-transitive` | Réussi ; aucun package vulnérable signalé par les sources actuelles |
| `git diff --check` | Réussi ; aucun défaut d’espace blanc |

Les tests existants couvrent la sérialisation réelle d’un XPS temporaire non
vide, la désactivation de la commande sans détails complets, le rejet des
réponses périmées ou discordantes et la transmission au presenter de
l’instance de détails attendue.

## Package local de contrôle

- Archive : `artifacts/LayupPulse-win-x64.zip`.
- Taille : 128 611 963 octets, soit 122,654 Mio.
- SHA-256 :
  `AA661F8F321382CC61A21DFDBD2ED8F4A93685FEA42A8E2EBCE1D0A3951D3AD5`.
- Versions de fichier Desktop et Simulator : `0.4.0.0`.
- Versions produit Desktop et Simulator : `0.4.0+58a25aac263e`.
- Smoke test : Desktop et Simulator autonomes actifs pendant 5 secondes, puis
  arrêtés proprement.

Le package a été créé depuis le SHA de merge validé, avant le commit de
finalisation de la publication. Il constitue une preuve locale de packaging et
de démarrage. L’actif public doit être reconstruit depuis le SHA tagué et son
empreinte doit être publiée dans les notes de release.

## Validation GitHub Actions

- [workflow de push de la branche 0.4.0](https://github.com/arnaud-wissart-lab/layup-pulse/actions/runs/30045056075) :
  réussi ;
- [workflow de la pull request 0.4.0](https://github.com/arnaud-wissart-lab/layup-pulse/actions/runs/30045058868) :
  réussi ;
- [workflow du merge dans main](https://github.com/arnaud-wissart-lab/layup-pulse/actions/runs/30045305516) :
  réussi, y compris le packaging, le smoke test Windows x64 et le
  téléversement de l’archive.

## Dépendances CODE Framework et notices

Le graphe restauré contient une seule référence directe
`CODE.Framework.Wpf.Documents` 6.0.0 dans `LayupPulse.Desktop`.
`CODE.Framework.Wpf` 6.0.0 reste uniquement transitif. Un test d’architecture
vérifie les références de packages et recherche tout usage du namespace
`CODE.Framework` en dehors de `LayupPulse.Desktop/Reporting`.

Les fichiers `.nuspec` restaurés pour les deux packages déclarent la licence
MIT et ciblent `net10.0-windows7.0`. `THIRD-PARTY-NOTICES.md` reflète ces
versions, licences et usages.

## Inspection WPF réellement effectuée

La session de contrôle exposait 120 DPI, soit 125 %, et une surface logique
2048 × 1152. Dans la fenêtre maximisée, les contrôles suivants ont été
observés :

- **Historique** filtré sur **En cours**, sans résultat ni sélection ;
- message vide explicite et commande **Rapport du cycle** désactivée ;
- retour au filtre complet, sélection et chargement d’un cycle ;
- détails d’alarme visibles et commande de rapport réactivée ;
- noms d’automatisation indiquant explicitement les cycles et données simulés.

Une passe antérieure sur le commit d’aperçu `ca1a371` a réellement vérifié la
fenêtre propriétaire, les deux pages du document, le focus clavier,
l’annulation du dialogue d’impression et l’annulation du dialogue
d’enregistrement XPS. Aucune capture n’a été créée.

## Validations manuelles restantes

- ouvrir l’aperçu final et parcourir ses deux pages ;
- annuler à nouveau le dialogue d’impression sur le contenu final ;
- enregistrer un fichier XPS depuis l’interface puis l’ouvrir avec un lecteur
  XPS installé ;
- vérifier le rendu à 1280 × 720 et sur des sessions Windows réellement
  configurées à 150 % et 200 % ;
- vérifier une impression physique ou Microsoft Print to PDF uniquement si
  cette sortie est nécessaire à la démonstration.

## Risques et limites

- l’export natif reste XPS ; aucun export PDF natif n’est fourni ou promis ;
- les rapports décrivent un seul cycle ; les comparaisons multi-cycles restent
  à réaliser ;
- le package Windows demeure non signé ;
- l’avertissement `NU1701` connu reste lié à
  `ScottPlot.WPF → SkiaSharp.Views.WPF`, indépendamment de CODE Framework ;
- LayupPulse utilise uniquement des données simulées et reste impropre au
  pilotage d’une machine réelle ou à toute fonction de sûreté.
