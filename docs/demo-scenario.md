# Scénario de démonstration

## Objectif

Ce scénario présente en environ deux minutes les comportements logiciels observables de LayupPulse. Il utilise uniquement la cellule fictive et ne doit jamais être décrit comme une démonstration de commande ou de sûreté d’une machine réelle.

## Préparation

Depuis la racine du dépôt :

```powershell
./scripts/run-demo.ps1 -Build
```

Avant une présentation, vérifier que le port `5057` est libre, que l’affichage est lisible à la mise à l’échelle Windows utilisée et que les boutons de défaut sont visibles dans **Diagnostics**. Répéter la coupure de communication séparément avant de décider de la montrer.

## Déroulé cible — environ deux minutes

| Temps | Action | Résultat attendu |
| --- | --- | --- |
| 0:00–0:10 | Lancer LayupPulse, puis sélectionner **Connecter**. | La connexion passe à `Connected` et la machine à `Ready`. |
| 0:10–0:25 | Charger **Wing Panel Demo**, puis sélectionner **Démarrer**. | La machine passe à `Running` et la progression commence. |
| 0:25–0:45 | Montrer les valeurs, les quatre tendances et la scène 3D. | Les courbes avancent, la tête se déplace et la mémoire d’affichage reste bornée. |
| 0:45–1:00 | Ouvrir **Diagnostics** et injecter **Surtempérature**. | La machine passe immédiatement à `Faulted`; l’alarme apparaît après son debounce déterministe. |
| 1:00–1:15 | Ouvrir **Alarmes** et acquitter l’alarme. | L’alarme devient acquittée mais reste active : la condition n’est pas effacée. |
| 1:15–1:30 | Revenir dans **Diagnostics**, lever la surtempérature, puis sélectionner **Reset**. | La condition disparaît, l’alarme passe à `Cleared` et la machine revient à `Ready`. |
| 1:30–1:45 | Ouvrir **Historique**, filtrer sur **En défaut** et sélectionner le run interrompu. | Le résumé, l’alarme et les agrégats télémétriques d’une seconde sont relus depuis SQLite. |
| 1:45–2:00 | Ouvrir **Rapport du cycle** et parcourir ses pages. | L’aperçu affiche l’avertissement sur les données simulées et propose l’impression Windows ou l’export XPS. |
| Après 2:00 | Optionnel : injecter une coupure de communication, puis la lever. | La session affiche la reconnexion et revient à un état connecté sans bloquer l’interface. |

## Vérification de la persistance

Après le parcours principal, fermer uniquement Desktop, le relancer, se reconnecter si nécessaire, puis rouvrir **Historique**. Le run précédent doit rester visible depuis `%LOCALAPPDATA%\LayupPulse\layuppulse.db`. Les détails montrent les alarmes et agrégats d’une seconde ; aucun échantillon brut à 20 Hz n’est stocké.

Présenter cette capacité comme un historique local de démonstration.
L’interface opérateur fournit un aperçu, une impression Windows et un export
XPS du cycle sélectionné. Elle ne fournit ni export PDF natif, ni
authentification, ni base serveur, ni garantie de traçabilité industrielle
validée. Un PDF peut uniquement être produit en sélectionnant une imprimante
Windows compatible.

## Validation technique du rapport de cycle

Cette validation couvre la projection, le document et leur raccordement à
`HistoryView`.

Depuis la racine du dépôt :

```powershell
dotnet restore LayupPulse.sln
dotnet build LayupPulse.sln -c Release --no-restore
dotnet test tests/LayupPulse.Tests/LayupPulse.Tests.csproj `
  -c Release `
  --no-build `
  --filter "FullyQualifiedName~ProductionRunReport"
```

Résultat attendu :

1. les tests `ProductionRunReport` réussissent sans test ignoré ;
2. la projection restitue l’identifiant, la recette, la référence pièce, les
   dates, la durée, l’état final, la progression, les indicateurs, la santé
   minimale et les métadonnées de génération ;
3. les buckets désordonnés produisent une période, un total d’échantillons et
   des plages min–max corrects sans être recopiés dans le modèle de rapport ;
4. l’absence de fin de cycle ou d’agrégats reste explicite ;
5. seules les 100 premières alarmes sont détaillées et le nombre d’alarmes
   omises est conservé ;
6. la factory WPF crée un `FlowDocumentEx` avec avertissement, en-tête, pied de
   page, numérotation et filigrane ;
7. un fichier XPS temporaire non vide est réellement sérialisé, contrôlé puis
   supprimé par le test ;
8. la commande reste désactivée sans détails complets et ignore les réponses
   périmées ou discordantes ;
9. les contrôles de l’aperçu possèdent des noms d’automatisation, des
   infobulles et des touches d’accès.

L’inspection visuelle complémentaire doit vérifier l’aperçu, la pagination,
l’annulation de l’impression, l’export puis l’ouverture du XPS et les mises à
l’échelle Windows réellement disponibles sur le poste de validation.

## Coupure de communication

Cette séquence est optionnelle parce qu’elle dépend du rythme de présentation :

1. établir une session et démarrer un cycle ;
2. injecter **Coupure de communication** dans **Diagnostics** ;
3. observer `Reconnecting` et l’alarme de timeout après le délai configuré ;
4. lever le défaut ;
5. attendre le rétablissement du flux et les échantillons frais ;
6. ne poursuivre que si l’état visible est stable.

Si le rétablissement tarde, lever le défaut, attendre le backoff borné, puis utiliser **Déconnecter** et **Connecter**. Ne jamais improviser une action présentée comme un arrêt d’urgence.

## Fin de démonstration

Fermer la fenêtre LayupPulse. `scripts/run-demo.ps1` arrête alors le simulateur et rend un code non nul si le démarrage ou l’application s’est terminé en erreur.
