# ADR 0003 — Borner le pipeline et sérialiser la reconnexion

## Statut

Acceptée le 10 juillet 2026.

## Contexte

Le simulateur produit environ 20 échantillons par seconde, jusqu’à 50 selon la configuration. L’interface ne doit ni traiter chaque valeur sur le Dispatcher, ni conserver une collection croissante. Une coupure doit rester récupérable et ne doit pas créer plusieurs boucles concurrentes. Les alarmes exigent néanmoins que chaque échantillon acquis soit évalué avant la coalescence UI.

## Décision

Le client lit le flux gRPC séquentiellement, sans file cliente intermédiaire. Cette lecture exerce une contre-pression. Le serveur conserve son canal borné de capacité 8 par abonné en mode `DropOldest`; un client en retard reçoit donc la valeur la plus récente et mesure les pertes par les numéros de séquence.

`TelemetryPipeline` traite les règles d’alarme et les agrégats sur le thread d’acquisition, conserve au plus 60 secondes/3 000 valeurs et publie une capture immuable au plus toutes les 100 ms. Les agrégats d’une seconde sont bornés à 60 et restent en mémoire dans cet incrément.

`MachineSessionService` possède exactement un superviseur de flux et un moniteur de fraîcheur par connexion demandée. Après interruption ou timeout, le superviseur réessaie avec un backoff exponentiel de 250 ms à 2 s. Il réutilise la session tant que les appels unitaires répondent, sinon il l’abandonne localement et en crée une nouvelle. La déconnexion explicite annule le superviseur, la tentative de flux et le délai avant de fermer le transport.

## Conséquences

- L’acquisition, l’agrégation et l’affichage ont des cadences indépendantes et observables.
- Une surcharge privilégie explicitement la fraîcheur et incrémente les compteurs de perte/coalescence.
- Les alarmes ne perdent pas les échantillons uniquement parce que l’UI est limitée à 10 Hz.
- La mémoire est bornée même sans persistance.
- La reconnexion ne peut pas se chevaucher, mais un arrêt du serveur peut rester visible jusqu’au prochain délai borné.
- Les agrégats ne survivent pas au redémarrage ; leur persistance EF Core est hors périmètre.
