# ADR 0004 — Utiliser ScottPlot et WPF 3D pour le tableau de bord

## Statut

Acceptée le 10 juillet 2026.

## Contexte

Overview doit présenter quatre tendances bornées et une cellule 3D fictive sans lier la télémétrie brute au Dispatcher. Les bibliothèques doivent être stables, compatibles avec .NET 10 et proportionnées à une démonstration locale. La scène ne nécessite ni import CAO, ni rendu photoréaliste, ni moteur DirectX propre au projet.

## Décision

Les tendances utilisent `ScottPlot.WPF` 5.1.59. Elles lisent l’historique de 60 secondes déjà borné par `TelemetryPipeline`, se limitent à 600 points par signal et se rafraîchissent au plus toutes les 200 ms.

La scène utilise `HelixToolkit.Wpf` 3.1.2 sur le moteur WPF 3D standard. `HelixToolkit.Wpf.SharpDX` n’est pas retenu : les besoins se limitent à des primitives simples, et la variante classique réduit les dépendances de périphérique et le risque d’initialisation dans le contexte .NET 10/WPF. Aucun moteur DirectX n’est écrit dans le dépôt.

La géométrie statique est créée une fois. La tête est déplacée par une transformation réutilisée et la trajectoire bornée n’est redécoupée qu’au changement d’un pourcentage entier. Les deux contrôles arrêtent ou limitent leur travail de rendu indépendamment de l’acquisition.

## Conséquences

- Le domaine, le contrat gRPC, la persistance et les règles d’alarme restent inchangés.
- Les graphiques et la scène sont confinés à Desktop et peuvent afficher un panneau de repli sans interrompre les commandes.
- WPF 3D fournit l’orbite et le zoom nécessaires, mais pas les performances et effets avancés de SharpDX.
- ScottPlot restaure actuellement `SkiaSharp.Views.WPF` avec un avertissement NuGet de compatibilité de framework ; le contrôle a néanmoins été compilé et exécuté sur .NET 10. Cet avertissement devra être réévalué lors d’une mise à jour du package.
