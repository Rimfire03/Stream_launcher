/// <summary>Contenu de l'aide intégrée (menu Aide, touche F1).</summary>
static class HelpTopics
{
    public record Topic(string Title, string Text);

    public static readonly Topic[] All =
    {
        new("Démarrage rapide", @"Stream Launcher prépare le lancement d'un live : il ouvre tes applications, les place sur le bon écran et clique dans leurs fenêtres.

1. Crée un profil (bouton « Nouveau », à gauche), par exemple « Live Twitch ».
2. Ajoute des étapes dans l'ordre voulu :
   - ouvre tes applications et place tes fenêtres comme tu les veux, puis « Importer fenêtres ouvertes… » ;
   - ou glisse-dépose des raccourcis, exe ou documents dans la fenêtre ;
   - ou utilise les boutons « + Application… », « + Navigateur », « + Icône du tray »…
3. Pour les clics, utilise « ● Enregistrer les clics » et fais les clics toi-même.
4. Teste : clic droit sur une étape > « Tester cette étape », ou « ▶ Lancer » pour tout le profil.
5. Pour lancer d'un clic : bouton « ▶ nom du profil » en haut de la fenêtre, icône de la zone de notification (clic droit), ou Stream Deck (voir la rubrique dédiée).

Tout est enregistré automatiquement dans profiles.json, à côté de l'exe."),

        new("Profils et étapes", @"Un profil est une liste d'étapes exécutées dans l'ordre, de haut en bas.

Zone de gauche : les profils (Nouveau, Renommer, Supprimer).
Zone du milieu : les étapes du profil sélectionné.
Zone de droite : les propriétés de l'étape sélectionnée (tout se règle là).

Boutons principaux :
- ▶ Lancer le live / ■ Fermer le live : exécute le profil, puis ferme ses fenêtres (bascule).
- ■ Stop : interrompt une exécution en cours.
- ● Enregistrer les clics : transforme tes clics en étapes.
- ＋ Ajouter une étape ▾ : menu par thèmes (Applications, Fenêtres, Interactions) : application ou fichier, import des fenêtres ouvertes, site web, placer / déplacer / réduire une fenêtre, clic, icône de la zone de notification.
- Outils ▾ : identifier ou renommer les écrans, lister les icônes de la zone de notification, mises à jour.
- ↑ ↓ Dupliquer Supprimer : réorganisent la liste (même chose par clic droit sur une étape).

Chaque étape de la liste porte une pastille de couleur (LANCER, PLACER, CLIC, FENÊTRE, TRAY, WEB), un résumé en clair et son temps d'attente. Le panneau de droite n'affiche que les réglages utiles au type d'étape choisi.

Réglages communs à toutes les étapes :
- DelayMs : attente avant l'étape (en millisecondes).
- TimeoutMs : temps maximum pour trouver la fenêtre ou l'élément. Passé ce délai l'étape est en erreur, le profil continue avec l'étape suivante."),

        new("Étape Lancer (applications)", @"Lance une application, un document ou une URL.

Propriétés (catégorie « Lancement ») :
- Exe : chemin de l'exécutable, du document ou de l'URL.
- Args : arguments de ligne de commande (par exemple un argument « démarrer réduit dans le tray » si l'application en propose un).
- SkipIfRunning : si vrai (par défaut), l'application n'est pas relancée quand un processus du même nom tourne déjà.

Ajouter facilement :
- glisse-dépose un raccourci (.lnk), un exe ou un document dans la fenêtre : un raccourci est résolu vers sa cible ;
- « + Application… » ouvre un sélecteur de fichier ;
- « Importer fenêtres ouvertes… » récupère le chemin des applications déjà ouvertes.

Limite : les applications du Microsoft Store ont un chemin d'importation qui n'est pas leur exe ; corrige-le à la main ou utilise leur raccourci."),

        new("Étape Placer (écrans et Multitâche)", @"Place la fenêtre d'une application sur un écran, à une position et une taille données.

Propriétés (catégorie « Placement ») :
- Monitor : l'écran (voir « Écrans »).
- Multitâche : comment définir la zone.
   Manuel : positions X, Y, W (largeur), H (hauteur) relatives à l'écran. Maximize agrandit la fenêtre.
   Gauche / Droite / Haut / Bas : moitiés d'écran.
   HautGauche, HautDroite, BasGauche, BasDroite : quarts.
   TiersGauche, TiersCentre, TiersDroit : tiers.
   Grille : écran découpé en Cols x Rows ; la fenêtre occupe la case Col, Row (la première est 0) sur SpanCols x SpanRows cases.
- Les zones sont calculées sur la zone de travail de l'écran (la barre des tâches est exclue). Les bordures invisibles de Windows sont compensées : deux fenêtres voisines se touchent sans trou ni chevauchement.

Comment retrouver la fenêtre : voir « Retrouver une fenêtre »."),

        new("Étape Clic", @"Clique dans une fenêtre déjà ouverte.

Deux modes (propriété Mode) :
- Element : cherche le bouton ou le contrôle par son nom (ElementName) ou son identifiant (ElementId) avec UI Automation, puis clique dessus. Robuste si la fenêtre bouge ou change de taille. Ne marche pas avec les applications qui n'exposent pas leurs contrôles.
- Coords : clique à la position RelX, RelY mesurée depuis le coin haut-gauche de la fenêtre. Marche partout, mais dépend de la taille de la fenêtre.

Autres propriétés : Button (gauche ou droit), Double (double-clic).

Enregistrement : « ● Enregistrer les clics » réduit Stream Launcher et affiche une barre « Arrêter » en haut de l'écran. Chaque clic que tu fais devient une étape (nom de l'élément quand il existe, sinon coordonnées), avec l'attente réelle entre les clics. Deux clics rapprochés au même endroit deviennent un double-clic.

Limites : les éléments de menus contextuels sont mal retrouvés par nom, préfère Coords pour ceux-là."),

        new("Étape Action fenêtre", @"Agit sur une fenêtre existante (catégorie « Action fenêtre »).

Actions : Reduire, Agrandir, Restaurer, Deplacer.
- Deplacer applique le placement de l'étape (Monitor, Multitâche, X/Y/W/H) à la fenêtre cible.

Ajouter avec capture :
- « + Déplacer fenêtre » : une barre s'affiche ; déplace (ou redimensionne) toi-même la fenêtre voulue, sa nouvelle position est enregistrée.
- « + Réduire fenêtre » : clique sur la fenêtre à réduire (ce clic est capturé, pas transmis).
Le bouton « Annuler » de la barre annule la capture.

La fenêtre cible est cherchée comme pour l'étape Placer (voir « Retrouver une fenêtre »)."),

        new("Étape Navigateur", @"Ouvre une URL dans une nouvelle fenêtre du navigateur de ton choix.

Propriétés (catégorie « Navigateur ») :
- Browser : Chrome, Edge, Firefox, Brave, Opera, Vivaldi, ou Defaut (navigateur par défaut de Windows, sans garantie de nouvelle fenêtre).
- Url : adresse http:// ou https:// uniquement.
- PlaceWindow : place la nouvelle fenêtre avec le placement de l'étape (Monitor, Multitâche, X/Y/W/H).

Le titre des pages web change tout le temps. Pour ne pas en dépendre :
- coche PlaceWindow pour placer directement la fenêtre ouverte par l'étape ;
- ou coche OnLastWindow (catégorie « Fenêtre cible ») sur les étapes suivantes (Placer, Action fenêtre, Clic) : elles ciblent la fenêtre que l'étape Navigateur vient d'ouvrir.
Ces options ne fonctionnent pas avec Defaut."),

        new("Étape Icône du tray", @"Fait apparaître une application qui tourne dans la zone de notification (près de l'horloge) en cliquant son icône. Les icônes cachées (menu « ^ ») sont gérées.

Enregistrer : clique « + Icône du tray », puis clique toi-même l'icône de l'application dans la zone de notification. Si l'icône est cachée, ouvre d'abord le menu des icônes cachées (ce clic est ignoré). Ton clic passe aussi à l'application. L'étape garde le nom de l'icône et le bouton utilisé (gauche ou droit).

Propriétés :
- TrayName : fragment du nom (info-bulle) de l'icône.
- Button, Double : clic droit ou double-clic selon l'application (voir catégorie « Clic »).

« Icônes du tray… » écrit dans le journal la liste des noms disponibles (même chose en ligne de commande : StreamLauncher.exe --list-tray)."),

        new("Retrouver une fenêtre", @"Les étapes Placer, Action fenêtre et Clic doivent retrouver la bonne fenêtre. Critères (catégorie « Fenêtre cible ») :

- Process : nom du processus sans .exe (obligatoire s'il est renseigné).
- WindowClass : classe de fenêtre Windows, stable même quand le titre change (capturée automatiquement ; obligatoire si renseignée).
- Title : fragment du titre, utilisé comme préférence.

Les applications changent souvent leur titre (nom du document, messages non lus, « Discussion en cours - Discord »…). Donc :
- si une fenêtre contient Title, c'est elle ;
- sinon, après un court délai (au plus 5 secondes), la fenêtre du bon processus et de la bonne classe qui est au premier plan est utilisée (le journal le signale) ;
- TitleStrict = vrai rend le titre obligatoire : utile quand plusieurs fenêtres du même processus coexistent.
- OnLastWindow = vrai : cible la fenêtre ouverte par la dernière étape Navigateur.

À la capture, si une partie du titre est le nom du processus, seule cette partie est gardée (« Discussion en cours - Discord » devient Discord)."),

        new("Écrans", @"Les écrans sont désignés par leur nom Windows (\\.\DISPLAY1, \\.\DISPLAY2…).

- « Identifier les écrans » affiche un grand numéro sur chaque écran pendant 5 secondes (clic pour fermer), avec son nom et sa résolution.
- « Renommer les écrans… » donne un nom parlant à chacun (par exemple « Stream », « Jeu »). Le champ Monitor affiche alors « Stream (\\.\DISPLAY2) ».

Les profils retiennent le nom Windows de l'écran : renommer ne casse rien. Dans le fichier profiles.json, tu peux aussi écrire le nom personnalisé. Si un écran n'existe plus, l'écran principal est utilisé."),

        new("Fermer le live (bouton bascule)", @"Le bouton de lancement d'un profil est une bascule :
- 1er appui (« ▶ Lancer » ou « ▶ nom du profil ») : le profil s'exécute et Stream Launcher retient les fenêtres déjà ouvertes à ce moment.
- Le bouton devient « ■ Fermer le live ». 2e appui : toutes les fenêtres ouvertes depuis le lancement sont fermées proprement (demande de fermeture : une application qui a un travail non enregistré affiche sa question habituelle). Les fenêtres que le profil a placées ou cliquées sont fermées aussi.
- Les fenêtres qui étaient déjà ouvertes avant le lancement et que le profil n'a pas touchées ne sont pas fermées.

Ça marche partout : bouton du profil, bouton « ▶ nom » en haut de la fenêtre, menu de l'icône de la zone de notification, et Stream Deck (même touche pour lancer puis fermer). L'état est mémorisé sur disque : on peut lancer depuis la fenêtre et fermer depuis Stream Deck.

Précisions :
- Une application qui reste dans la zone de notification quand on ferme sa fenêtre (Discord, par exemple) continue de tourner ; seule sa fenêtre est fermée.
- Un « Tester une étape » ne démarre pas de live.
- --start et --stop (ligne de commande) forcent le lancement ou la fermeture au lieu de basculer."),

        new("Tester une étape", @"Clic droit sur une étape dans la liste :
- « Tester cette étape » : exécute uniquement cette étape ;
- « Tester à partir de cette étape » : exécute cette étape et toutes les suivantes.

Le test est réel (les applications sont vraiment lancées, les clics vraiment faits). La fenêtre de Stream Launcher se réduit pendant le test pour ne pas gêner. ■ Stop ou « Arrêter l'exécution » dans le menu de l'icône de la zone de notification interrompt l'exécution. Le journal en bas de la fenêtre détaille chaque étape et les erreurs."),

        new("Stream Deck et ligne de commande", @"Lancer un profil sans ouvrir la fenêtre :

StreamLauncher.exe --profile ""Nom du profil""

C'est une bascule : le premier appel lance le profil, le suivant ferme les fenêtres ouvertes (voir « Fermer le live »). Les arguments sont portés par les raccourcis générés (voir plus bas).

Dans Stream Deck, l'action « Système > Ouvrir » n'a pas de champ pour les arguments. Utilise les raccourcis générés par l'application :
1. Les raccourcis sont créés automatiquement : dans le dossier « Profils » de l'application (par défaut C:\Program Files (x86)\Stream Launcher\Profils), un sous-dossier par profil contient « nom - bascule », « nom - lancer » et « nom - fermer » (en .exe pour Stream Deck, en .lnk pour un usage manuel). Ils sont mis à jour quand tu crées, renommes ou supprimes un profil, et à chaque démarrage. « Outils > Ouvrir le dossier des raccourcis Stream Deck » ouvre le dossier du profil sélectionné.
2. Dans Stream Deck, glisse « Système > Ouvrir » sur une touche et choisis le raccourci voulu (« bascule » = même touche pour lancer puis fermer).
3. Choisis le fichier .exe du dossier du profil (« nom - bascule.exe »), pas le .lnk : l'action « Ouvrir » de Stream Deck ignore les arguments d'un raccourci et refuse les .cmd. Ces .exe sont de minuscules lanceurs (4 Ko) qui démarrent StreamLauncher.exe : c'est leur nom qui indique l'action. Les .lnk servent à lancer à la main. Lancer StreamLauncher.exe sans argument ne fait que réafficher la fenêtre.
4. « Outils > Journal des lancements Stream Deck » ouvre le journal : chaque lancement en ligne de commande y est tracé (arguments reçus, étapes, erreurs).

Code de retour : 0 = tout est passé, 1 = au moins une étape en erreur, 2 = profil inconnu (la fermeture renvoie 0).

Autres options :
- --start ""Nom"" / --stop ""Nom"" : force le lancement ou la fermeture.
- --tray : démarre l'interface masquée dans la zone de notification. « Outils > Démarrer avec Windows » l'ajoute au démarrage de la session avec cette option.
- --list-tray : écrit sur la sortie standard les noms des icônes de la zone de notification.

Au quotidien, l'icône de la zone de notification (clic droit) permet aussi de lancer chaque profil."),

        new("Installation et mises à jour", @"Installation : l'assistant MSI propose le dossier de destination (par défaut C:\Program Files (x86)\Stream Launcher) et les icônes du menu Démarrer et du bureau. Le dossier d'installation reste inscriptible : profiles.json y est enregistré.

Mises à jour : au démarrage, et via le bouton « Mises à jour » ou le menu Aide, l'application compare sa version (format Va.b.c, par exemple V0.1.10) à la dernière release GitHub. Si une version plus récente existe, elle télécharge l'installateur, vérifie qu'il est signé par TomLine prod&co, l'installe (Windows demande une validation administrateur) puis redémarre l'application.

Fermer la fenêtre réduit l'application dans la zone de notification ; « Quitter » est dans le menu de son icône."),

        new("Dépannage", @"- Une application lancée en administrateur ne reçoit pas les clics de Stream Launcher lancé normalement : lance aussi Stream Launcher en administrateur.
- Un clic « Element » échoue : l'application n'expose pas ce contrôle ou son nom a changé. Utilise le mode Coords, ou corrige ElementName.
- La fenêtre n'est pas retrouvée : vérifie Process, WindowClass, Title ; augmente TimeoutMs si l'application est lente à s'ouvrir ; regarde le journal.
- Mauvaise fenêtre déplacée : plusieurs fenêtres du même processus ; coche TitleStrict ou utilise OnLastWindow pour un navigateur.
- Position décalée après un changement d'écrans : relance « Identifier les écrans » et vérifie le champ Monitor.
- Fichier de profils illisible : il est sauvegardé en profiles.json.bak et un fichier neuf est créé.
- Un test déplace tes fenêtres de travail : utilise « Tester cette étape » plutôt que le profil complet."),
    };
}

/// <summary>Fenêtre d'aide : rubriques à gauche, texte à droite.</summary>
class HelpDialog : Form
{
    public HelpDialog(int topic)
    {
        Text = "Aide - Stream Launcher";
        Size = new Size(960, 640);
        StartPosition = FormStartPosition.CenterParent;
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10f), BackColor = SystemColors.Window };
        foreach (var t in HelpTopics.All) list.Items.Add(t.Title);
        list.SelectedIndexChanged += (_, _) => { if (list.SelectedIndex >= 0) text.Text = HelpTopics.All[list.SelectedIndex].Text.Replace("\r\n", "\n").Replace("\n", "\r\n"); };
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 260, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(list);
        split.Panel2.Controls.Add(text);
        Controls.Add(split);
        list.SelectedIndex = Math.Clamp(topic, 0, HelpTopics.All.Length - 1);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }
}
