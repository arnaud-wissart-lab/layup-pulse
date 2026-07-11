# Journal des modifications

Les modifications notables de ce projet sont consignées dans ce fichier. Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/) et le projet utilise le versionnement sémantique lorsque des versions sont publiées.

## [Non publié]

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

[Non publié]: https://github.com/arnaud-wissart-lab/layup-pulse/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/arnaud-wissart-lab/layup-pulse/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/arnaud-wissart-lab/layup-pulse/releases/tag/v0.1.0
