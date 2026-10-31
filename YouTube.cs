using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// YouTube: Google OAuth for desktop apps (PKCE, loopback redirect), YouTube Data API v3. The live is a broadcast (active, else the next one
/// scheduled in YouTube Studio): title through liveBroadcasts.update, tags and category through videos.update. YouTube has no "game"
/// field in its API: the game becomes the category "Gaming" and a tag.
/// </summary>
sealed class YouTubePlatform : Platform
{
    static string Api => Environment.GetEnvironmentVariable("STREAMLAUNCHER_YT_API") ?? "https://www.googleapis.com/youtube/v3";
    static string AuthUrl => Environment.GetEnvironmentVariable("STREAMLAUNCHER_YT_AUTH") ?? "https://accounts.google.com/o/oauth2/v2/auth";
    static string TokenUrl => Environment.GetEnvironmentVariable("STREAMLAUNCHER_YT_TOKEN") ?? "https://oauth2.googleapis.com/token";
    const string Scope = "https://www.googleapis.com/auth/youtube.force-ssl";
    const int TitleMax = 100;
    const string GamingCategory = "20";

    public override string Id => "youtube";
    public override string Name => "YouTube";
    public override bool NeedsSecret => true;
    public override bool UsesGameIds => false;
    protected override string DefaultClientId => BuiltIn.YouTubeId;
    protected override string DefaultClientSecret => BuiltIn.YouTubeSecret;
    protected override long? IdOf(GameEntry g) => null;

    static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public override async Task ConnectAsync(Action<string> info, CancellationToken ct)
    {
        var verifier = B64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = B64Url(RandomNumberGenerator.GetBytes(16));
        int port = FreePort();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();

        info("Autorise l'application dans la page Google qui s'ouvre…\n\n(en attente de ton autorisation)");
        OpenBrowser($"{AuthUrl}?client_id={Uri.EscapeDataString(ClientId)}&redirect_uri={Uri.EscapeDataString(redirect)}&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scope)}&code_challenge={challenge}&code_challenge_method=S256&state={state}&access_type=offline&prompt=consent");

        using var reg = ct.Register(listener.Stop);
        string code;
        while (true)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { ct.ThrowIfCancellationRequested(); throw; }
            var q = ctx.Request.QueryString;
            bool ok = q["code"] != null && q["state"] == state;
            var html = Encoding.UTF8.GetBytes(ok ? "<html><body style='font-family:Segoe UI'><h3>Connexion réussie.</h3>Tu peux fermer cet onglet et revenir à Stream Launcher.</body></html>"
                : "<html><body style='font-family:Segoe UI'>Requête ignorée.</body></html>");
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.OutputStream.WriteAsync(html, ct);
            ctx.Response.Close();
            if (q["error"] != null) throw new InvalidOperationException("YouTube : " + q["error"]);
            if (!ok) continue;
            code = q["code"]!;
            break;
        }

        using var r = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = ClientId, ["client_secret"] = Secret,
            ["redirect_uri"] = redirect, ["code_verifier"] = verifier,
        }), ct);
        Store(await ReadAsync("YouTube", r, ct));
        var me = (await SendAsync(HttpMethod.Get, "/channels?part=snippet&mine=true", null, ct)).GetProperty("items")[0];
        Auth.UserId = me.GetProperty("id").GetString() ?? "";
        Auth.UserName = me.GetProperty("snippet").GetProperty("title").GetString() ?? "";
        Secrets.Save();
    }

    protected override async Task RefreshAsync(CancellationToken ct)
    {
        using var r = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = Auth.Refresh, ["client_id"] = ClientId, ["client_secret"] = Secret,
        }), ct);
        Store(await ReadAsync("YouTube", r, ct));
    }

    async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, Api + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var r = await Http.SendAsync(req, ct);
        return await ReadAsync("YouTube", r, ct);
    }

    /// <summary>the live: the active broadcast, else the next one scheduled (YouTube Studio creates it; the API only edits it)</summary>
    async Task<JsonElement> FindBroadcastAsync(CancellationToken ct)
    {
        foreach (var status in new[] { "active", "upcoming" })
        {
            var r = await SendAsync(HttpMethod.Get, $"/liveBroadcasts?part=snippet&broadcastStatus={status}&maxResults=1", null, ct);
            if (r.TryGetProperty("items", out var items) && items.GetArrayLength() > 0) return items[0];
        }
        throw new InvalidOperationException("YouTube : aucune diffusion en cours ni programmée. Crée-la dans YouTube Studio (Créer > Passer en direct).");
    }

    // ---- live chat (unified chat window)

    /// <summary>chat id of the active broadcast, else of the next scheduled one; null when there is none</summary>
    public async Task<string?> LiveChatIdAsync(CancellationToken ct)
    {
        try { return (await FindBroadcastAsync(ct)).GetProperty("snippet").GetProperty("liveChatId").GetString(); }
        catch (InvalidOperationException e) when (e.Message.Contains("aucune diffusion")) { return null; }
    }

    public Task<JsonElement> LiveChatMessagesAsync(string chatId, string? pageToken, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"/liveChat/messages?liveChatId={Uri.EscapeDataString(chatId)}&part=snippet,authorDetails&maxResults=200" + (pageToken == null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken)), null, ct);

    /// <param name="seconds">length of a temporary ban, null for a permanent one</param>
    public Task BanAsync(string chatId, string channelId, int? seconds, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/liveChat/bans?part=snippet", new
        {
            snippet = new
            {
                liveChatId = chatId,
                type = seconds == null ? "permanent" : "temporary",
                banDurationSeconds = seconds,
                bannedUserDetails = new { channelId },
            },
        }, ct);

    public Task DeleteMessageAsync(string messageId, CancellationToken ct) => SendAsync(HttpMethod.Delete, "/liveChat/messages?id=" + Uri.EscapeDataString(messageId), null, ct);

    /// <summary>sends a message (YouTube refuses more than 200 characters)</summary>
    public Task LiveChatSendAsync(string chatId, string text, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/liveChat/messages?part=snippet",
            new { snippet = new { liveChatId = chatId, type = "textMessageEvent", textMessageDetails = new { messageText = text.Length > 200 ? text[..200] : text } } }, ct);

    async Task<JsonElement> VideoSnippetAsync(string id, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Get, "/videos?part=snippet&id=" + Uri.EscapeDataString(id), null, ct);
        return r.GetProperty("items")[0].GetProperty("snippet");
    }

    public override async Task<LiveInfo> GetLiveAsync(CancellationToken ct)
    {
        var b = await FindBroadcastAsync(ct);
        var v = await VideoSnippetAsync(b.GetProperty("id").GetString()!, ct);
        return new LiveInfo
        {
            Title = b.GetProperty("snippet").GetProperty("title").GetString() ?? "",
            GameName = "", // no game field on YouTube
            Tags = v.TryGetProperty("tags", out var t) ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new(),
        };
    }

    public override async Task<string?> SetLiveAsync(string? title, string? gameName, List<string>? tags, CancellationToken ct)
    {
        var warnings = new List<string>();
        var b = await FindBroadcastAsync(ct);
        var id = b.GetProperty("id").GetString()!;
        var bs = b.GetProperty("snippet");
        var currentTitle = bs.GetProperty("title").GetString() ?? "";

        if (!string.IsNullOrWhiteSpace(title))
        {
            if (title.Length > TitleMax) { title = title[..TitleMax]; warnings.Add($"titre raccourci à {TitleMax} caractères (limite de YouTube)"); }
            // the update replaces the whole snippet: keep the schedule and the description
            var snippet = new Dictionary<string, object?> { ["title"] = title, ["scheduledStartTime"] = bs.GetProperty("scheduledStartTime").GetString() };
            if (bs.TryGetProperty("description", out var d)) snippet["description"] = d.GetString();
            await SendAsync(HttpMethod.Put, "/liveBroadcasts?part=snippet", new { id, snippet }, ct);
            currentTitle = title;
        }

        bool game = !string.IsNullOrWhiteSpace(gameName);
        if (tags != null || game)
        {
            var v = await VideoSnippetAsync(id, ct);
            var newTags = tags != null ? tags.ToList()
                : v.TryGetProperty("tags", out var t) ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
            if (game && !newTags.Contains(gameName!.Trim(), StringComparer.OrdinalIgnoreCase)) newTags.Add(gameName!.Trim());
            var snippet = new Dictionary<string, object?>
            {
                ["title"] = currentTitle,
                ["categoryId"] = game ? GamingCategory : v.GetProperty("categoryId").GetString(),
                ["tags"] = newTags,
            };
            if (v.TryGetProperty("description", out var vd)) snippet["description"] = vd.GetString();
            await SendAsync(HttpMethod.Put, "/videos?part=snippet", new { id, snippet }, ct);
            if (game) warnings.Add("YouTube n'a pas de champ « jeu » : le jeu est enregistré comme catégorie « Jeux vidéo » et comme tag");
        }
        return warnings.Count == 0 ? null : string.Join(" ; ", warnings);
    }

    // no game dictionary on YouTube
    public override Task<List<GameEntry>> SearchGamesAsync(string query, CancellationToken ct) => Task.FromResult(new List<GameEntry>());
    public override Task<List<GameEntry>> ListGamesAsync(CancellationToken ct) => Task.FromResult(new List<GameEntry>());

    /// <summary>titles of the latest finished broadcasts</summary>
    public override async Task<List<HistoryEntry>> RecentLivesAsync(CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Get, $"/liveBroadcasts?part=snippet&broadcastStatus=completed&maxResults={StreamingStore.HistorySize}", null, ct);
        if (!r.TryGetProperty("items", out var items)) return new();
        return items.EnumerateArray().Select(i => i.GetProperty("snippet")).Select(s => new HistoryEntry
        {
            Title = s.GetProperty("title").GetString() ?? "",
            Date = DateTime.TryParse(s.TryGetProperty("actualStartTime", out var a) ? a.GetString() : s.GetProperty("publishedAt").GetString(), out var d) ? d.ToLocalTime() : DateTime.Now,
            Source = "YouTube",
        }).ToList();
    }
}
