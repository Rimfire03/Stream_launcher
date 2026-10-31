# Stream Launcher

[![Buy Me A Coffee](https://img.shields.io/badge/Buy%20Me%20A%20Coffee-Donate-orange.svg?logo=buy-me-a-coffee&logoColor=white)](https://buymeacoffee.com/slide598b)

Application Windows gratuite (.NET 8, self-contained), distribuée uniquement par installateur MSI signé (release GitHub), pour préparer un live Twitch ou Kick : lance des applications, les place sur le bon écran, clique dans leurs fenêtres, change la scène, le profil et la collection d'OBS, et met à jour le titre, le jeu et les tags de la chaîne sur Twitch et Kick. Profils stockés dans `profiles.json` à côté de l'exe. Un basculement (même touche Stream Deck) lance le live puis ferme ses fenêtres.

## Utilisation

- Créer un profil, puis ajouter des étapes :
  - **Importer fenêtres ouvertes** : ouvre tes apps, place-les à la main, importe → étapes `Launch` + `Place` (écran + position + taille).
  - **+ Application** : étape de lancement manuelle. Ou glisser-déposer des raccourcis (.lnk), exe ou documents dans la fenêtre (le raccourci est résolu vers sa cible).
  - **+ Déplacer fenêtre** : clique le bouton, puis déplace (ou redimensionne) toi-même la fenêtre voulue : sa nouvelle position est enregistrée. **+ Réduire fenêtre** : clique le bouton, puis clique sur la fenêtre à réduire (le clic est capturé, pas transmis). Annulation par la barre affichée en haut de l'écran.
  - **+ Navigateur** : ouvre une URL http(s) dans une **nouvelle fenêtre** du navigateur choisi (`Browser` : Chrome, Edge, Firefox, Brave, Opera, Vivaldi, ou `Defaut` pour le navigateur par défaut de Windows, sans garantie de nouvelle fenêtre). Pour positionner la fenêtre sans dépendre du titre de la page (qui change) : cocher `PlaceWindow` (placement porté par l'étape elle-même : écran, Multitâche/Zone ou X/Y/W/H), et/ou cocher `OnLastWindow` sur les étapes suivantes (Placer, Action fenêtre, Clic) pour cibler la fenêtre que cette étape vient d'ouvrir.
  - **+ Scène OBS Studio** : passe OBS sur une scène (serveur WebSocket d'OBS 28+ : *Outils > Réglages du serveur WebSocket*). Champs `ObsHost` (défaut `localhost:4455`), `ObsPassword`, `ObsProfile`, `ObsCollection`, `ObsScene` (listes lues dans OBS quand il tourne ; appliqués dans cet ordre, chacun facultatif). Réessaie tant qu'OBS démarre.
  - **+ Icône du tray** : clique le bouton puis clique toi-même l'icône de l'application dans la zone de notification (ouvre d'abord les icônes cachées si besoin) : l'étape est enregistrée (nom de l'icône, clic gauche ou droit). À l'exécution, elle reclique cette icône pour faire apparaître l'application. `TrayName` est un fragment de l'info-bulle ; **Icônes du tray…** (ou `StreamLauncher.exe --list-tray`) liste les noms. Selon l'appli, régler `Double` (double-clic).
  - **Enregistrer mes clics** (menu *Ajouter une étape*) : clique normalement dans les apps, chaque clic devient une étape `Click` (élément UI Automation par nom + coordonnées relatives, délais réels conservés).
- **Glisser-déposer** : une étape se déplace à la souris dans la liste (ligne d'insertion). **Étapes en rouge** : exécutable d'une étape `Lancer` introuvable (vérifié au démarrage) ; l'étape et celles qui visent le même processus sont ignorées au lancement. Un profil ne s'exécute qu'une fois à la fois (touche Stream Deck pressée deux fois : 2e appel ignoré).
- **Multitâche** (écran scindé) : sur une étape `Place`, champ `Multitâche` (`Zone` dans le JSON) : `Gauche/Droite`, `Haut/Bas`, quarts (`HautGauche`…), tiers, ou `Grille` (écran découpé en `Cols` x `Rows`, fenêtre sur la case `Col`,`Row` avec `SpanCols`/`SpanRows`). Calculé sur la zone de travail de l'écran choisi, bordures invisibles de Windows compensées. `Manuel` garde X/Y/W/H.
- **Clic droit sur une étape** : *Tester cette étape* ou *Tester à partir de cette étape* (exécution réelle, la fenêtre se réduit pendant le test).
- **Recherche de fenêtre** : le processus et la classe de fenêtre (`WindowClass`, capturée automatiquement) sont obligatoires, le titre n'est qu'une préférence : si aucune fenêtre ne contient `Title` (titre modifié par l'appli), la fenêtre du bon processus/classe est utilisée après un court délai. `TitleStrict` rend le titre obligatoire (utile quand plusieurs fenêtres du même processus coexistent, ex. navigateur).
- **Titre capturé** : si une partie du titre est le nom du processus (« Discussion en cours - Discord » → `Discord`), seule cette partie stable est enregistrée ; sinon le titre complet.
- Chaque étape se règle dans le panneau de propriétés (mode `Element` ou `Coords` par étape, timeouts, titre/processus cible…).
- Icône de zone de notification : clic droit → lancer un profil. Fermer la fenêtre réduit dans le tray.

## Aide

Menu **Aide** (ou F1) : guide complet avec une rubrique par fonction (étapes, Multitâche, écrans, navigateur, tray, Stream Deck, dépannage).

## Stream Deck

L'action « Ouvrir » (Système) de Stream Deck n'a pas de champ d'arguments : l'app crée automatiquement, dans le dossier `Profils` de son dossier d'installation, un sous-dossier par profil avec trois raccourcis (`bascule`, `lancer`, `fermer`) à choisir dans l'action « Ouvrir » (repli : `Documents\Stream Launcher\Profils`). Ils suivent la création, le renommage et la suppression des profils. **Outils > Ouvrir le dossier des raccourcis Stream Deck** ouvre le dossier du profil sélectionné. Dans Stream Deck, choisir les fichiers **`.exe`** du dossier du profil (`nom - bascule.exe`…) : ce sont de minuscules lanceurs (4 Ko, source dans `tools/stub`) qui démarrent `StreamLauncher.exe` et dont le nom porte l'action, car Stream Deck ignore les arguments d'un `.lnk` et refuse les `.cmd`. Les lancements en ligne de commande sont tracés dans `%LOCALAPPDATA%\StreamLauncher\logs\launcher.log` (**Outils > Journal des lancements Stream Deck**). Ligne de commande équivalente :

```
StreamLauncher.exe --profile "Nom du profil"
```

C'est une **bascule** : le 1er appel lance le profil, le suivant ferme toutes les fenêtres ouvertes depuis (même touche Stream Deck pour lancer et fermer le live). `--start "Nom"` / `--stop "Nom"` forcent un sens. Exécution sans fenêtre, code retour 0 = OK, 1 = étapes en erreur, 2 = profil inconnu. `--tray` démarre l'interface masquée. **Paramètres > Démarrer avec Windows** lance l'application à l'ouverture de session, directement dans la zone de notification (désactivé par défaut).

## Installation

Au lancement, l'assistant MSI cherche la dernière version stable publiée (`releases/latest`, branche `main`) et, si elle est plus récente que le MSI utilisé, propose de la télécharger (action personnalisée `installer\CheckLatest.js`, ignorée sans réseau ou en installation silencieuse). Il affiche ensuite le CLUF, puis propose le dossier de destination (défaut : `C:\Program Files (x86)\Stream Launcher`). Les mises à jour automatiques réinstallent dans le dossier choisi la première fois.

## Build

```
powershell -File tools\Build-Release.ps1
```

Produit `dist\StreamLauncher-Va.b.c-win-x64.msi` signé (certificat global TomLine). Les identifiants de l'application Kick sont lus dans le dépôt privé `TomLine-signing-keys` (`kick/Stream-Launcher-Kick.md`) et injectés pendant le build (`KickSecrets.g.cs`, ignoré par git, supprimé après) : ils ne sont jamais commités. Un build local (`dotnet build`) n'a pas ces identifiants : Kick y est indisponible. La compilation du lanceur Stream Deck (`assets\LaunchStub.exe`) se refait avec `tools\Build-Stub.ps1`. Le CLUF de l'installateur est `installer\CLUF.txt`. Avec `-Publish`, seul le MSI est joint à la release ; la mise à jour automatique le télécharge, vérifie la signature et l'installe (invite UAC).

## Limites connues

- App lancée en administrateur : lancer Stream Launcher en administrateur aussi (UIPI).
- Apps UWP/Store : le chemin importé est celui d'`ApplicationFrameHost`, à corriger à la main.
- Titres de fenêtre dynamiques (navigateur, documents) : réduire `Title` à un fragment stable.
- Éléments dans des menus contextuels (fenêtres popup) : préférer `Coords`.
- Kick : le secret de l'application est embarqué dans l'exécutable (il n'est pas dans le dépôt) ; le port local 43117 doit être libre pendant la connexion. Twitch et Kick : une connexion inutilisée trop longtemps peut expirer, reconnecter le compte.

## Soutenir le projet

Stream Launcher est gratuit. Si il vous est utile, vous pouvez soutenir son développement :

<a href="https://buymeacoffee.com/slide598b" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" height="50"></a>

## Chat unifié

Bouton **Chat** (barre du profil), étape de profil **Ouvrir le chat unifié** ou `StreamLauncher.exe --chat` : fenêtre unique avec le chat de Twitch, Kick et YouTube (logo de plateforme, badges Twitch). Les logos en haut allument/éteignent chaque plateforme ; un message écrit dans la fenêtre part sur toutes les plateformes allumées. Son à l'arrivée d'un message après un silence (délai réglable, fichier .wav au choix, pseudos exclus) et choix par plateforme de ce qui s'affiche en plus du texte (points de chaîne, bits, abonnements, raids, GIF, Super Chat…). Twitch : IRC (portées `chat:read chat:edit`) ; Kick : lecture par les webhooks officiels de Kick (droit `events:subscribe`) relayés par un petit Worker Cloudflare (dossier `relay/`), envoi par l'API officielle (`chat:write`) ; YouTube : API `liveChat` lue toutes les 10 s au minimum. Clic droit sur un pseudo : copier le pseudo, supprimer le message, exclure temporairement (1 min à 2 semaines), bannir, débannir, nommer ou retirer modérateur et VIP (Twitch : droits `moderator:manage:*` ; Kick : `moderation:ban` et `moderation:chat_message:manage`, à activer sur l'application Kick ; YouTube : pas de débannissement ni de VIP par l'API ; Kick : pas de modérateur ni de VIP dans l'API). Le son par défaut est généré par `tools/Make-ChatSound.ps1`.

## Twitch et Kick : titre, jeu, tags

Bouton **Infos du live** (barre du profil) : mêmes titre, jeu et tags envoyés à Twitch, Kick et YouTube.

- **Connexion sans configuration** : les versions publiées embarquent les applications Twitch et Kick de Stream Launcher. Twitch : flux « device code » (identifiant client public, code à saisir sur twitch.tv). Kick : OAuth 2.1 PKCE avec retour sur `http://localhost:43117/callback` (port à garder libre), droits `channel:read` et `channel:write`.
- **YouTube** : Google OAuth « application de bureau » (PKCE, retour sur `127.0.0.1`), YouTube Data API v3. L'application modifie la diffusion en cours, sinon la prochaine programmée dans YouTube Studio (titre via `liveBroadcasts.update`, tags et catégorie via `videos.update`). Pas de champ « jeu » sur YouTube : le jeu devient la catégorie « Jeux vidéo » et un tag. Application Google en mode test : comptes de test seulement, connexion valable 7 jours.
- **Jetons chiffrés** (DPAPI, compte Windows courant) dans `%LOCALAPPDATA%\StreamLauncher\streaming-auth.bin`. Ils ne sont transmis qu'à Twitch et à Kick.
- **Envoi par plateforme** : un interrupteur à côté de chaque compte connecté choisit si les mises à jour lui sont envoyées (choix mémorisé) ; le bouton principal nomme les plateformes visées.
- **Voyants de connexion** en haut à droite : vert (opérationnelle), orange (vérification), rouge (erreur, détail au survol), gris (non connecté). Vérifiés au démarrage, toutes les 5 minutes et à chaque envoi.
- **Favoris** (combos titre + jeu + tags) créés, modifiables et appliqués en 1 clic, **historique** des 15 derniers lives (`streaming.json`), **dictionnaire des jeux** mis à jour dynamiquement (`games.json`) : relu à chaque lancement de l'application (catégories des plateformes connectées), complété à chaque recherche.
- Étape **Scène OBS Studio** : profil, collection de scènes puis scène (serveur WebSocket d'OBS 28+).

## Canaux de mise à jour et branches

- **main** : versions stables (releases GitHub `Va.b.c`). Canal par défaut.
- **dev** : versions de test, publiées en pré-release GitHub (tag `Va.b.c-dev`) depuis la branche `dev` avec `tools\Build-Release.ps1 -Publish -Dev`. Le script refuse de publier depuis une autre branche.
- Le choix se fait dans **Paramètres > Canal de mise à jour** (enregistré dans `profiles.json`). Revenir de dev à stable ne rétrograde pas : l'application attend la prochaine version stable plus récente.
