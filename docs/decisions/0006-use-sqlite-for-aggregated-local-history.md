# ADR 0006 — Utiliser SQLite pour un historique local agrégé

## Statut

Acceptée et implémentée le 11 juillet 2026.

## Contexte

Le démonstrateur doit conserver localement les exécutions simulées, les occurrences d’alarme et une quantité bornée de données de tendance. La télémétrie brute peut atteindre 50 Hz et ne doit pas être écrite directement depuis le thread WPF ni créer une base ou une file non bornée. Le dépôt n’a besoin ni d’un serveur de base de données, ni d’un modèle multi-utilisateur.

## Décision

L’historique local utilise EF Core avec SQLite dans `LayupPulse.Infrastructure`. `LayupPulse.Application` définit les ports d’écriture et de lecture ainsi que les modèles consommés par la session et la page Historique ; le domaine ne reçoit aucun attribut ni type EF Core.

La persistance télémétrique porte sur les agrégats UTC d’une seconde produits par `TelemetryPipeline`, pas sur chaque échantillon brut. Une file bornée alimente des contextes courts créés par `IDbContextFactory`. Les lectures sont asynchrones, annulables et bornées par run ou par nombre maximal de résultats. Les migrations créent la base par utilisateur sous `%LOCALAPPDATA%\LayupPulse\layuppulse.db`.

## Conséquences

- Un fichier local suffit à la démonstration et peut être supprimé sans service externe.
- L’agrégation réduit fortement le volume et découple l’acquisition de l’écriture disque.
- La saturation de la file ou une défaillance SQLite produit un diagnostic non fatal ; elle ne doit jamais arrêter le flux télémétrique.
- Le chemin de base ne doit jamais être versionné, inclus dans un package ou écrit dans le répertoire du dépôt.
- Les tests de round trip utilisent SQLite réel et rouvrent la base au travers d’une nouvelle factory de contextes.
- SQLite ne transforme pas LayupPulse en MES et ne fournit aucune garantie de traçabilité industrielle validée.
