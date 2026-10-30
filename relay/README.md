# Relais du chat Kick

Kick n'envoie les événements de chat que par webhook (URL publique). Ce Worker Cloudflare les reçoit, vérifie la signature RSA de Kick, puis les renvoie en WebSocket à l'application (une salle par chaîne, rien n'est enregistré).

- `POST /webhook` : appelé par Kick (adresse à saisir dans l'application Kick, portail développeur, « Webhook URL »).
- `GET /ws/<broadcaster_user_id>` : WebSocket écouté par `KickChatSource` (adresse dans `Chat.cs`, variable `STREAMLAUNCHER_KICK_RELAY` pour les tests).

Déploiement : `npx wrangler login` puis `npx wrangler deploy` dans ce dossier. Adresse actuelle : `https://streamlauncher-kick-relay.slide598.workers.dev`.
