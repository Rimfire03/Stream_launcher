# Stream Launcher

Application Windows (.NET 8, self-contained) distribuée uniquement par installateur MSI signé (release GitHub) : lance des applications, les place sur le bon écran et clique dans leurs fenêtres. Profils stockés dans `profiles.json` à côté de l'exe.

## Utilisation

- Créer un profil, puis ajouter des étapes :
  - **Importer fenêtres ouvertes** : ouvre tes apps, place-les à la main, importe → étapes `Launch` + `Place` (écran + position + taille).
  - **+ Application** : étape de lancement manuelle. Ou glisser-déposer des raccourcis (.lnk), exe ou documents dans la fenêtre (le raccourci est résolu vers sa cible).
  - **+ Déplacer fenêtre** : clique le bouton, puis déplace (ou redimensionne) toi-même la fenêtre voulue : sa nouvelle position est enregistrée. **+ Réduire fenêtre** : clique le bouton, puis clique sur la fenêtre à réduire (le clic est capturé, pas transmis). Annulation par la barre affichée en haut de l'écran.
  - **+ Navigateur** : ouvre une URL http(s) dans une **nouvelle fenêtre** du navigateur choisi (`Browser` : Chrome, Edge, Firefox, Brave, Opera, Vivaldi, ou `Defaut` pour le navigateur par défaut de Windows, sans garantie de nouvelle fenêtre). Pour positionner la fenêtre sans dépendre du titre de la page (qui change) : cocher `PlaceWindow` (placement porté par l'étape elle-même : écran, Multitâche/Zone ou X/Y/W/H), et/ou cocher `OnLastWindow` sur les étapes suivantes (Placer, Action fenêtre, Clic) pour cibler la fenêtre que cette étape vient d'ouvrir.
  - **+ Icône du tray** : clique le bouton puis clique toi-même l'icône de l'application dans la zone de notification (ouvre d'abord les icônes cachées si besoin) : l'étape est enregistrée (nom de l'icône, clic gauche ou droit). À l'exécution, elle reclique cette icône pour faire apparaître l'application. `TrayName` est un fragment de l'info-bulle ; **Icônes du tray…** (ou `StreamLauncher.exe --list-tray`) liste les noms. Selon l'appli, régler `Double` (double-clic).
  - **● Enregistrer les clics** : clique normalement dans les apps, chaque clic devient une étape `Click` (élément UI Automation par nom + coordonnées relatives, délais réels conservés).
- **Multitâche** (écran scindé) : sur une étape `Place`, champ `Multitâche` (`Zone` dans le JSON) : `Gauche/Droite`, `Haut/Bas`, quarts (`HautGauche`…), tiers, ou `Grille` (écran découpé en `Cols` x `Rows`, fenêtre sur la case `Col`,`Row` avec `SpanCols`/`SpanRows`). Calculé sur la zone de travail de l'écran choisi, bordures invisibles de Windows compensées. `Manuel` garde X/Y/W/H.
- **Clic droit sur une étape** : *Tester cette étape* ou *Tester à partir de cette étape* (exécution réelle, la fenêtre se réduit pendant le test).
- **Recherche de fenêtre** : le processus et la classe de fenêtre (`WindowClass`, capturée automatiquement) sont obligatoires, le titre n'est qu'une préférence : si aucune fenêtre ne contient `Title` (titre modifié par l'appli), la fenêtre du bon processus/classe est utilisée après un court délai. `TitleStrict` rend le titre obligatoire (utile quand plusieurs fenêtres du même processus coexistent, ex. navigateur).
- **Titre capturé** : si une partie du titre est le nom du processus (« Discussion en cours - Discord » → `Discord`), seule cette partie stable est enregistrée ; sinon le titre complet.
- Chaque étape se règle dans le panneau de propriétés (mode `Element` ou `Coords` par étape, timeouts, titre/processus cible…).
- Icône de zone de notification : clic droit → lancer un profil. Fermer la fenêtre réduit dans le tray.

## Aide

Menu **Aide** (ou F1) : guide complet avec une rubrique par fonction (étapes, Multitâche, écrans, navigateur, tray, Stream Deck, dépannage).

## Stream Deck

Action « Ouvrir » (Système) :

```
StreamLauncher.exe --profile "Nom du profil"
```

C'est une **bascule** : le 1er appel lance le profil, le suivant ferme toutes les fenêtres ouvertes depuis (même touche Stream Deck pour lancer et fermer le live). `--start "Nom"` / `--stop "Nom"` forcent un sens. Exécution sans fenêtre, code retour 0 = OK, 1 = étapes en erreur, 2 = profil inconnu. `--tray` démarre l'interface masquée.

## Installation

L'assistant MSI propose le dossier de destination (défaut : `C:\Program Files (x86)\Stream Launcher`). Les mises à jour automatiques réinstallent dans le dossier choisi la première fois.

## Build

```
powershell -File tools\Build-Release.ps1
```

Produit `dist\StreamLauncher-Va.b.c-win-x64.msi` signé (certificat global TomLine). Avec `-Publish`, seul le MSI est joint à la release ; la mise à jour automatique le télécharge, vérifie la signature et l'installe (invite UAC).

## Limites connues

- App lancée en administrateur : lancer Stream Launcher en administrateur aussi (UIPI).
- Apps UWP/Store : le chemin importé est celui d'`ApplicationFrameHost`, à corriger à la main.
- Titres de fenêtre dynamiques (navigateur, documents) : réduire `Title` à un fragment stable.
- Éléments dans des menus contextuels (fenêtres popup) : préférer `Coords`.
