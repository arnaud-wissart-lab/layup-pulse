# ADR 0006 — Utiliser SQLite pour un historique local agrégé

## Statut

Acceptée comme cible d’architecture le 11 juillet 2026 ; implémentation concrète en cours.

## Contexte

Le démonstrateur doit conserver localement les exécutions simulées, les occurrences d’alarme et une quantité bornée de données de tendance. La télémétrie brute peut atteindre 50 Hz et ne doit pas être écrite directement depuis le thread WPF ni créer une base ou une file non bornée. Le dépôt n’a besoin ni d’un serveur de base de données, ni d’un modèle multi-utilisateur.

## Décision

L’historique local utilisera EF Core avec SQLite dans `LayupPulse.Infrastructure`. `LayupPulse.Application` définira les ports de production, d’alarme, d’agrégats et de gestion du stockage local avec leurs premiers consommateurs concrets ; le domaine ne recevra aucun attribut ni type EF Core.

La persistance télémétrique porte sur les agrégats d’une seconde produits par `TelemetryPipeline`, pas sur chaque échantillon brut. Les lectures sont asynchrones, annulables et bornées par run ou par nombre maximal de résultats. Les migrations, le chemin de base par utilisateur, la politique de rétention et la composition du service d’enregistrement doivent être explicites avant d’activer la page Historique.

## Conséquences

- Un fichier local suffit à la démonstration et peut être supprimé sans service externe.
- L’agrégation réduit fortement le volume et découple l’acquisition de l’écriture disque.
- Une file d’écriture bornée et une stratégie de drainage à l’arrêt restent nécessaires dans l’implémentation.
- Le chemin de base ne doit jamais être versionné, inclus dans un package ou écrit dans le répertoire du dépôt.
- La page Historique ne peut être déclarée fonctionnelle qu’après ajout des migrations, adaptateurs, tests de round trip et gestion observable des échecs.
- SQLite ne transforme pas LayupPulse en MES et ne fournit aucune garantie de traçabilité industrielle validée.
