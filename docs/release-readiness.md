# État de préparation à la publication 0.3.0

## Statut

- Date de la passe locale : 23 juillet 2026, fuseau Europe/Paris.
- Dépôt public : `arnaud-wissart-lab/layup-pulse`.
- Branche candidate :
  `feature/production-run-report-code-framework`.
- Version candidate : `0.3.0`, définie dans `Directory.Build.props`.
- Dernière version publique de référence : `v0.2.2`.
- SHA candidat : à figer après le commit documentaire et la validation de la CI.
- Tag, release GitHub et actif public `v0.3.0` : non créés.

Les preuves historiques de la version précédente restent inchangées dans
[l’état de préparation 0.2.2](release-readiness-0.2.2.md).

## Justification de version

La version `0.3.0` constitue un incrément fonctionnel par rapport à `0.2.2`.
Elle livre le premier rapport du cycle sélectionné dans **Historique**, avec un
aperçu WPF paginé, l’impression Windows et l’export XPS. Elle ajoute la
référence directe `CODE.Framework.Wpf.Documents` 6.0.0 et sa dépendance
transitive `CODE.Framework.Wpf` 6.0.0. Il ne s’agit donc pas d’une correction
compatible de type `0.2.3`.

## Périmètre candidat

- modèle de rapport immuable sans type WPF ;
- projection bornée de `ProductionRunHistoryDetails`, avec 100 alarmes
  détaillées au maximum et une synthèse des agrégats ;
- `FlowDocumentEx` avec avertissement, en-tête, pied de page, numérotation,
  filigrane et pinceaux partagés gelés ;
- presenter Desktop testable, sans type WPF dans `HistoryViewModel` ;
- protection contre les réponses asynchrones périmées ou discordantes ;
- fenêtre d’aperçu propriétaire, redimensionnable et accessible au clavier ;
- impression par le dialogue Windows et export XPS par `PrintHelper.SaveAsXps` ;
- aucun export PDF natif ; un PDF dépend d’une imprimante Windows telle que
  Microsoft Print to PDF ;
- aucune comparaison multi-cycles, aucune extension du shell et aucun usage de
  CODE Framework hors de `LayupPulse.Desktop/Reporting` dans le code de
  production.

## Validation locale

Toutes les commandes ci-dessous ont réellement retourné un code de sortie nul
sur le contenu de travail audité.

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

## Dépendances CODE Framework et notices

Le graphe restauré contient une seule référence directe
`CODE.Framework.Wpf.Documents` 6.0.0 dans `LayupPulse.Desktop`.
`CODE.Framework.Wpf` 6.0.0 est uniquement transitif. Un test d’architecture
vérifie les références de packages et recherche désormais tout usage du
namespace `CODE.Framework` en dehors de `LayupPulse.Desktop/Reporting`.

Les fichiers `.nuspec` restaurés pour les deux packages déclarent la licence
MIT et ciblent `net10.0-windows7.0`. `THIRD-PARTY-NOTICES.md` reflète ces
versions, licences et usages.

## Package local de contrôle

- Archive : `artifacts/LayupPulse-win-x64.zip`.
- Taille : 128 611 994 octets, soit 122,654 Mio.
- SHA-256 :
  `BA93CB932E98313961DC38104E1099F624596F1489A2EA66EEBE2597AA96585B`.
- Versions de fichier Desktop et Simulator : `0.3.0.0`.
- Versions produit Desktop et Simulator : `0.3.0+ca1a371fe617`.
- Smoke test : Desktop et Simulator autonomes actifs pendant 5 secondes, puis
  arrêtés proprement.

Le package a été créé depuis le contenu de travail après l’audit, avant le
commit documentaire final. Son information de version identifie donc le commit
parent `ca1a371fe617`. Il constitue une preuve locale de packaging et de
démarrage, pas un actif publiable. La CI devra reconstruire l’archive depuis le
SHA poussé.

## Inspection WPF réellement effectuée

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

## Validations manuelles restantes

- ouvrir l’aperçu final et parcourir ses deux pages ;
- annuler à nouveau le dialogue d’impression sur le contenu final ;
- enregistrer un fichier XPS depuis l’interface puis l’ouvrir avec un lecteur
  XPS installé ;
- vérifier le rendu à 1280 × 720 et sur des sessions Windows réellement
  configurées à 150 % et 200 % ;
- vérifier une impression physique ou Microsoft Print to PDF uniquement si
  cette sortie est nécessaire à la démonstration.

## Conditions avant publication

1. obtenir une CI verte sur le SHA poussé de la draft PR ;
2. compléter les validations manuelles restantes sur un bureau Windows
   disponible ;
3. fusionner la branche validée dans `main` sans modifier le périmètre ;
4. reconstruire le package Windows autonome depuis le SHA destiné au tag ;
5. figer la taille et le SHA-256 de l’archive publiable ;
6. déplacer les notes de `Non publié` vers la section `0.3.0` du changelog ;
7. créer ensuite seulement le tag annoté et la release GitHub `v0.3.0`.

## Risques et limites résiduels

- l’export natif reste XPS ; aucun export PDF natif n’est fourni ou promis ;
- le package Windows demeure non signé ;
- l’avertissement `NU1701` connu reste lié à
  `ScottPlot.WPF → SkiaSharp.Views.WPF`, indépendamment de CODE Framework ;
- les rapports comparent un seul cycle ; les comparaisons multi-cycles restent
  à réaliser ;
- LayupPulse utilise uniquement des données simulées et reste impropre au
  pilotage d’une machine réelle ou à toute fonction de sûreté.
