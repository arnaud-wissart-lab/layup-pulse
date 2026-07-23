# ADR 0009 — Adopter sélectivement CODE Framework Documents

## Statut

Accepté pour le rapport de cycle dont le socle est publié en 0.3.0 et dont
l’intégration à **Historique** est livrée en 0.4.0.

## Contexte

LayupPulse doit préparer un rapport imprimable à partir des détails bornés de
l’historique, avec en-tête, pied de page, pagination et filigrane. WPF fournit
les primitives de document, mais la pagination répétée et l’export XPS
nécessitent une infrastructure supplémentaire.

CODE Framework 6.0.0 propose ces capacités dans le paquet autonome
`CODE.Framework.Wpf.Documents`, compatible avec `net10.0-windows` et distribué
sous licence MIT. Ce paquet dépend du socle `CODE.Framework.Wpf`. LayupPulse
utilise déjà `CommunityToolkit.Mvvm` et possède son propre shell, son thème, sa
navigation et sa composition MVVM.

## Décision

LayupPulse adopte uniquement le module `CODE.Framework.Wpf.Documents` dans
`LayupPulse.Desktop`. Ce paquet est la seule référence directe ajoutée au
projet. `CODE.Framework.Wpf` reste une dépendance transitive, dont la version
6.0.0 est épinglée par la gestion centralisée des paquets.

L’intégration est confinée au dossier `Reporting` :

- un modèle de rapport immuable ne contient aucun type WPF ;
- une factory pure projette `ProductionRunHistoryDetails` vers ce modèle et
  borne les alarmes détaillées à 100 ;
- une factory WPF transforme le modèle en `FlowDocumentEx` et utilise son
  en-tête, son pied de page, sa numérotation et son filigrane.
- un presenter construit le document et ouvre une fenêtre d’aperçu appartenant
  à la fenêtre principale ;
- un service de sortie encapsule `PrintHelper`, l’impression WPF et
  l’enregistrement XPS.

`CommunityToolkit.Mvvm` reste l’unique socle MVVM de LayupPulse. Aucun shell
MVC/MVVM, thème, mécanisme de sécurité, `ViewAction` ou système de navigation de
CODE Framework n’est adopté. Le shell, le thème et la navigation existants ne
changent pas. `HistoryViewModel` dépend uniquement d’une abstraction de
présentation et ne connaît ni `Window`, ni `PrintDialog`, ni `FlowDocument`.

Le périmètre de sortie couvre l’impression WPF et l’export XPS fourni par le
module Documents. LayupPulse ne promet aucun export PDF natif. Une éventuelle
conversion PDF devra faire l’objet d’une décision distincte avec ses propres
contraintes de fidélité, de licence et de distribution.

## Conséquences

- Les projets Domain, Application, Contracts et Infrastructure restent
  indépendants de CODE Framework.
- Le rapport ne conserve pas les buckets télémétriques individuels : seule une
  synthèse statistique bornée est projetée.
- La dépendance peut être retirée en remplaçant la factory
  `FlowDocumentEx`, sans modifier le modèle de rapport ni la projection pure.
- Le paquet WPF principal est présent dans le graphe d’exécution uniquement
  parce que le module Documents l’exige ; il ne devient pas une architecture
  applicative.
- `HistoryView` expose la commande de rapport, mais tout usage direct de CODE
  Framework reste dans `LayupPulse.Desktop/Reporting` et sa fenêtre d’aperçu.
