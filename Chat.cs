using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Unified chat: one reader per platform feeding the same window, and one sender that writes the streamer's message everywhere.
//  - Twitch  : IRC over WebSocket (scopes chat:read chat:edit).
//  - Kick    : reading through the real-time feed of kick.com (not an official API, may break), sending through the official API (chat:write).
//  - YouTube : liveChatMessages (polled every 10 s at least, to spare the 10 000 daily quota units).

/// <param name="Kind">message, highlight, bits, reward, sub, gift, raid, gif (Twitch / Kick) ; superchat, sticker, member, gift (YouTube) : each kind can be hidden per platform</param>
/// <param name="Extra">short label shown before the text for special messages ("100 bits", "Raid"...)</param>
/// <param name="SourceRoom">Twitch shared chat: id of the channel the message was sent from</param>
/// <param name="Badges">Twitch badges as "set/version" (broadcaster/1, subscriber/12...)</param>
record ChatMessage(DateTime Time, string PlatformId, string Author, string Text, string? Color = null, bool Mine = false, bool System = false,
    string Kind = "message", string? Extra = null, string[]? Badges = null, EmoteSpan[]? Emotes = null, string? SourceRoom = null);

/// <summary>an emote inside the text of a message: UTF-16 position and length of its name, and the image url</summary>
readonly record struct EmoteSpan(int Start, int Length, string Url);

/// <summary>what a platform can show besides plain messages: (kind, label in the settings)</summary>
static class ChatKinds
{
    public static readonly Dictionary<string, (string Kind, string Label)[]> ByPlatform = new()
    {
        ["twitch"] = new[]
        {
            ("badges", "Badges des utilisateurs"), ("emotes", "Emotes (images)"), ("highlight", "Messages mis en avant"), ("bits", "Cheers (bits)"),
            ("reward", "Messages de récompense de points de chaîne"), ("sub", "Abonnements et réabonnements"),
            ("gift", "Abonnements offerts"), ("raid", "Raids"), ("gif", "Messages avec un lien GIF"),
        },
        ["kick"] = new[]
        {
            ("emotes", "Emotes (images)"), ("reward", "Récompenses de points"), ("sub", "Abonnements"), ("gift", "Abonnements offerts"), ("gif", "Messages avec un lien GIF"),
        },
        ["youtube"] = new[]
        {
            ("superchat", "Super Chats"), ("sticker", "Super Stickers"), ("member", "Nouveaux membres et jalons"), ("gift", "Adhésions offertes"),
        },
    };

    public static bool Shown(string platformId, string kind) => kind == "message" || !StreamingStore.Current.ChatHidden.Contains(platformId + ":" + kind);

    public static readonly Regex GifLink = new(@"(giphy\.com|tenor\.com|\.gif(\?|$|\s))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

abstract class ChatSource : IDisposable
{
    CancellationTokenSource? cts;

    public abstract string PlatformId { get; }
    public string Status { get; private set; } = "Arrêté";
    public bool Reading { get; private set; }
    public event Action<ChatMessage>? Message;
    public event Action? StatusChanged;

    protected Platform P => Streaming.Platforms.First(p => p.Id == PlatformId);

    /// <summary>true when a message can be sent to this platform now</summary>
    public abstract bool CanSend { get; }

    public void Start()
    {
        if (cts != null) return;
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        _ = Task.Run(() => LoopAsync(ct));
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
        SetStatus("Arrêté", false);
    }

    public void Dispose() => Stop();

    public abstract Task SendAsync(string text, CancellationToken ct);
    protected abstract Task ReadAsync(CancellationToken ct);

    protected void SetStatus(string status, bool reading)
    {
        Status = status;
        Reading = reading;
        StatusChanged?.Invoke();
    }

    protected void Emit(ChatMessage m) => Message?.Invoke(m);

    async Task LoopAsync(CancellationToken ct)
    {
        int delay = 3;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!P.Connected) { SetStatus("Compte non connecté", false); delay = 10; }
                else { await ReadAsync(ct); delay = 3; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                SetStatus("Erreur : " + e.Message, false);
                delay = Math.Min(delay * 2, 60);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch (OperationCanceledException) { break; }
        }
    }

    protected static async Task<string?> ReceiveTextAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, r.Count);
        } while (!r.EndOfMessage);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    protected static Task SendTextAsync(ClientWebSocket ws, string text, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
}

/// <summary>Twitch chat: IRC over WebSocket, authenticated with the user token.</summary>
sealed class TwitchChatSource : ChatSource
{
    static string Url => Environment.GetEnvironmentVariable("STREAMLAUNCHER_TWITCH_IRC") ?? "wss://irc-ws.chat.twitch.tv:443";
    static string EventSubUrl => Environment.GetEnvironmentVariable("STREAMLAUNCHER_TWITCH_EVENTSUB") ?? "wss://eventsub.wss.twitch.tv/ws";
    ClientWebSocket? ws;
    string channel = "";
    volatile bool redemptions; // true while EventSub delivers the reward redemptions (then the IRC lines of rewards are dropped: they would show twice)

    public override string PlatformId => "twitch";
    public override bool CanSend => ws is { State: WebSocketState.Open } && P.HasScope("chat:edit");

    protected override async Task ReadAsync(CancellationToken ct)
    {
        var twitch = (TwitchPlatform)P;
        if (!twitch.HasScope("chat:read")) { SetStatus("Reconnecte Twitch pour activer le chat (nouveaux droits)", false); await Task.Delay(TimeSpan.FromSeconds(20), ct); return; }
        SetStatus("Connexion…", false);
        var login = await twitch.LoginAsync(ct);
        var token = await twitch.AccessTokenAsync(ct);
        channel = "#" + login.ToLowerInvariant();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(Url), ct);
        ws = socket;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = RedemptionsAsync(twitch, linked.Token);
        try
        {
            await SendTextAsync(socket, "CAP REQ :twitch.tv/tags twitch.tv/commands", ct);
            await SendTextAsync(socket, "PASS oauth:" + token, ct);
            await SendTextAsync(socket, "NICK " + login.ToLowerInvariant(), ct);
            await SendTextAsync(socket, "JOIN " + channel, ct);
            while (true)
            {
                var frame = await ReceiveTextAsync(socket, ct);
                if (frame == null) throw new InvalidOperationException("connexion fermée par Twitch");
                foreach (var line in frame.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("PING")) { await SendTextAsync(socket, "PONG :tmi.twitch.tv", ct); continue; }
                    if (line.Contains("Login authentication failed")) throw new InvalidOperationException("authentification refusée (reconnecte Twitch)");
                    if (line.Contains(" 366 ") || line.Contains(" JOIN ")) { SetStatus("Connecté (#" + login + ")", true); continue; }
                    if (ParseIrc(line) is { } m && !(redemptions && m.Kind == "reward")) Emit(m);
                }
            }
        }
        finally { ws = null; redemptions = false; linked.Cancel(); }
    }

    /// <summary>channel point rewards (with or without a message) come from EventSub; reconnects by itself while the chat is open</summary>
    async Task RedemptionsAsync(TwitchPlatform twitch, CancellationToken ct)
    {
        int delay = 3;
        bool hinted = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!twitch.HasScope("channel:read:redemptions")) // not granted yet: check again, so that reconnecting the account is enough (no need to reopen the chat)
                {
                    if (!hinted) Emit(new ChatMessage(DateTime.Now, "twitch", "Stream Launcher", "Reconnecte Twitch (Infos du live > Twitch > Connecter) pour afficher les récompenses de points de chaîne : nouveau droit à accepter.", System: true));
                    hinted = true;
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    continue;
                }
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(EventSubUrl), ct);
                while (true)
                {
                    var frame = await ReceiveTextAsync(socket, ct);
                    if (frame == null) break;
                    using var doc = JsonDocument.Parse(frame);
                    var root = doc.RootElement;
                    switch (root.GetProperty("metadata").GetProperty("message_type").GetString())
                    {
                        case "session_welcome":
                            await twitch.SubscribeRedemptionsAsync(root.GetProperty("payload").GetProperty("session").GetProperty("id").GetString()!, ct);
                            redemptions = true;
                            delay = 3;
                            break;
                        case "notification":
                            if (ParseRedemption(root.GetProperty("payload").GetProperty("event")) is { } m) Emit(m);
                            break;
                        case "session_reconnect": case "revocation": goto reconnect;
                    }
                }
                reconnect:;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { RunLog.Write("récompenses Twitch (EventSub) : " + e.Message); }
            redemptions = false;
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch (OperationCanceledException) { break; }
            delay = Math.Min(delay * 2, 60);
        }
    }

    public static ChatMessage? ParseRedemption(JsonElement e)
    {
        try
        {
            var reward = e.GetProperty("reward");
            var text = $"{reward.GetProperty("title").GetString()} ({reward.GetProperty("cost").GetInt32()})";
            var input = e.TryGetProperty("user_input", out var ui) ? ui.GetString() ?? "" : "";
            if (input != "") text += " : " + input;
            return new ChatMessage(DateTime.Now, "twitch", e.GetProperty("user_name").GetString() ?? "?", text, Kind: "reward", Extra: "Récompense");
        }
        catch { return null; }
    }

    public override async Task SendAsync(string text, CancellationToken ct)
    {
        var socket = ws;
        if (socket is not { State: WebSocketState.Open }) throw new InvalidOperationException("chat Twitch non connecté");
        if (!P.HasScope("chat:edit")) throw new InvalidOperationException("reconnecte Twitch (droit d'écriture manquant)");
        await SendTextAsync(socket, $"PRIVMSG {channel} :{text}", ct);
    }

    static readonly Regex Irc = new(@"^(?:@(?<tags>\S+) )?(?::(?<prefix>\S+) )?(?<cmd>[A-Z]+) (?<rest>.*)$", RegexOptions.Compiled);

    static string Unescape(string v) => v.Replace(@"\s", " ").Replace(@"\:", ";").Replace(@"\r", "").Replace(@"\n", " ").Replace(@"\\", @"\");

    /// <summary>"@display-name=Bob;color=#FF0000 :bob!bob@bob.tmi.twitch.tv PRIVMSG #chan :hello" (or USERNOTICE) -> message</summary>
    public static ChatMessage? ParseIrc(string line)
    {
        var m = Irc.Match(line);
        if (!m.Success) return null;
        var cmd = m.Groups["cmd"].Value;
        if (cmd != "PRIVMSG" && cmd != "USERNOTICE") return null;
        var tags = m.Groups["tags"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split('=', 2)).Where(a => a.Length == 2).ToDictionary(a => a[0], a => Unescape(a[1]));
        string Tag(string k) => tags.TryGetValue(k, out var v) ? v : "";
        var nick = m.Groups["prefix"].Value.Split('!')[0];
        var name = Tag("display-name") != "" ? Tag("display-name") : Tag("login") != "" ? Tag("login") : nick;
        var color = Tag("color") != "" ? Tag("color") : null;
        var rest = m.Groups["rest"].Value;
        int at = rest.IndexOf(" :", StringComparison.Ordinal);
        var text = at >= 0 ? rest[(at + 2)..] : "";
        var badges = Tag("badges") == "" ? null : Tag("badges").Split(',', StringSplitOptions.RemoveEmptyEntries);
        int shift = 0;
        if (text.StartsWith("\u0001ACTION ") && text.EndsWith("\u0001")) { text = "/me " + text[8..^1]; shift = 4; }
        var emotes = TwitchEmotes(Tag("emotes"), text, shift);

        if (cmd == "USERNOTICE")
        {
            var (kind, extra) = Tag("msg-id") switch
            {
                "sub" or "resub" or "extendsub" => ("sub", "Abonnement"),
                "subgift" or "submysterygift" or "giftpaidupgrade" or "anongiftpaidupgrade" => ("gift", "Abonnement offert"),
                "raid" => ("raid", "Raid"),
                _ => ("", ""),
            };
            if (kind == "") return null; // announcements, rituals...
            var plan = Tag("msg-param-sub-plan") switch { "Prime" => "Prime", "2000" => "niveau 2", "3000" => "niveau 3", _ => "niveau 1" };
            var recipient = Tag("msg-param-recipient-display-name");
            var sys = Tag("msg-id") switch // Twitch sends system-msg in English only: rebuild it in French from the tags
            {
                "sub" => $"s'abonne ({plan})",
                "resub" => $"se réabonne ({plan}), {Tag("msg-param-cumulative-months")} mois au total" + (Tag("msg-param-should-share-streak") == "1" ? $", série de {Tag("msg-param-streak-months")} mois" : ""),
                "extendsub" => $"prolonge son abonnement ({plan})",
                "subgift" => $"offre un abonnement ({plan}) à {recipient}",
                "submysterygift" => $"offre {Tag("msg-param-mass-gift-count")} abonnements ({plan}) à la communauté",
                "giftpaidupgrade" or "anongiftpaidupgrade" => "continue l'abonnement offert",
                "raid" => $"raid avec {Tag("msg-param-viewerCount")} spectateurs",
                _ => Tag("system-msg"),
            };
            int offset = sys == "" ? 0 : sys.Length + 3; // the viewer's own text comes after "<system text> : ", the emotes shift with it
            var shifted = emotes?.Select(s => s with { Start = s.Start + offset }).ToArray();
            return new ChatMessage(DateTime.Now, "twitch", name, sys == "" ? text : sys + (text != "" ? " : " + text : ""), color, Kind: kind, Extra: extra, Badges: badges, Emotes: shifted);
        }

        string k = "message"; string? ex = null;
        if (Tag("bits") != "" && Tag("bits") != "0") { k = "bits"; ex = Tag("bits") + " bits"; }
        else if (Tag("custom-reward-id") != "") { k = "reward"; ex = "Récompense"; }
        else if (Tag("msg-id") == "highlighted-message") { k = "highlight"; ex = "Mis en avant"; }
        else if (ChatKinds.GifLink.IsMatch(text)) k = "gif";
        return new ChatMessage(DateTime.Now, "twitch", name, text, color, Kind: k, Extra: ex, Badges: badges, Emotes: emotes, SourceRoom: Tag("source-room-id") is { Length: > 0 } room ? room : null); // source-room-id only exists during a shared chat (Stream Together)
    }

    /// <summary>the "emotes" tag ("25:0-4,12-16/1902:6-10": id, then first-last positions in code points) as spans of the text</summary>
    static EmoteSpan[]? TwitchEmotes(string tag, string text, int shift)
    {
        if (tag == "") return null;
        var idx = new List<int>(); // code point number -> UTF-16 index
        for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) idx.Add(i);
        var spans = new List<EmoteSpan>();
        foreach (var part in tag.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':', 2);
            if (kv.Length != 2) continue;
            foreach (var range in kv[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var ab = range.Split('-');
                if (ab.Length != 2 || !int.TryParse(ab[0], out var a) || !int.TryParse(ab[1], out var b)) continue;
                a += shift; b += shift;
                if (a < 0 || b < a || b >= idx.Count) continue;
                int start = idx[a], end = b + 1 < idx.Count ? idx[b + 1] : text.Length;
                spans.Add(new EmoteSpan(start, end - start, $"https://static-cdn.jtvnw.net/emoticons/v2/{kv[0]}/default/dark/2.0"));
            }
        }
        return spans.Count == 0 ? null : spans.OrderBy(x => x.Start).ToArray();
    }
}

/// <summary>Kick chat: Kick sends the channel's events as webhooks (official API) to a small relay (relay/), the app listens to the relay.</summary>
sealed class KickChatSource : ChatSource
{
    // the relay address is filled in after "wrangler deploy" (see relay/README.md)
    const string RelayUrl = "wss://streamlauncher-kick-relay.slide598.workers.dev/ws";
    static readonly string[] Events = { "chat.message.sent", "channel.subscription.new", "channel.subscription.renewal", "channel.subscription.gifts", "channel.reward.redemption.updated" };

    public override string PlatformId => "kick";
    public override bool CanSend => P.Connected && P.HasScope("chat:write");

    protected override async Task ReadAsync(CancellationToken ct)
    {
        if (!P.HasScope("events:subscribe")) { SetStatus("Reconnecte Kick pour lire le chat (nouveau droit « événements »)", false); await Task.Delay(TimeSpan.FromSeconds(20), ct); return; }
        SetStatus("Connexion…", false);
        await ((KickPlatform)P).SubscribeEventsAsync(Events, ct);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"{Environment.GetEnvironmentVariable("STREAMLAUNCHER_KICK_RELAY") ?? RelayUrl}/{P.UserId}"), ct);
        SetStatus($"Connecté (lecture : {P.UserName})", true);
        using var keepAlive = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(async () => // the relay answers "pong" by itself: the connection is never idle
        {
            try { while (true) { await Task.Delay(TimeSpan.FromSeconds(30), keepAlive.Token); await SendTextAsync(socket, "ping", keepAlive.Token); } }
            catch { }
        });
        try
        {
            while (true)
            {
                var frame = await ReceiveTextAsync(socket, ct);
                if (frame == null) throw new InvalidOperationException("connexion fermée par le relais");
                if (frame == "pong") continue;
                try
                {
                    using var doc = JsonDocument.Parse(frame);
                    var type = doc.RootElement.GetProperty("type").GetString() ?? "";
                    var data = doc.RootElement.GetProperty("data").GetRawText();
                    var m = type == "chat.message.sent" ? ParseMessage(data) : ParseEvent(type, data);
                    if (m != null) Emit(m);
                }
                catch (JsonException) { }
            }
        }
        finally { keepAlive.Cancel(); }
    }

    public override Task SendAsync(string text, CancellationToken ct)
    {
        if (!P.HasScope("chat:write")) throw new InvalidOperationException("reconnecte Kick (droit « écrire dans le chat » non accordé : à activer sur l'application Kick)");
        return ((KickPlatform)P).SendChatAsync(text, ct);
    }

    static readonly Regex Emote = new(@"\[emote:(\d+):([^\]]+)\]", RegexOptions.Compiled);

    public static ChatMessage? ParseMessage(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var r = doc.RootElement;
            var sender = r.GetProperty("sender");
            var color = sender.TryGetProperty("identity", out var id) && id.ValueKind == JsonValueKind.Object && id.TryGetProperty("username_color", out var c) ? c.GetString() : null;
            var content = r.GetProperty("content").GetString() ?? "";
            var sb = new System.Text.StringBuilder();
            var spans = new List<EmoteSpan>();
            int pos = 0;
            foreach (Match m in Emote.Matches(content)) // "[emote:37226:KEKW]" becomes "KEKW", remembered as an image
            {
                sb.Append(content, pos, m.Index - pos);
                spans.Add(new EmoteSpan(sb.Length, m.Groups[2].Length, $"https://files.kick.com/emotes/{m.Groups[1].Value}/fullsize"));
                sb.Append(m.Groups[2].Value);
                pos = m.Index + m.Length;
            }
            sb.Append(content, pos, content.Length - pos);
            var text = sb.ToString();
            return new ChatMessage(DateTime.Now, "kick", sender.GetProperty("username").GetString() ?? "?", text, color, Kind: ChatKinds.GifLink.IsMatch(text) ? "gif" : "message", Emotes: spans.Count == 0 ? null : spans.ToArray());
        }
        catch { return null; }
    }

    /// <summary>reward, subscription and gifted subscriptions webhooks (official payloads)</summary>
    public static ChatMessage? ParseEvent(string type, string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var r = doc.RootElement;
            string User(string prop) => r.TryGetProperty(prop, out var u) && u.ValueKind == JsonValueKind.Object && u.TryGetProperty("username", out var n) ? n.GetString() ?? "?" : "?";
            switch (type)
            {
                case "channel.reward.redemption.updated":
                    if (r.GetProperty("status").GetString() != "pending") return null; // accepted / rejected: same redemption, already shown
                    var reward = r.GetProperty("reward");
                    var input = r.TryGetProperty("user_input", out var ui) ? ui.GetString() : "";
                    return new ChatMessage(DateTime.Now, "kick", User("redeemer"), $"{reward.GetProperty("title").GetString()} {input}".Trim(), null, Kind: "reward", Extra: "Récompense");
                case "channel.subscription.new":
                    return new ChatMessage(DateTime.Now, "kick", User("subscriber"), "s'abonne", null, Kind: "sub", Extra: "Abonnement");
                case "channel.subscription.renewal":
                    return new ChatMessage(DateTime.Now, "kick", User("subscriber"), $"se réabonne ({r.GetProperty("duration").GetInt32()} mois)", null, Kind: "sub", Extra: "Abonnement");
                case "channel.subscription.gifts":
                    int n = r.TryGetProperty("giftees", out var g) && g.ValueKind == JsonValueKind.Array ? g.GetArrayLength() : 0;
                    var anonymous = r.GetProperty("gifter").TryGetProperty("is_anonymous", out var an) && an.ValueKind == JsonValueKind.True;
                    return new ChatMessage(DateTime.Now, "kick", anonymous ? "Anonyme" : User("gifter"), n > 0 ? $"offre {n} abonnement(s)" : "offre des abonnements", null, Kind: "gift", Extra: "Abonnement offert");
            }
        }
        catch { }
        return null;
    }
}

/// <summary>YouTube chat: polls liveChatMessages (10 s minimum between two reads: each read costs quota units).</summary>
sealed class YouTubeChatSource : ChatSource
{
    string? chatId;

    public override string PlatformId => "youtube";
    public override bool CanSend => P.Connected && chatId != null;

    protected override async Task ReadAsync(CancellationToken ct)
    {
        var yt = (YouTubePlatform)P;
        SetStatus("Recherche de la diffusion…", false);
        chatId = await yt.LiveChatIdAsync(ct);
        if (chatId == null) { SetStatus("Aucune diffusion YouTube en cours ou programmée", false); await Task.Delay(TimeSpan.FromSeconds(30), ct); return; }

        string? page = null;
        bool first = true; // the first read only gives the existing history: not shown
        while (true)
        {
            var r = await yt.LiveChatMessagesAsync(chatId, page, ct);
            YouTubeQuota.Add(5);
            page = r.TryGetProperty("nextPageToken", out var t) ? t.GetString() : null;
            if (!first && r.TryGetProperty("items", out var items))
                foreach (var i in items.EnumerateArray())
                    if (ParseItem(i) is { } m) Emit(m);
            first = false;
            SetStatus($"Connecté (quota estimé : {YouTubeQuota.Used}/{YouTubeQuota.Daily} aujourd'hui)", true);
            int wait = Math.Max(10000, r.TryGetProperty("pollingIntervalMillis", out var p) ? p.GetInt32() : 0);
            await Task.Delay(wait, ct);
        }
    }

    public override async Task SendAsync(string text, CancellationToken ct)
    {
        if (chatId == null) throw new InvalidOperationException("aucune diffusion YouTube active");
        await ((YouTubePlatform)P).LiveChatSendAsync(chatId, text, ct);
        YouTubeQuota.Add(50);
    }

    public static ChatMessage? ParseItem(JsonElement i)
    {
        var sn = i.GetProperty("snippet");
        var author = i.GetProperty("authorDetails").GetProperty("displayName").GetString() ?? "?";
        string D(string name, string child) => sn.TryGetProperty(name, out var d) && d.TryGetProperty(child, out var c) ? c.GetString() ?? "" : "";
        switch (sn.GetProperty("type").GetString())
        {
            case "textMessageEvent":
                var text = sn.GetProperty("displayMessage").GetString() ?? "";
                return new ChatMessage(DateTime.Now, "youtube", author, text, Kind: ChatKinds.GifLink.IsMatch(text) ? "gif" : "message");
            case "superChatEvent":
                return new ChatMessage(DateTime.Now, "youtube", author, D("superChatDetails", "userComment"), Kind: "superchat", Extra: D("superChatDetails", "amountDisplayString") + " Super Chat");
            case "superStickerEvent":
                var alt = sn.TryGetProperty("superStickerDetails", out var ss) && ss.TryGetProperty("superStickerMetadata", out var md) && md.TryGetProperty("altText", out var at) ? at.GetString() ?? "" : "";
                return new ChatMessage(DateTime.Now, "youtube", author, alt, Kind: "sticker", Extra: D("superStickerDetails", "amountDisplayString") + " Super Sticker");
            case "newSponsorEvent":
                return new ChatMessage(DateTime.Now, "youtube", author, "devient membre", Kind: "member", Extra: "Nouveau membre");
            case "memberMilestoneChatEvent":
                return new ChatMessage(DateTime.Now, "youtube", author, D("memberMilestoneChatDetails", "userComment"), Kind: "member", Extra: "Jalon de membre");
            case "membershipGiftingEvent":
                return new ChatMessage(DateTime.Now, "youtube", author, "offre des adhésions", Kind: "gift", Extra: "Adhésions offertes");
            case "giftMembershipReceivedEvent":
                return new ChatMessage(DateTime.Now, "youtube", author, "reçoit une adhésion offerte", Kind: "gift", Extra: "Adhésion reçue");
            default:
                return null;
        }
    }
}

/// <summary>Local estimate of the YouTube API units used today (the real counter is in the Google Cloud console).</summary>
static class YouTubeQuota
{
    public const int Daily = 10000;
    public static int Used => StreamingStore.Current.YtQuotaDay == DateTime.Now.ToString("yyyy-MM-dd") ? StreamingStore.Current.YtQuotaUnits : 0;

    public static void Add(int units)
    {
        var s = StreamingStore.Current;
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (s.YtQuotaDay != today) { s.YtQuotaDay = today; s.YtQuotaUnits = 0; }
        s.YtQuotaUnits += units;
        if (s.YtQuotaUnits % 100 < units) s.Save(); // not at every read
    }
}
