# Politique de confidentialité / Privacy policy

*Stream Launcher, logiciel gratuit de TomLine prod&co (Thomas Guidez), contact : contact@tomlineprodco.fr*

## Français

Stream Launcher s'exécute sur ton PC. **L'éditeur ne collecte, ne reçoit et ne stocke aucune donnée sur toi.**

**Comptes Twitch, Kick et YouTube (fonction facultative).** Si tu connectes un compte, l'application communique directement,
depuis ton PC, avec la plateforme concernée, uniquement pour lire et modifier le titre, le jeu (ou la catégorie) et les tags de ta chaîne
ou de ta diffusion, et, si tu ouvres le chat unifié, pour lire le chat de ta chaîne et y envoyer les messages que tu écris. Les messages du chat s'affichent seulement dans la fenêtre et ne sont pas enregistrés. Pour Kick, les messages de ta chaîne passent par un relais (Worker Cloudflare) qui les transmet à l'application sans les conserver. Les listes d'emotes 7TV, BetterTTV et FrankerFaceZ sont demandées à leurs serveurs (seul l'identifiant de ta chaîne Twitch est envoyé). Les actions de modération (exclusion, bannissement) ne sont envoyées qu'à la plateforme concernée, quand tu les demandes. Pour afficher emotes, badges, GIF et emoji, l'application télécharge ces images depuis les serveurs de Twitch, Kick, Giphy, Tenor et jsDelivr (images Twemoji) ; seule l'adresse de l'image est demandée, jamais le contenu de tes messages. Elle demande les autorisations strictement nécessaires à cela.
- Les jetons d'accès sont enregistrés **chiffrés** sur ton PC (protection Windows, compte utilisateur courant) et ne sont jamais envoyés à l'éditeur ni à un tiers.
- Les titres, jeux, tags, favoris et historique sont enregistrés **localement** (`%LOCALAPPDATA%\StreamLauncher`).
- Tu peux révoquer l'accès à tout moment : bouton « Déconnecter » dans l'application, ou dans les paramètres de sécurité de ton compte
  (Twitch, Kick, Google : https://myaccount.google.com/permissions).
- L'utilisation des informations reçues de l'API YouTube respecte les [Conditions d'utilisation des services d'API YouTube](https://developers.google.com/youtube/terms/api-services-terms-of-service)
  et la [politique de confidentialité de Google](https://policies.google.com/privacy). Stream Launcher n'utilise ces données que pour la fonction décrite ci-dessus et ne les partage avec personne.

### Données utilisateur Google (YouTube)

Si tu connectes ton compte YouTube, l'application demande une seule autorisation Google : `https://www.googleapis.com/auth/youtube.force-ssl` (gérer ton compte YouTube).

**1. Données Google auxquelles l'application accède.**
- Ton compte : l'identifiant et le nom de ta chaîne YouTube.
- Tes diffusions en direct et vidéos : titre, description, tags, catégorie, état (programmée, en cours, terminée) et horaires.
- Le chat de ta diffusion en cours : messages, pseudos des auteurs, Super Chats, abonnements (membres), et la liste de tes modérateurs.

**2. Comment l'application utilise ces données.**
- Les titres, tags et catégories servent à afficher et à modifier les informations de ta diffusion, et à garder un historique de tes lives.
- Les messages du chat servent uniquement à les afficher dans la fenêtre du chat unifié. À ta demande, l'application envoie un message au chat, supprime un message, exclut ou bannit un spectateur, ou ajoute/retire un modérateur.
- Aucune autre utilisation : pas de publicité, pas de profilage, pas d'analyse, pas d'entraînement de modèle d'IA, pas de revente.

**3. Partage avec des tiers.** L'application ne partage, ne transfère ni ne divulgue **aucune** donnée utilisateur Google à un tiers, ni à l'éditeur. Les échanges se font directement entre ton PC et les serveurs de Google (API YouTube). Aucun serveur de l'éditeur ne reçoit ces données. Seule exception : les obligations légales, qui ne concernent pas l'éditeur, puisqu'il ne détient aucune de ces données.

**4. Protection des données.**
- Les jetons d'accès et d'actualisation sont enregistrés **chiffrés** sur ton PC (protection Windows DPAPI, liée à ton compte utilisateur) et ne quittent jamais ton ordinateur.
- Toutes les communications avec Google passent par HTTPS (TLS).
- Les titres, tags et l'historique sont enregistrés uniquement en local (`%LOCALAPPDATA%\StreamLauncher`). Les messages du chat ne sont pas enregistrés : ils restent en mémoire pendant que la fenêtre est ouverte.
- Aucune personne de l'éditeur n'a accès à ces données.

**5. Conservation et suppression.**
- Les jetons sont conservés tant que le compte est connecté. Le bouton « Déconnecter » les efface immédiatement de ton PC.
- Les titres, tags et l'historique restent en local jusqu'à ce que tu les supprimes dans l'application ou que tu effaces le dossier `%LOCALAPPDATA%\StreamLauncher` (la désinstallation de l'application peut aussi s'en charger).
- Les messages du chat sont perdus à la fermeture de la fenêtre.
- Tu peux révoquer l'accès à tout moment sur https://myaccount.google.com/permissions : les jetons deviennent alors inutilisables.
- Pour toute demande de suppression : contact@tomlineprodco.fr. L'éditeur ne détenant aucune de ces données, la suppression se fait sur ton PC.

L'utilisation et le transfert par Stream Launcher des informations reçues des API Google vers toute autre application respectent la [politique relative aux données utilisateur des services d'API Google](https://developers.google.com/terms/api-services-user-data-policy), y compris les exigences d'utilisation limitée (*Limited Use*).

**Mises à jour.** L'application interroge GitHub (`api.github.com`) pour savoir si une version plus récente existe ; GitHub peut traiter ton adresse IP selon sa propre politique.

**Droits.** Aucune donnée personnelle n'étant détenue par l'éditeur, il n'y a rien à consulter ou à effacer chez lui. Pour toute question : contact@tomlineprodco.fr.

## English

Stream Launcher runs on your PC. **The publisher does not collect, receive or store any data about you.**

**Twitch, Kick and YouTube accounts (optional feature).** If you connect an account, the app talks directly from your PC to that platform, only to read and
update the title, game (or category) and tags of your channel or broadcast and, if you open the unified chat, to read your channel's chat and send the messages you type. Chat messages are only shown in the window and are not stored. To show emotes, badges, GIFs and emoji, the app downloads these images from the servers of Twitch, Kick, Giphy, Tenor and jsDelivr (Twemoji images); only the image address is requested, never the content of your messages. It uses the minimum permissions required.
- Access tokens are stored **encrypted** on your PC (Windows user-scope protection) and are never sent to the publisher or to any third party.
- Titles, games, tags, favourites and history are stored **locally** (`%LOCALAPPDATA%\StreamLauncher`).
- You can revoke access at any time: "Disconnect" in the app, or in your account security settings (Google: https://myaccount.google.com/permissions).
- Use of information received from YouTube APIs follows the [YouTube API Services Terms of Service](https://developers.google.com/youtube/terms/api-services-terms-of-service)
  and the [Google Privacy Policy](https://policies.google.com/privacy). Stream Launcher uses this data only for the feature described above and shares it with no one.

### Google user data (YouTube)

If you connect your YouTube account, the app requests a single Google permission: `https://www.googleapis.com/auth/youtube.force-ssl` (manage your YouTube account).

**1. Google user data the app accesses.**
- Your account: the id and name of your YouTube channel.
- Your live broadcasts and videos: title, description, tags, category, status (scheduled, live, completed) and times.
- The chat of your current broadcast: messages, author names, Super Chats, memberships, and the list of your moderators.

**2. How the app uses this data.**
- Titles, tags and categories are used to show and edit your broadcast information and to keep a history of your lives.
- Chat messages are only used to display them in the unified chat window. When you ask for it, the app sends a chat message, deletes a message, times out or bans a viewer, or adds/removes a moderator.
- No other use: no advertising, no profiling, no analytics, no AI model training, no resale.

**3. Sharing with third parties.** The app does **not** share, transfer or disclose any Google user data to any third party, nor to the publisher. Exchanges happen directly between your PC and Google's servers (YouTube API). No publisher server receives this data.

**4. Data protection.**
- Access and refresh tokens are stored **encrypted** on your PC (Windows DPAPI, tied to your user account) and never leave your computer.
- All communication with Google uses HTTPS (TLS).
- Titles, tags and history are stored locally only (`%LOCALAPPDATA%\StreamLauncher`). Chat messages are not stored: they stay in memory while the window is open.
- Nobody at the publisher can access this data.

**5. Retention and deletion.**
- Tokens are kept as long as the account is connected. The "Disconnect" button erases them from your PC immediately.
- Titles, tags and history stay local until you delete them in the app or erase the `%LOCALAPPDATA%\StreamLauncher` folder (uninstalling the app may also do it).
- Chat messages are lost when the window is closed.
- You can revoke access at any time at https://myaccount.google.com/permissions: the tokens then become unusable.
- For any deletion request: contact@tomlineprodco.fr. The publisher holds none of this data, so deletion is done on your PC.

Stream Launcher's use and transfer to any other app of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements.

**Updates.** The app queries GitHub (`api.github.com`) to check for a newer version; GitHub may process your IP address under its own policy.

**Contact:** contact@tomlineprodco.fr
