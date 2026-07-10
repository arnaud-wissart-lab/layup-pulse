# ADR 0001 — Utiliser gRPC entre le simulateur et le client de bureau

## Statut

Acceptée le 10 juillet 2026.

## Contexte

LayupPulse exécute la cellule simulée et l’application WPF dans deux processus distincts. Le transport doit fournir un instantané ponctuel, des commandes corrélées avec rejet explicite, un flux télémétrique serveur à haute fréquence et une annulation propre. Les contrats ne doivent pas introduire ASP.NET Core ou des types générés dans le domaine et l’application.

Le système reste un démonstrateur pour une cellule fictive. Ce choix de transport ne lui confère aucune compatibilité avec du matériel réel, aucune fonction de sûreté et aucune aptitude au pilotage industriel.

## Décision

Le simulateur héberge un service gRPC ASP.NET Core en HTTP/2. Le schéma protobuf versionné `layuppulse.v1` appartient à `LayupPulse.Contracts` et définit :

- `GetSnapshot` pour l’état ponctuel ;
- `StreamTelemetry` en streaming serveur ;
- `ExecuteCommand` pour les commandes corrélées ;
- `InjectFault` et `ClearFault` pour les scénarios déterministes réservés au simulateur.

Les messages protobuf sont orientés transport. Des mappings C# manuels convertissent les enums, commandes, rejets, recettes résumées et échantillons ; AutoMapper n’est pas utilisé. Le processus Simulator est la racine de composition du serveur. Le futur client sera un adaptateur Infrastructure derrière `IMachineGateway`.

Le développement local utilise par défaut `http://127.0.0.1:5057` en HTTP/2 clair. L’écoute est limitée à une adresse de bouclage et n’est pas une configuration de production. Les options d’endpoint, de graine et de fréquence sont fournies par la configuration ASP.NET Core et la ligne de commande.

## Raisons

- Protobuf fournit un contrat compact, versionnable et indépendant de WPF.
- Le streaming serveur correspond directement au flux télémétrique unidirectionnel.
- Les primitives gRPC exposent l’annulation, les statuts de transport et HTTP/2 sans protocole applicatif ad hoc.
- Les types client et serveur générés réduisent les divergences de schéma tout en laissant les règles dans le domaine.
- Un service local TCP permet de tester le même franchissement de processus que le futur client de bureau.

Les sockets nommées auraient renforcé le couplage à Windows et demandé un protocole de framing propre. WebSocket aurait nécessité de définir manuellement les requêtes, réponses, corrélations et erreurs. Une API HTTP avec interrogation périodique aurait été moins adaptée à 20 Hz et à la détection immédiate des interruptions.

## Conséquences

- `LayupPulse.Contracts` dépend de la toolchain protobuf et de l’API gRPC, mais reste indépendant du domaine, de WPF et de la persistance.
- `LayupPulse.Simulator` dépend des contrats et du domaine et contient les mappings serveur explicites.
- Les changements incompatibles du schéma nécessiteront une nouvelle version de package protobuf.
- Les flux sont distribués par des canaux bornés afin qu’un client lent ne crée pas de croissance mémoire non bornée.
- `CommunicationDrop` devient observable par le statut gRPC `Unavailable`; le serveur reste disponible pour lever le défaut.
- Un transport distant ou un déploiement hors poste local exigerait une décision séparée sur TLS, l’authentification, l’exposition réseau et l’exploitation. Il n’est pas autorisé par cette ADR.
