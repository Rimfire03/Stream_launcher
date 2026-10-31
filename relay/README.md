# Relais du chat Kick

Kick n'envoie les événements de chat que par webhook (URL publique). Ce Worker Cloudflare les reçoit, vérifie la signature RSA de Kick, puis les renvoie en WebSocket à l'application (une salle par chaîne, rien n'est enregistré).

- `POST /webhook` : appelé par Kick (adresse à saisir dans l'application Kick, portail développeur, « Webhook URL »).
- `GET /ws/<broadcaster_user_id>` : WebSocket écouté par `KickChatSource` (adresse dans `Chat.cs`, variable `STREAMLAUNCHER_KICK_RELAY` pour les tests).

Déploiement : `npx wrangler login` puis `npx wrangler deploy` dans ce dossier. Adresse actuelle : `https://streamlauncher-kick-relay.slide598.workers.dev`.

Si le relais est injoignable ou si la connexion se perd (aucun signe de vie pendant 80 s), l'application affiche un message dans le chat une seule fois, passe le voyant Kick en rouge (« Erreur : relais du chat Kick injoignable… »), réessaie toute seule avec un délai croissant (3 s jusqu'à 60 s), puis écrit « lecture du chat rétablie » au retour.
