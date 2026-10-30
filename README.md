# Stream Launcher

Exe Windows portable (.NET 8, self-contained, un seul fichier) : lance des applications, les place sur le bon écran et clique dans leurs fenêtres. Profils stockés dans `profiles.json` à côté de l'exe.

## Utilisation

- Créer un profil, puis ajouter des étapes :
  - **Importer fenêtres ouvertes** : ouvre tes apps, place-les à la main, importe → étapes `Launch` + `Place` (écran + position + taille).
  - **+ Application** : étape de lancement manuelle. Ou glisser-déposer des raccourcis (.lnk), exe ou documents dans la fenêtre (le raccourci est résolu vers sa cible).
  - **● Enregistrer les clics** : clique normalement dans les apps, chaque clic devient une étape `Click` (élément UI Automation par nom + coordonnées relatives, délais réels conservés).
- **Multitâche** (écran scindé) : sur une étape `Place`, champ `Multitâche` (`Zone` dans le JSON) : `Gauche/Droite`, `Haut/Bas`, quarts (`HautGauche`…), tiers, ou `Grille` (écran découpé en `Cols` x `Rows`, fenêtre sur la case `Col`,`Row` avec `SpanCols`/`SpanRows`). Calculé sur la zone de travail de l'écran choisi, bordures invisibles de Windows compensées. `Manuel` garde X/Y/W/H.
- Chaque étape se règle dans le panneau de propriétés (mode `Element` ou `Coords` par étape, timeouts, titre/processus cible…).
- Icône de zone de notification : clic droit → lancer un profil. Fermer la fenêtre réduit dans le tray.

## Stream Deck

Action « Ouvrir » (Système) :

```
StreamLauncher.exe --profile "Nom du profil"
```

Exécution sans fenêtre, code retour 0 = OK, 1 = étapes en erreur, 2 = profil inconnu. `--tray` démarre l'interface masquée.

## Build

```
powershell -File tools\Build-Release.ps1
```

Produit `dist\StreamLauncher.exe` signé (certificat global TomLine).

## Limites connues

- App lancée en administrateur : lancer Stream Launcher en administrateur aussi (UIPI).
- Apps UWP/Store : le chemin importé est celui d'`ApplicationFrameHost`, à corriger à la main.
- Titres de fenêtre dynamiques (navigateur, documents) : réduire `Title` à un fragment stable.
- Éléments dans des menus contextuels (fenêtres popup) : préférer `Coords`.
