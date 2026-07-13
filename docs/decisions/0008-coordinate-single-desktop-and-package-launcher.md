# ADR 0008 — Coordonner l’instance Desktop et le lanceur packagé

## Statut

Accepté pour la préparation de la version 0.2.2.

## Contexte

Le package pouvait lancer plusieurs processus Desktop et Simulator. Un second
simulateur pouvait échouer sur `127.0.0.1:5057`, tandis que la vérification de
disponibilité acceptait à tort un socket appartenant au premier processus.
Cette situation produisait une trace Kestrel volumineuse et rendait la propriété
des processus ambiguë au moment du nettoyage.

## Décision

Desktop prend un mutex nommé dans l’espace `Local`, donc limité à la session
Windows interactive. Une seconde invocation n’initialise ni hôte ni fenêtre ;
elle envoie une demande d’activation par événements nommés et attend un
acquittement bref. L’instance principale restaure la fenêtre si elle est
minimisée, puis tente de la placer au premier plan.

Le lanceur packagé possède un second mutex nommé pour sérialiser
l’orchestration. Avant de démarrer un processus, il vérifie le mutex Desktop et
le détenteur du point d’écoute. Il refuse explicitement de réutiliser un
simulateur existant afin de conserver une propriété de cycle de vie sans
ambiguïté. Après le démarrage, la disponibilité n’est acceptée que si le PID du
socket correspond au processus Simulator créé par ce lanceur.

Le nettoyage conserve uniquement les objets `Process` créés par l’invocation
courante. Aucun processus découvert par nom, PID ou socket n’est arrêté.

## Conséquences

- Il ne peut y avoir qu’une fenêtre Desktop intentionnelle par session Windows.
- Deux lanceurs packagés ne peuvent pas démarrer des paires concurrentes.
- Un simulateur déjà actif impose une action explicite de l’utilisateur au lieu
  d’être réutilisé silencieusement.
- Les noms des objets de synchronisation deviennent des contrats locaux entre
  Desktop, le lanceur et leurs tests de régression.
