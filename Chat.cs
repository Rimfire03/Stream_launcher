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
/// <param name="Badges">Twitch badges as "set/version" (broadcaster/1, subscriber/12...)</param>
record ChatMessage(DateTime Time, string PlatformId, string Author, string Text, string? Color = null, bool Mine = false, bool System = false,
    string Kind = "message", string? Extra = null, string[]? Badges = null);

/// <summary>what a platform can show besides plain messages: (kind, label in the settings)</summary>
static class ChatKinds
{
    public static readonly Dictionary<string, (string Kind, string Label)[]> ByPlatform = new()
    {
        ["twitch"] = new[]
        {
            ("badges", "Badges des utilisateurs"), ("highlight", "Messages mis en avant"), ("bits", "Cheers (bits)"),
            ("reward", "Messages de récompense de points de chaîne"), ("sub", "Abonnements et réabonnements"),
            ("gift", "Abonnements offerts"), ("raid", "Raids"), ("gif", "Messages avec un lien GIF"),
        },
        ["kick"] = new[]
        {
            ("reward", "Récompenses de points"), ("sub", "Abonnements"), ("gift", "Abonnements offerts"), ("gif", "Messages avec un lien GIF"),
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
    ClientWebSocket? ws;
    string channel = "";

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
                    if (ParseIrc(line) is { } m) Emit(m);
                }
            }
        }
        finally { ws = null; }
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
        if (text.StartsWith("\u0001ACTION ") && text.EndsWith("\u0001")) text = "/me " + text[8..^1];

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
            var sys = Tag("system-msg");
            return new ChatMessage(DateTime.Now, "twitch", name, (sys + (text != "" ? " : " + text : "")).Trim(), color, Kind: kind, Extra: extra, Badges: badges);
        }

        string k = "message"; string? ex = null;
        if (Tag("bits") != "" && Tag("bits") != "0") { k = "bits"; ex = Tag("bits") + " bits"; }
        else if (Tag("custom-reward-id") != "") { k = "reward"; ex = "Récompense"; }
        else if (Tag("msg-id") == "highlighted-message") { k = "highlight"; ex = "Mis en avant"; }
        else if (ChatKinds.GifLink.IsMatch(text)) k = "gif";
        return new ChatMessage(DateTime.Now, "twitch", name, text, color, Kind: k, Extra: ex, Badges: badges);
    }
}

/// <summary>Kick chat: reads the real-time feed of kick.com (unofficial), sends through the official API.</summary>
sealed class KickChatSource : ChatSource
{
    const string PusherUrl = "wss://ws-us2.pusher.com/app/32cbd69e4b950bf97679?protocol=7&client=js&version=8.4.0-rc2&flash=false";
    static readonly HttpClient Web = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };

    public override string PlatformId => "kick";
    public override bool CanSend => P.Connected && P.HasScope("chat:write");

    protected override async Task ReadAsync(CancellationToken ct)
    {
        var slug = P.UserName;
        SetStatus("Connexion…", false);
        long chatroom = await ChatroomIdAsync(slug, ct);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(Environment.GetEnvironmentVariable("STREAMLAUNCHER_KICK_WS") ?? PusherUrl), ct);
        while (true)
        {
            var frame = await ReceiveTextAsync(socket, ct);
            if (frame == null) throw new InvalidOperationException("connexion fermée par Kick");
            using var doc = JsonDocument.Parse(frame);
            var ev = doc.RootElement.TryGetProperty("event", out var e) ? e.GetString() : "";
            var data = doc.RootElement.TryGetProperty("data", out var d) ? (d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : d.GetRawText()) : "";
            switch (ev)
            {
                case "pusher:connection_established":
                    await SendTextAsync(socket, JsonSerializer.Serialize(new { @event = "pusher:subscribe", data = new { auth = "", channel = $"chatrooms.{chatroom}.v2" } }), ct);
                    break;
                case "pusher_internal:subscription_succeeded":
                    SetStatus($"Connecté (lecture : {slug})", true);
                    break;
                case "pusher:ping":
                    await SendTextAsync(socket, "{\"event\":\"pusher:pong\",\"data\":{}}", ct);
                    break;
                case @"App\Events\ChatMessageEvent":
                    if (ParseMessage(data) is { } m) Emit(m);
                    break;
                case @"App\Events\RewardRedeemedEvent" or @"App\Events\SubscriptionEvent" or @"App\Events\GiftedSubscriptionsEvent":
                    if (ParseEvent(ev!, data) is { } evm) Emit(evm);
                    break;
            }
        }
    }

    /// <summary>the real-time room of a channel is identified by its chat room id, only given by kick.com's own API</summary>
    static async Task<long> ChatroomIdAsync(string slug, CancellationToken ct)
    {
        var url = Environment.GetEnvironmentVariable("STREAMLAUNCHER_KICK_WEB") ?? "https://kick.com/api/v2/channels";
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{url}/{Uri.EscapeDataString(slug)}");
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var r = await Web.SendAsync(req, ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"lecture du chat indisponible (kick.com a répondu {(int)r.StatusCode}) ; l'envoi reste possible");
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("chatroom").GetProperty("id").GetInt64();
    }

    public override Task SendAsync(string text, CancellationToken ct)
    {
        if (!P.HasScope("chat:write")) throw new InvalidOperationException("reconnecte Kick (droit « écrire dans le chat » non accordé : à activer sur l'application Kick)");
        return ((KickPlatform)P).SendChatAsync(text, ct);
    }

    static readonly Regex Emote = new(@"\[emote:\d+:([^\]]+)\]", RegexOptions.Compiled);

    public static ChatMessage? ParseMessage(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var r = doc.RootElement;
            var sender = r.GetProperty("sender");
            var color = sender.TryGetProperty("identity", out var id) && id.TryGetProperty("color", out var c) ? c.GetString() : null;
            var text = Emote.Replace(r.GetProperty("content").GetString() ?? "", "$1");
            return new ChatMessage(DateTime.Now, "kick", sender.GetProperty("username").GetString() ?? "?", text, color, Kind: ChatKinds.GifLink.IsMatch(text) ? "gif" : "message");
        }
        catch { return null; }
    }

    /// <summary>reward, subscription and gifted subscriptions events of the real-time feed (unofficial: field names are read defensively)</summary>
    public static ChatMessage? ParseEvent(string ev, string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var r = doc.RootElement;
            string Str(params string[] names) { foreach (var n in names) if (r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString() ?? ""; return ""; }
            switch (ev)
            {
                case @"App\Events\RewardRedeemedEvent":
                    return new ChatMessage(DateTime.Now, "kick", Str("username", "user_username"), (Str("reward_title", "title") + " " + Str("user_input", "input")).Trim(), null, Kind: "reward", Extra: "Récompense");
                case @"App\Events\SubscriptionEvent":
                    return new ChatMessage(DateTime.Now, "kick", Str("username"), r.TryGetProperty("months", out var mo) ? $"s'abonne ({mo} mois)" : "s'abonne", null, Kind: "sub", Extra: "Abonnement");
                case @"App\Events\GiftedSubscriptionsEvent":
                    var n = r.TryGetProperty("gifted_usernames", out var g) && g.ValueKind == JsonValueKind.Array ? g.GetArrayLength() : 0;
                    return new ChatMessage(DateTime.Now, "kick", Str("gifter_username", "username"), n > 0 ? $"offre {n} abonnement(s)" : "offre des abonnements", null, Kind: "gift", Extra: "Abonnement offert");
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
