# ADR 0002 — Centraliser la session machine hors de WPF

## Statut

Acceptée le 10 juillet 2026.

## Contexte

Le premier client WPF doit partager un état de connexion, un instantané et un flux télémétrique entre la vue d’ensemble, les diagnostics et l’en-tête. La lecture gRPC s’exécute hors du thread de l’interface, doit être annulable et ne doit pas faire dépendre Application de WPF. Un bus d’événements serait disproportionné pour une session unique.

## Décision

`MachineSessionService`, dans Application, possède le cycle de vie d’une unique `IMachineSession`. Il conserve uniquement le dernier échantillon, compte les échantillons reçus, détecte la télémétrie périmée avec `TimeProvider` et publie un `MachineSessionState` immuable par événement .NET direct.

Les notifications issues de la télémétrie sont coalescées à dix publications par seconde au maximum. Le shell Desktop reçoit l’événement puis remarshal une mise à jour bornée sur le Dispatcher. Les ViewModels n’utilisent ni types gRPC ni primitives de threading WPF pour piloter la session.

Le Generic Host construit et détruit la passerelle, le service de session, les ViewModels et la fenêtre principale. La fermeture de la fenêtre est temporairement différée le temps d’annuler le flux, d’envoyer la déconnexion avec un délai fini, d’arrêter l’hôte et de le libérer de façon asynchrone.

## Conséquences

- Toutes les pages observent une source de vérité unique sans état global mutable.
- La détection de fraîcheur et la propagation des rejets sont testables sans Dispatcher ni serveur gRPC.
- La présentation reste responsable du remarshalement et des collections observables.
- Une interruption inattendue ferme la session locale et laisse l’interface dans un état reconnectable.
- Les diagnostics récents sont bornés ; cet historique en mémoire ne remplace pas la future persistance.
- La reconnexion automatique et la politique de pipeline ajoutées ensuite sont précisées par l’ADR 0003.
