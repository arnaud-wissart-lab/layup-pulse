# État de préparation à la publication

## Périmètre audité

- Date de la passe : 13 juillet 2026, fuseau Europe/Paris.
- Dépôt public : `arnaud-wissart-lab/layup-pulse`.
- Branche de départ : `main` au commit
  `e8dc8f2d49f7dba9ff71f3b78a474dac23ca6525`.
- SHA source final validé, packagé et destiné au tag :
  `30b8547c332a3757c2eaaa31b58dbbf7b00673e1`.
- Version : `0.2.2`, définie dans `Directory.Build.props`.
- Objet : durcissement de l’application packagée, de l’instance unique Desktop
  et de la qualité visuelle WPF, sans modification de la machine d’états ni du
  comportement de persistance.

## État des versions publiques

Le tag annoté `v0.2.1` pointe sur le commit de départ et la release publique
`LayupPulse 0.2.1` a été publiée le 11 juillet 2026. Son actif
`LayupPulse-win-x64.zip` mesure 128 275 071 octets et porte le SHA-256 publié
`a112774d90a029863a345d58f070a936e55141f5b499834f73fb309cf1152321`.
Ce tag et cet actif sont immuables et ne seront ni déplacés, ni remplacés.

La correction prépare donc `v0.2.2`. Aucun tag, aucune release et aucun actif
GitHub `v0.2.2` ne doivent être créés avant la revue des captures et du présent
rapport de validation.

## Stratégies retenues

- Desktop utilise un mutex nommé limité à la session Windows et des événements
  nommés avec acquittement pour restaurer et activer la fenêtre existante.
- Le lanceur packagé utilise un mutex distinct, refuse un Desktop déjà actif,
  identifie le détenteur du point d’écoute et vérifie que le socket prêt
  appartient au PID Simulator qu’il vient de créer.
- Un simulateur existant n’est pas réutilisé silencieusement. Le lanceur
  s’arrête avec un message clair et ne termine jamais un processus qu’il n’a pas
  créé.
- Les contrôles WPF sensibles disposent de templates sombres explicites pour
  leurs états usuels, sans dépendre des pinceaux système de sélection claire.
- Les définitions des défauts de simulation centralisent le libellé français,
  le nom technique anglais et l’aide contextuelle.

## Validation locale

Les résultats ci-dessous proviennent des trois commits de la version corrective,
de la CI sur le SHA source final et du package autonome reconstruit après cette
CI le 13 juillet 2026. Une commande n’est marquée réussie qu’après constat de
son code de sortie nul.

| Commande ou contrôle | Résultat |
| --- | --- |
| `dotnet restore LayupPulse.sln` | Réussi ; seul l’avertissement `NU1701` connu subsiste. |
| `dotnet format LayupPulse.sln --verify-no-changes --no-restore` | Réussi ; aucun changement de format requis. |
| `dotnet build LayupPulse.sln -c Release --no-restore` | Réussi ; 0 erreur, 3 occurrences de `NU1701`, dont le projet WPF temporaire. |
| `dotnet test LayupPulse.sln -c Release --no-build` | Réussi ; 124 tests, 0 échec, 0 test ignoré. |
| `scripts/run-demo.ps1 -SmokeTest -SmokeTestDurationSeconds 5` | Réussi ; Desktop et Simulator sont restés actifs pendant 5 secondes puis ont été arrêtés. |
| `scripts/package-demo.ps1` avant la CI | Réussi ; package candidat et smoke test packagé créés depuis les trois commits. |
| `scripts/package-demo.ps1` après la CI verte | Réussi ; dossier autonome et ZIP recréés, sans réutilisation de l’archive candidate. |
| Tests packagés de double lancement et de conflit de port | Réussis ; détail ci-dessous. |
| Inspection visuelle et six captures corrigées | Réussie à 125 % réel ; limites DPI détaillées ci-dessous. |
| `git diff --check` | Réussi ; aucun défaut d’espace blanc, seulement les avertissements de normalisation de fins de ligne Git. |

## Validation GitHub Actions

La CI [GitHub Actions 29241477556](https://github.com/arnaud-wissart-lab/layup-pulse/actions/runs/29241477556)
est entièrement réussie sur
`30b8547c332a3757c2eaaa31b58dbbf7b00673e1`. Le job Windows unique a terminé
en 2 min 43 s et couvre la restauration, la vérification du formatage, le build
Release, les 124 tests, la publication autonome, le smoke test packagé et le
téléversement de l’artefact.

### Package contrôlé

- Archive : `artifacts/LayupPulse-win-x64.zip`.
- Taille : 128 285 896 octets, soit 122,343 Mio.
- SHA-256 :
  `acce8051172bbc0f7d5524b134b070916eb2327e3713f74cf0b6a6c01b045273`.
- Versions de fichier Desktop et Simulator : `0.2.2.0`.
- Versions produit Desktop et Simulator : `0.2.2+30b8547c332a`.
- Le script PowerShell copié et son entrée dans le ZIP commencent tous deux
  par la marque UTF-8 BOM attendue par Windows PowerShell 5.1.
- Le lanceur CMD copié contient `chcp 65001` et les messages français du
  lanceur ont conservé leurs accents pendant les scénarios packagés.
- Le README extrait de l’archive explique l’avertissement « Éditeur inconnu »,
  limite l’exécution au dépôt officiel et documente le déblocage explicite.

### Scénarios d’instance et de conflit

1. Le smoke test du lanceur packagé crée exactement un Desktop et un Simulator
   et les maintient actifs pendant cinq secondes.
2. Une seconde exécution du lanceur se termine avec le code 0 et
   le message « Un lancement de LayupPulse est déjà en cours dans cette
   session Windows. » ; les nombres de processus restent à un et un.
3. Une seconde exécution directe de `LayupPulse.Desktop.exe` se termine avec le
   code 0. Le nombre de Desktop reste à un et la fenêtre principale reste
   maximisée après son activation.
4. La fermeture de la fenêtre Desktop lancée par le package termine le lanceur
   avec le code 0 et arrête uniquement le Simulator qu’il possède ; aucun
   Simulator packagé ne reste actif.
5. Avec `127.0.0.1:5057` occupé par `pwsh`, le lanceur se termine avec le code 1,
   nomme le processus et son PID, et ne démarre ni Desktop ni Simulator.
6. Un second `LayupPulse.Simulator.exe` packagé sur le même point d’écoute se
   termine avec le code 2, une seule ligne d’erreur et aucune trace de pile ;
   le premier Simulator reste actif jusqu’au nettoyage explicite du test.

### Inspection visuelle

La session Windows de validation expose réellement 120 DPI, soit 125 %. Toutes
les pages ont été inspectées dans la fenêtre maximisée. Les états vérifiés
comprennent la liste déroulante ouverte, les onglets sélectionnés et non
sélectionnés, la sélection DataGrid active puis inactive, les contrôles
désactivés, le survol, les infobulles, le focus clavier et la navigation au
clavier. La touche Flèche droite a notamment sélectionné l’onglet
« Agrégats télémétriques · 1 s », et la sélection d’une ligne Diagnostics est
restée visible après déplacement du focus vers la navigation. L’aide 3D a été
ouverte depuis son bouton focalisable ; son nom d’automatisation et son texte
d’aide ont été lus via UI Automation.

Les surfaces de fenêtre ont aussi été réduites à 2 048 × 1 104 et
1 707 × 920 pixels physiques sur l’écran à 125 %, soit des surfaces logiques
plus contraignantes que le plein écran attendu à 150 %. Les quatre pages sont
restées utilisables grâce aux barres de défilement explicites. Ce contrôle de
mise en page ne remplace pas une validation de rasterisation sur des sessions
Windows réellement configurées à 100 % et 150 %.

Captures recréées depuis la build Release corrigée après ajout de l’aide 3D :

- `docs/screenshots/v0.2.2/overview-maximized.png` ;
- `docs/screenshots/v0.2.2/combobox-open.png` ;
- `docs/screenshots/v0.2.2/history-alarms-tab.png` ;
- `docs/screenshots/v0.2.2/history-aggregates-tab.png` ;
- `docs/screenshots/v0.2.2/diagnostics-faults-tooltip.png` ;
- `docs/screenshots/v0.2.2/diagnostics-selected-row-columns.png`.

La capture de la vue d’ensemble montre simultanément le cube bleu, le cylindre
gris, les bandes vertes, les lignes grises et l’avertissement sur le caractère
schématique de la scène. Les cinq autres captures confirment la ComboBox
ouverte, les deux onglets Historique, les sélections DataGrid active et
inactive, le défaut traduit avec son infobulle et les quatre colonnes de la
table Diagnostics.

## Risques et limites résiduels

- Le package Windows x64 reste volontairement non signé ; Windows peut afficher
  « Éditeur inconnu » ou SmartScreen.
- La restauration du premier plan reste soumise aux règles Windows contre le
  vol de focus. Si elle échoue, la seconde invocation affiche un message bref.
- La session a permis une validation DPI réelle à 125 % uniquement. Les
  sessions Windows à 100 % et 150 % restent à contrôler avant publication ;
  seules des surfaces logiques plus contraintes ont été éprouvées localement.
- L’avertissement `NU1701` connu de `SkiaSharp.Views.WPF 3.119.0` reste lié à la
  dépendance transitive des graphiques.
- GitHub Actions signale que `actions/checkout@v4` et
  `actions/upload-artifact@v4` ciblent encore Node.js 20, actuellement forcé
  vers Node.js 24 par l’environnement hébergé. La CI reste verte, mais une mise
  à niveau des actions sera nécessaire.
- LayupPulse demeure un démonstrateur logiciel fictif, impropre au pilotage
  d’une machine réelle et à toute fonction de sûreté.
