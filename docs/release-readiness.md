# État de préparation à la publication

## État audité

- Date de l’audit : 11 juillet 2026, fuseau Europe/Paris.
- Branche : `agent/domain-machine-state-model`.
- Révision de référence : `49b80357582a9d1d8cd974261093416bac476c3a` (`docs(project): documente la démo, l’architecture et les limites`).
- État initial : arbre de travail propre, aucun diff indexé ou non indexé.
- État final : corrections de l’audit présentes dans l’arbre de travail et non commitées. Les sorties `bin`, `obj` et `artifacts` sont ignorées par Git.
- Portée relue : règles du dépôt, documentation produit/UI/architecture, ADR, projets et références, code Domain/Application/Contracts/Infrastructure/Simulator/Desktop, tests, configuration, scripts, workflow CI, README, capture publique et diff complet.

## Validation exécutée

| Commande ou contrôle | Résultat |
| --- | --- |
| `dotnet format LayupPulse.sln` | Réussi. Le chargeur d’espace de travail signale des avertissements non détaillés, sans modification résiduelle ni code d’échec. |
| `dotnet format LayupPulse.sln --verify-no-changes` | Réussi. Même avertissement générique du chargeur d’espace de travail. |
| `dotnet restore LayupPulse.sln` | Réussi. Avertissement `NU1701` pour l’actif transitif `SkiaSharp.Views.WPF 3.119.0`, restauré avec des cibles .NET Framework pour `net10.0-windows7.0`. |
| `dotnet build LayupPulse.sln -c Release --no-restore` | Réussi, 0 erreur et 3 occurrences de l’avertissement `NU1701` lors de la génération WPF. |
| `dotnet test LayupPulse.sln -c Release --no-build` | Réussi : 94 tests, 0 échec, 0 ignoré. |
| `.\scripts\run-demo.ps1 -SmokeTest -SmokeTestDurationSeconds 5` | Réussi : Simulator et Desktop sont restés actifs pendant cinq secondes, puis le script a nettoyé les deux processus. |
| `.\scripts\package-demo.ps1` | Réussi : publications autonomes `win-x64`, smoke test intégré réussi et archive ZIP de 119,3 Mio créée. |
| `.\artifacts\LayupPulse-win-x64\Run-LayupPulse.ps1 -SmokeTest -SmokeTestDurationSeconds 5` | Réussi : second smoke test direct du package autonome. |
| Parcours Windows UI Automation à 1280 × 800 | Réussi : connexion, disponibilité des commandes, chargement de recette, démarrage, pause, reprise, défaut haute température, acquittement, levée du défaut, reset et fermeture normale. |
| Capture `PrintWindow` à 1280 × 800 | Inspectée : aucune perte horizontale du shell ou des commandes ; le contenu inférieur utilise le défilement vertical prévu. Le focus clavier du bouton initial est visible. |
| Liens Markdown locaux | Réussi : toutes les cibles locales référencées existent. Les liens externes principaux répondent ; les services de badges SVG ne sont pas interprétés comme pages HTML par l’outil de contrôle. |
| Capture publique `docs/screenshots/overview-running.png` | Inspectée et cohérente avec l’interface actuelle ; aucun élément obsolète identifié. |
| `git diff --check` | Réussi après rédaction du présent rapport. |

## Scénarios fonctionnels couverts

- Les transitions métier, commandes invalides, validation de recette, arrêt, défauts, reset et cycle terminé sont couvertes par les tests du domaine et du simulateur.
- La pause ne fait pas progresser le cycle ; ce comportement est testé avec 100 échantillons en pause.
- `Stop` termine le run avec le statut `Aborted` et remet la machine simulée à `Ready`.
- L’acquittement d’une alarme ne lève pas sa condition ; le parcours UI confirme que l’action devient indisponible après acquittement, puis que la condition doit être levée séparément.
- L’hystérésis, les temporisations, la récupération de communication et l’unicité des alarmes actives sont couvertes par les tests du moteur d’alarmes.
- La reconnexion est sérialisée et son délai est annulable ; les tests vérifient l’absence de tentatives concurrentes et l’arrêt lors d’une déconnexion explicite.
- Les canaux serveur, historiques télémétriques, agrégats, alarmes et diagnostics sont bornés. Les graphiques réutilisent l’historique borné et limitent leur rendu à 600 points par signal.
- La géométrie 3D statique est créée une fois ; la tête réutilise une transformation et les chemins ne sont redécoupés qu’au changement de palier de progression.
- La fermeture normale du client a été exercée après un cycle et un défaut. Desktop ne possède pas le processus Simulator ; les scripts de démonstration possèdent et nettoient les deux processus qu’ils lancent.

## Défauts corrigés

1. **Blocage de la télémétrie après remplacement de session.** Le pipeline comparait les numéros de séquence d’une nouvelle session à ceux de l’ancienne. Une séquence repartant à 1 pouvait être rejetée indéfiniment. Une portée de séquence explicite est maintenant ouverte lors de la création d’une nouvelle session, sans perdre l’historique borné.
2. **Retard Dispatcher potentiellement non borné.** Chaque publication applicative créait un travail WPF, même si la valeur précédente n’avait pas encore été traitée. Le shell ne conserve désormais qu’un état en attente et remplace celui-ci par le plus récent. Un test vérifie que 100 publications ne créent qu’un travail UI en attente.
3. **Promesse de canal incorrecte.** Le canal abonné déclarait `SingleWriter = true`, alors que la publication et la terminaison peuvent provenir de threads distincts. La configuration autorise maintenant plusieurs producteurs synchronisés par le canal.
4. **Frontières de présentation trop larges.** `DiagnosticsViewModel` dépendait de `GrpcMachineGatewayOptions` et Desktop référençait directement Contracts sans l’utiliser. Le ViewModel reçoit maintenant une `Uri` neutre et la référence de projet superflue a été supprimée.
5. **Abstractions de persistance spéculatives.** Des ports, modèles de requête et un service d’historique n’avaient ni implémentation, ni composition, ni consommateur. Ils ont été retirés afin que les abstractions correspondent à des frontières réelles. README, architecture et ADR précisent désormais que toute cette intégration reste absente.
6. **Avertissement d’analyse du domaine.** La validation négative de `alarmCount` utilise la garde standard .NET, supprimant l’avertissement `CA1512`.

## Audit d’architecture

- Domain ne référence aucun autre projet et ne dépend ni de WPF, EF Core, gRPC, Contracts ou Infrastructure.
- Application ne référence que Domain et ne dépend pas de WPF.
- Les ViewModels ne référencent ni `DbContext`, ni types gRPC générés, ni entités de base de données.
- Les types gRPC restent dans Contracts, Infrastructure et Simulator.
- Desktop est la racine de composition WPF ; les appels à `GetRequiredService` y restent limités à la composition. Simulator applique la même règle dans sa racine de composition.
- Aucune référence circulaire n’existe ; le test d’architecture vérifie la liste exacte des références de projets.
- Aucun `.Result`, `.Wait()`, `Task.Run` inutile, boucle infinie non annulable ou tâche longue sans propriétaire n’a été trouvé dans le code applicatif.

## Hygiène du dépôt

- Aucun secret, chemin absolu propre à une machine, base locale, log, symbole, binaire ou sortie de publication n’est versionné.
- Les seuls `NotSupportedException` trouvés appartiennent à des doubles de test pour des opérations volontairement hors scénario ; aucun `NotImplementedException` n’existe.
- Aucun `TODO`, `FIXME`, bypass de débogage ou code commenté obsolète n’a été trouvé.
- La page `PlaceholderView` restante est intentionnelle et limitée à Historique ; cette limitation est visible dans l’interface, le README et la démonstration.
- `appsettings.Development.json` du simulateur est intentionnel, portable et limité à des paramètres fictifs de bouclage local.

## Limites connues

- L’historique SQLite, l’enregistrement durable et la page Historique ne sont pas implémentés. Le scénario « l’historique survit au redémarrage » n’a donc pas pu être exécuté et le critère P0 correspondant n’est pas satisfait.
- Le build conserve l’avertissement `NU1701` lié à `SkiaSharp.Views.WPF 3.119.0`, dépendance transitive de la bibliothèque de graphiques. Les smoke tests réduisent le risque de démarrage, mais ne prouvent pas une compatibilité complète.
- Aucun flux de traces de liaison WPF issu d’une session Visual Studio Debug n’a été capturé. Le parcours UI et les valeurs visibles n’ont révélé aucun symptôme de liaison défaillante, sans constituer une preuve d’absence de tout avertissement.
- Le panneau de repli 3D a été relu, mais une panne d’initialisation 3D n’a pas été injectée manuellement.
- La vérification visuelle a été réalisée à 1280 × 800 au facteur d’échelle courant, pas à l’ensemble de la plage 100–200 % annoncée par la spécification UI.
- Le package Windows x64 n’est pas signé et peut déclencher SmartScreen.
- Le workflow GitHub Actions n’a pas été déclenché par cet audit local ; seuls ses commandes et son contenu ont été relus.

## Risques résiduels

- Une incompatibilité future de SkiaSharp avec une mise à jour du runtime ou du poste graphique peut affecter uniquement les tendances. Le contrôle prévoit un repli, mais la dépendance doit rester surveillée.
- La reconnexion à une session réellement recréée après redémarrage complet du processus est couverte au niveau des règles et doubles de transport, pas par un scénario manuel de redémarrage du processus pendant le parcours UI.
- L’absence de persistance empêche toute promesse de traçabilité durable et doit rester explicite dans une démonstration d’entretien.

## Recommandation finale

**Ready with documented limitations.**

La révision est cohérente pour une publication GitHub de démonstrateur technique : elle compile, ses tests passent, les processus et le package démarrent, le parcours opérateur principal est stable et les frontières techniques sont défendables en entretien. Elle ne doit toutefois pas être présentée comme P0 complet, car l’historique durable manque, ni comme application industrielle ou compatible avec du matériel réel. L’avertissement WPF transitif et les validations manuelles non exécutées ci-dessus doivent accompagner toute release.
