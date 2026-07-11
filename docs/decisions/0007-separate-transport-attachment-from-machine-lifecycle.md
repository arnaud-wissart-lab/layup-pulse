# ADR 0007 — Séparer l’attachement transport du cycle de vie machine

## Statut

Acceptée le 11 juillet 2026.

## Contexte

Le client gRPC utilisait la commande métier `ConnectRequested` pour représenter simultanément l’ouverture d’un canal local et l’activation de la session machine simulée. Lorsqu’un client disparaissait sans envoyer `Disconnect`, le simulateur continuait correctement son cycle en `Ready`, `Running`, `Paused`, `Faulted` ou `Completed`. Un client de remplacement envoyait toutefois un nouveau `ConnectRequested`, que le domaine rejetait légitimement puisque cette transition n’est valide que depuis `Disconnected`.

Autoriser `ConnectRequested` depuis les états actifs aurait masqué la confusion de responsabilités et risqué de modifier ou réinitialiser le contexte métier. Le démonstrateur reste local, mono-opérateur et sans authentification ; la gestion de sessions concurrentes ou multi-utilisateurs demeure hors périmètre.

## Décision

`IMachineGateway` expose `AttachAsync`, qui ouvre et vérifie une session de transport puis retourne une `MachineTransportAttachment` contenant la session locale et l’instantané courant. L’adaptateur gRPC réalise cette vérification par `GetSnapshot` et n’envoie aucune commande métier pendant l’attachement.

`MachineSessionService` examine ensuite l’instantané :

- si la machine est `Disconnected`, il envoie la commande métier `ConnectRequested` et conserve les transitions existantes vers `Connecting`, puis `Ready` ;
- si la machine est déjà active, il adopte l’instantané et démarre le flux télémétrique sans nouvelle transition métier.

La déconnexion explicite conserve son comportement : elle annule les tâches et le flux possédés, envoie `Disconnect`, puis ferme la session locale. L’abandon ou la destruction de la passerelle ferme uniquement le transport local et ne modifie pas la machine distante.

## Conséquences

- Un client local de remplacement peut reprendre un simulateur actif sans perdre recette, exécution, progression ni défauts actifs.
- La règle du domaine reste stricte : `ConnectRequested` est accepté uniquement depuis `Disconnected`.
- L’attachement et sa lecture initiale restent annulables, soumis au délai gRPC et nettoient le canal en cas d’échec.
- Aucun identifiant d’utilisateur, jeton d’authentification, bail distribué ou arbitrage multi-client n’est introduit.
- Le contrat protobuf n’a pas besoin d’une nouvelle RPC : `GetSnapshot` fournit déjà la vérification et l’état nécessaires à l’attachement local.
