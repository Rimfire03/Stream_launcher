// Kick chat relay: Kick sends its webhooks here (public https), the Stream Launcher app listens on a WebSocket.
// One room (Durable Object) per broadcaster. Nothing is stored: a message is forwarded to the connected sockets and dropped.

const KEY_URL = "https://api.kick.com/public/v1/public-key";
const EVENTS = new Set([
  "chat.message.sent",
  "channel.subscription.new",
  "channel.subscription.renewal",
  "channel.subscription.gifts",
  "channel.reward.redemption.updated",
]);
const MAX_AGE_MS = 10 * 60 * 1000; // older signed messages are replays

let keyCache = null; // Kick can rotate its key at any time: refetched when a signature fails

async function publicKey(force) {
  if (keyCache && !force) return keyCache;
  const pem = await (await fetch(KEY_URL)).json().then((j) => j.data?.public_key ?? "");
  const der = Uint8Array.from(atob(pem.replace(/-----[A-Z ]+-----|\s/g, "")), (c) => c.charCodeAt(0));
  keyCache = await crypto.subtle.importKey("spki", der, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["verify"]);
  return keyCache;
}

async function verified(request, body) {
  const h = request.headers;
  const id = h.get("Kick-Event-Message-Id"), ts = h.get("Kick-Event-Message-Timestamp"), sig = h.get("Kick-Event-Signature");
  if (!id || !ts || !sig) return false;
  if (Math.abs(Date.now() - Date.parse(ts)) > MAX_AGE_MS) return false;
  const data = new TextEncoder().encode(`${id}.${ts}.${body}`);
  const raw = Uint8Array.from(atob(sig), (c) => c.charCodeAt(0));
  for (const force of [false, true]) {
    try {
      if (await crypto.subtle.verify("RSASSA-PKCS1-v1_5", await publicKey(force), raw, data)) return true;
    } catch {}
  }
  return false;
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "POST" && url.pathname === "/webhook") {
      const body = await request.text();
      if (!(await verified(request, body))) return new Response("bad signature", { status: 401 });
      const type = request.headers.get("Kick-Event-Type") ?? "";
      if (!EVENTS.has(type)) return new Response("ignored");
      let room;
      try { room = JSON.parse(body).broadcaster?.user_id; } catch {}
      if (!room) return new Response("no broadcaster", { status: 400 });
      const stub = env.ROOMS.get(env.ROOMS.idFromName(String(room)));
      await stub.fetch("https://room/push", { method: "POST", body: JSON.stringify({ type, data: JSON.parse(body) }) });
      return new Response("ok");
    }

    const m = url.pathname.match(/^\/ws\/(\d{1,12})$/);
    if (m && request.headers.get("Upgrade") === "websocket") {
      return env.ROOMS.get(env.ROOMS.idFromName(m[1])).fetch(request);
    }

    return new Response("Stream Launcher Kick relay");
  },
};

export class Room {
  constructor(state) {
    this.state = state;
    this.state.setWebSocketAutoResponse(new WebSocketRequestResponsePair("ping", "pong"));
  }

  async fetch(request) {
    if (request.method === "POST") {
      const text = await request.text();
      for (const ws of this.state.getWebSockets()) { try { ws.send(text); } catch {} }
      return new Response("ok");
    }
    const pair = new WebSocketPair();
    this.state.acceptWebSocket(pair[1]);
    return new Response(null, { status: 101, webSocket: pair[0] });
  }

  webSocketMessage() {} // the app only listens
}
