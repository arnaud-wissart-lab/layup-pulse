# Journal des modifications

Les modifications notables de ce projet sont consignées dans ce fichier. Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/) et le projet utilise le versionnement sémantique lorsque des versions sont publiées.

## [Non publié]

## [0.2.2] — 2026-07-13

### Corrigé

- Garantie d’une seule instance Desktop par session Windows, avec restauration et activation de la fenêtre existante.
- Prévol du lanceur packagé contre les orchestrations concurrentes, les simulateurs existants et les conflits du point d’écoute `127.0.0.1:5057`.
- Encodage UTF-8 compatible avec Windows PowerShell 5.1 pour les messages français du package.
- États sombres explicites des listes déroulantes, onglets, grilles, cases à cocher, zones de texte et barres de défilement.
- Suppression des colonnes DataGrid auto-générées dans les vues qui déclarent leurs propres colonnes.
- Libellés français, noms techniques et aide contextuelle des défauts de simulation et des mesures de diagnostic.
- Documentation de l’avertissement Windows relatif à l’éditeur inconnu, sans contournement des protections du système.

## [0.2.1] — 2026-07-11

### Corrigé

- Centralisation de la version `0.2.1` pour les builds, Diagnostics, le packaging et la CI.
- Finalisation des runs en défaut après accumulation du premier échantillon terminal, avec traitement explicite des coupures de communication sans télémétrie terminale.
- Abandon du run local lors du remplacement par un simulateur inactif, sans rattacher sa télémétrie `Ready` à l’ancien cycle.
- Protection de l’Historique contre l’écrasement de résultats récents par des requêtes de filtre ou de sélection obsolètes.

## [0.2.0] — 2026-07-11

### Ajouté

- Persistance locale EF Core 10 et SQLite des exécutions, alarmes et agrégats UTC d’une seconde.
- Migration initiale, file d’écriture bornée et requêtes asynchrones à contextes courts.
- Page Historique fonctionnelle avec tri récent, filtre d’état final et détails du run sélectionné.
- Tests d’intégration SQLite réels couvrant les migrations, les upserts et la réouverture de la base.

### Corrigé

- Association des agrégats et alarmes à l’identifiant du cycle actif au lieu de `Guid.Empty`.
- Conservation de l’association d’alarme après un passage en défaut pour les règles temporisées.

### Modifié

- Alignement des agrégats télémétriques sur des buckets UTC d’une seconde.
- Remontée des échecs SQLite comme diagnostics non fatals sans interrompre la télémétrie.

## [0.1.0] — 2026-07-11

### Ajouté

- Modèle d’état déterministe et recettes fictives.
- Simulateur séparé et contrat gRPC versionné.
- Application WPF, pipeline télémétrique borné, alarmes, diagnostics, tendances et visualisation 3D.
- Tests unitaires, d’architecture, de transport et d’intégration du simulateur.
- Script de démonstration en une commande avec readiness borné et nettoyage des processus.
- Publication Windows x64 autonome, smoke test et archive ZIP reproductible.
- Workflow GitHub Actions Windows pour le formatage, le build, les tests et le packaging.
- Documentation de démonstration, de contribution, de dépendances et de décisions d’architecture.

### Corrigé

- Acceptation de la télémétrie lorsque la séquence repart après le remplacement d’une session.
- Coalescence des publications vers le Dispatcher afin de borner le retard de l’interface.
- Configuration du canal télémétrique compatible avec une terminaison depuis un autre thread.

### Modifié

- Isolation des ViewModels vis-à-vis du transport gRPC.
- Retrait des abstractions de persistance sans implémentation ni consommateur.
- Documentation explicite des limites de persistance et de préparation à la publication.

[Non publié]: https://github.com/arnaud-wissart-lab/layup-pulse/compare/v0.2.2...HEAD
[0.2.2]: https://github.com/arnaud-wissart-lab/layup-pulse/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/arnaud-wissart-lab/layup-pulse/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/arnaud-wissart-lab/layup-pulse/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/arnaud-wissart-lab/layup-pulse/releases/tag/v0.1.0
