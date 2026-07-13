LayupPulse — démonstrateur Windows x64
======================================

Prérequis
---------

- Windows 10 ou Windows 11, x64.
- Le point d’écoute TCP local 127.0.0.1:5057 doit être disponible.
- Aucun runtime .NET n’est requis : ce package est autonome.

Lancement
---------

1. Extrayez toute l’archive ZIP dans un dossier local accessible en écriture.
2. Double-cliquez sur Run-LayupPulse.cmd.
3. Dans LayupPulse, sélectionnez Connecter, chargez Wing Panel Demo, puis
   sélectionnez Démarrer.
4. Fermez la fenêtre LayupPulse lorsque vous avez terminé. Le lanceur arrête
   alors uniquement le simulateur qu’il a lui-même démarré.

Le lanceur empêche deux orchestrations simultanées. Il s’arrête avec un message
clair si LayupPulse est déjà ouvert, si un autre simulateur LayupPulse écoute
déjà ou si le point d’écoute local appartient à un processus sans rapport.

Avertissement « Éditeur inconnu »
---------------------------------

Ce package de portfolio n’est pas signé numériquement. Windows peut donc
afficher « Éditeur inconnu » ou un avertissement Microsoft Defender SmartScreen.
Ce comportement est attendu et aucune protection Windows n’est contournée.

N’exécutez le package que s’il a été téléchargé depuis le dépôt officiel :
https://github.com/arnaud-wissart-lab/layup-pulse

Pour un ZIP de confiance, avant de l’extraire :

1. Cliquez avec le bouton droit sur LayupPulse-win-x64.zip.
2. Ouvrez Propriétés.
3. Dans l’onglet Général, cochez Débloquer, puis sélectionnez Appliquer.
4. Extrayez ensuite l’archive normalement.

Alternative explicite dans PowerShell, à exécuter uniquement sur le ZIP de
confiance que vous venez de télécharger :

Unblock-File -LiteralPath .\LayupPulse-win-x64.zip

Le lanceur n’exécute jamais Unblock-File automatiquement.

Historique local
----------------

Les résumés de cycle, alarmes et agrégats télémétriques d’une seconde sont
stockés dans %LOCALAPPDATA%\LayupPulse\layuppulse.db. Ils restent disponibles
après le redémarrage de l’application de bureau. Les échantillons bruts à 20 Hz
ne sont pas stockés. Cet historique sert uniquement à la démonstration
logicielle ; il ne constitue pas une traçabilité industrielle validée.

Avertissement de sécurité
-------------------------

LayupPulse est un démonstrateur technique indépendant. Il n’est ni affilié à
un constructeur industriel, ni fondé sur des logiciels, conceptions de machine
ou données de production propriétaires. Il ne doit pas être utilisé pour
piloter une machine réelle ni pour mettre en œuvre une fonction de sûreté.

Licence
-------

LayupPulse est distribué sous licence MIT. Consultez LICENSE.txt et
THIRD-PARTY-NOTICES.txt.
