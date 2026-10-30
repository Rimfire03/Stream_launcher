using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Twitch and Kick: edit the title, game and tags of the live (the same values go to every connected platform),
// favourites applied in one click, history of the last 15 lives, and a game dictionary that grows by itself.

class LiveInfo
{
    public string Title { get; set; } = "";
    public string GameName { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}

class HistoryEntry : LiveInfo
{
    public DateTime Date { get; set; } = DateTime.Now;
    public string Source { get; set; } = "";

    public bool Same(LiveInfo o) => Title == o.Title && GameName.Equals(o.GameName, StringComparison.OrdinalIgnoreCase) && Tags.SequenceEqual(o.Tags, StringComparer.OrdinalIgnoreCase);
}

/// <summary>A game known by the platforms; ids differ from one platform to the other, the name is the common key.</summary>
class GameEntry
{
    public string Name { get; set; } = "";
    public long? TwitchId { get; set; }
    public long? KickId { get; set; }
}

static class Json
{
    public static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static string Dir
    {
        get
        {
            var d = Environment.GetEnvironmentVariable("STREAMLAUNCHER_DATA") // tests only: keep the real data untouched
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamLauncher");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static T Load<T>(string file) where T : new()
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Dir, file))) ?? new T(); }
        catch { return new T(); }
    }

    public static void Save<T>(string file, T value)
    {
        try { File.WriteAllText(Path.Combine(Dir, file), JsonSerializer.Serialize(value, Opt)); } catch { }
    }
}

/// <summary>Favourites and history (streaming.json in %LOCALAPPDATA%\StreamLauncher).</summary>
class StreamingStore
{
    public List<string> FavTitles { get; set; } = new();
    public List<string> FavGames { get; set; } = new();
    public List<List<string>> FavTagSets { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = new();

    public const int HistorySize = 15;
    const string File_ = "streaming.json";
    static StreamingStore? current;
    public static StreamingStore Current => current ??= Json.Load<StreamingStore>(File_);
    public void Save() => Json.Save(File_, this);

    /// <summary>newest first, no duplicate of the latest entry, 15 at most</summary>
    public void AddHistory(HistoryEntry e)
    {
        if (History.Count > 0 && History[0].Same(e)) { History[0].Date = e.Date; Save(); return; }
        History.Insert(0, e);
        if (History.Count > HistorySize) History.RemoveRange(HistorySize, History.Count - HistorySize);
        Save();
    }

    public static bool AddUnique(List<string> list, string v)
    {
        if (v.Trim() == "" || list.Contains(v, StringComparer.OrdinalIgnoreCase)) return false;
        list.Add(v.Trim());
        return true;
    }
}

/// <summary>Game dictionary: every game seen on a platform, kept in games.json and refreshed from the platforms' category lists.</summary>
class GameDict
{
    public DateTime Updated { get; set; }
    public List<GameEntry> Games { get; set; } = new();

    const string File_ = "games.json";
    static GameDict? current;
    static readonly object Gate = new();
    public static GameDict Current => current ??= Json.Load<GameDict>(File_);

    public bool Stale => Games.Count == 0 || DateTime.Now - Updated > TimeSpan.FromDays(7);

    public GameEntry? Get(string name)
    {
        lock (Gate) return Games.FirstOrDefault(g => g.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public List<GameEntry> Find(string text, int max = 30)
    {
        text = text.Trim();
        lock (Gate)
            return Games.Where(g => text == "" || g.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(g => g.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ThenBy(g => g.Name).Take(max).ToList();
    }

    public void Merge(IEnumerable<GameEntry> found)
    {
        lock (Gate)
        {
            foreach (var f in found)
            {
                var g = Games.FirstOrDefault(x => x.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase));
                if (g == null) Games.Add(new GameEntry { Name = f.Name, TwitchId = f.TwitchId, KickId = f.KickId });
                else { g.TwitchId ??= f.TwitchId; g.KickId ??= f.KickId; }
            }
        }
    }

    public void Save()
    {
        lock (Gate) Json.Save(File_, this);
    }
}

/// <summary>Per-platform credentials and tokens, encrypted for the current Windows user (DPAPI) in streaming-auth.bin.</summary>
class PlatformAuth
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Access { get; set; } = "";
    public string Refresh { get; set; } = "";
    public DateTime Expires { get; set; }
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
}

static class Secrets
{
    static Dictionary<string, PlatformAuth>? all;
    static string FilePath => Path.Combine(Json.Dir, "streaming-auth.bin");

    public static PlatformAuth Of(string id)
    {
        if (all == null)
        {
            try { all = JsonSerializer.Deserialize<Dictionary<string, PlatformAuth>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser))); }
            catch { }
            all ??= new();
        }
        if (!all.TryGetValue(id, out var a)) all[id] = a = new PlatformAuth();
        return a;
    }

    public static void Save()
    {
        try { File.WriteAllBytes(FilePath, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(all ?? new()), null, DataProtectionScope.CurrentUser)); } catch { }
    }
}

enum Health { Off, Checking, Ok, Error }

abstract class Platform
{
    protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract bool NeedsSecret { get; }
    protected PlatformAuth Auth => Secrets.Of(Id);

    /// <summary>application id built into Stream Launcher for this platform (a public id, not a secret); empty when the user must bring their own</summary>
    protected virtual string DefaultClientId => "";
    /// <summary>the id in use: the one the user entered, else the built-in one</summary>
    public string ClientId => Auth.ClientId != "" ? Auth.ClientId : DefaultClientId;
    public string UserName => Auth.UserName;
    public bool Configured => ClientId != "" && (!NeedsSecret || Auth.ClientSecret != "");
    public bool Connected => Auth.Refresh != "" || Auth.Access != "";

    public void SetCredentials(string clientId, string secret)
    {
        Auth.ClientId = clientId.Trim();
        Auth.ClientSecret = secret.Trim();
        Secrets.Save();
    }

    public void Disconnect()
    {
        Auth.Access = Auth.Refresh = Auth.UserName = Auth.UserId = "";
        Secrets.Save();
        Mark(Health.Off, "Non connecté");
    }

    // ---- is the connection working? (indicator in the header and in the live dialog)
    public Health State { get; private set; } = Health.Off;
    public string StateText { get; private set; } = "Non connecté";
    public event Action? StateChanged; // may be raised from any thread

    public void Mark(Health h, string text)
    {
        State = h;
        StateText = text;
        StateChanged?.Invoke();
    }

    /// <summary>asks the platform for the channel: proves that the token and the network work</summary>
    public async Task CheckAsync()
    {
        if (!Connected) { Mark(Health.Off, Configured ? $"{Name} : non connecté" : $"{Name} : identifiants à saisir"); return; }
        Mark(Health.Checking, $"{Name} : vérification…");
        try
        {
            using var cts = new CancellationTokenSource(10000);
            await GetLiveAsync(cts.Token);
            Mark(Health.Ok, $"{Name} : connexion opérationnelle ({UserName})");
        }
        catch (Exception e) { Mark(Health.Error, $"{Name} : {e.Message}"); }
    }

    public abstract Task ConnectAsync(Action<string> info, CancellationToken ct);
    public abstract Task<LiveInfo> GetLiveAsync(CancellationToken ct);
    /// <returns>a warning (game not found on this platform...) or null</returns>
    public abstract Task<string?> SetLiveAsync(string? title, string? gameName, List<string>? tags, CancellationToken ct);
    public abstract Task<List<GameEntry>> SearchGamesAsync(string query, CancellationToken ct);
    public abstract Task<List<GameEntry>> ListGamesAsync(CancellationToken ct);
    public virtual Task<List<HistoryEntry>> RecentLivesAsync(CancellationToken ct) => Task.FromResult(new List<HistoryEntry>());

    protected abstract Task RefreshAsync(CancellationToken ct);
    protected abstract long? IdOf(GameEntry g);

    protected void Store(JsonElement tokens)
    {
        Auth.Access = tokens.GetProperty("access_token").GetString() ?? "";
        if (tokens.TryGetProperty("refresh_token", out var r) && r.GetString() is { Length: > 0 } rt) Auth.Refresh = rt;
        Auth.Expires = DateTime.UtcNow.AddSeconds(tokens.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600);
        Secrets.Save();
    }

    protected async Task<string> TokenAsync(CancellationToken ct)
    {
        if (!Connected) throw new InvalidOperationException($"{Name} : compte non connecté");
        if (Auth.Access == "" || Auth.Expires < DateTime.UtcNow.AddSeconds(60))
        {
            try { await RefreshAsync(ct); }
            catch (Exception e) { throw new InvalidOperationException($"{Name} : session expirée, reconnecte le compte ({e.Message})"); }
        }
        return Auth.Access;
    }

    protected static async Task<JsonElement> ReadAsync(string what, HttpResponseMessage r, CancellationToken ct)
    {
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode)
            throw new InvalidOperationException($"{what} : {(int)r.StatusCode} {(body.Length > 200 ? body[..200] : body)}");
        if (string.IsNullOrWhiteSpace(body)) return default;
        using var d = JsonDocument.Parse(body);
        return d.RootElement.Clone();
    }

    /// <summary>id of a game on this platform: from the dictionary, else by searching the platform</summary>
    protected async Task<long?> ResolveGameAsync(string name, CancellationToken ct)
    {
        var g = GameDict.Current.Get(name);
        if (g != null && IdOf(g) is { } known) return known;
        var found = await SearchGamesAsync(name, ct);
        GameDict.Current.Merge(found);
        GameDict.Current.Save();
        var hit = found.FirstOrDefault(f => f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        return hit == null ? null : IdOf(hit);
    }

    protected static void OpenBrowser(string url)
    {
        if (Environment.GetEnvironmentVariable("STREAMLAUNCHER_NO_BROWSER") != null) { File.WriteAllText(Path.Combine(Json.Dir, "last-browser-url.txt"), url); return; } // tests only
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

/// <summary>Twitch: device code flow (public application, no secret), Helix API.</summary>
sealed class TwitchPlatform : Platform
{
    static string Api => Environment.GetEnvironmentVariable("STREAMLAUNCHER_TWITCH_API") ?? "https://api.twitch.tv/helix";
    static string OAuth => Environment.GetEnvironmentVariable("STREAMLAUNCHER_TWITCH_AUTH") ?? "https://id.twitch.tv/oauth2";
    const string Scope = "channel:manage:broadcast";

    protected override string DefaultClientId => "gwjqrmds3c2ewc58x51mnr99v2d74z"; // Stream Launcher's Twitch application (public client)
    public override string Id => "twitch";
    public override string Name => "Twitch";
    public override bool NeedsSecret => false;
    protected override long? IdOf(GameEntry g) => g.TwitchId;

    static async Task<JsonElement> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct, bool allowError = false)
    {
        using var r = await Http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        if (allowError && !r.IsSuccessStatusCode)
        {
            var body = await r.Content.ReadAsStringAsync(ct);
            try { return JsonDocument.Parse(body).RootElement.Clone(); } catch { throw new InvalidOperationException(body); }
        }
        return await ReadAsync("Twitch", r, ct);
    }

    public override async Task ConnectAsync(Action<string> info, CancellationToken ct)
    {
        var dev = await PostFormAsync(OAuth + "/device", new() { ["client_id"] = ClientId, ["scopes"] = Scope }, ct);
        var code = dev.GetProperty("user_code").GetString() ?? "";
        var url = dev.GetProperty("verification_uri").GetString() ?? "https://www.twitch.tv/activate";
        var device = dev.GetProperty("device_code").GetString() ?? "";
        int interval = dev.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5;
        var end = DateTime.UtcNow.AddSeconds(dev.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 600);
        info($"Dans la page Twitch qui s'ouvre, saisis le code :\n\n{code}\n\n(en attente de ton autorisation…)");
        OpenBrowser(url);

        while (DateTime.UtcNow < end)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, interval)), ct);
            var t = await PostFormAsync(OAuth + "/token", new()
            {
                ["client_id"] = ClientId, ["scopes"] = Scope, ["device_code"] = device,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            }, ct, allowError: true);
            if (t.TryGetProperty("access_token", out _))
            {
                Store(t);
                var me = await SendAsync(HttpMethod.Get, "/users", null, ct);
                var u = me.GetProperty("data")[0];
                Auth.UserId = u.GetProperty("id").GetString() ?? "";
                Auth.UserName = u.GetProperty("display_name").GetString() ?? u.GetProperty("login").GetString() ?? "";
                Secrets.Save();
                return;
            }
            var msg = t.TryGetProperty("message", out var m) ? m.GetString() : "";
            if (msg == "slow_down") interval += 5;
            else if (msg != "authorization_pending") throw new InvalidOperationException("Twitch : " + msg);
        }
        throw new TimeoutException("Twitch : code expiré, recommence");
    }

    protected override async Task RefreshAsync(CancellationToken ct) =>
        Store(await PostFormAsync(OAuth + "/token", new() { ["grant_type"] = "refresh_token", ["refresh_token"] = Auth.Refresh, ["client_id"] = ClientId }, ct));

    async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, Api + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        req.Headers.Add("Client-Id", ClientId);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var r = await Http.SendAsync(req, ct);
        return await ReadAsync("Twitch", r, ct);
    }

    public override async Task<LiveInfo> GetLiveAsync(CancellationToken ct)
    {
        var c = (await SendAsync(HttpMethod.Get, "/channels?broadcaster_id=" + Auth.UserId, null, ct)).GetProperty("data")[0];
        return new LiveInfo
        {
            Title = c.GetProperty("title").GetString() ?? "",
            GameName = c.GetProperty("game_name").GetString() ?? "",
            Tags = c.TryGetProperty("tags", out var t) ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new(),
        };
    }

    public override async Task<string?> SetLiveAsync(string? title, string? gameName, List<string>? tags, CancellationToken ct)
    {
        var body = new Dictionary<string, object>();
        string? warning = null;
        if (title != null) body["title"] = title;
        if (tags != null) body["tags"] = tags;
        if (!string.IsNullOrWhiteSpace(gameName))
        {
            if (await ResolveGameAsync(gameName, ct) is { } id) body["game_id"] = id.ToString();
            else warning = $"jeu « {gameName} » introuvable sur Twitch (non modifié)";
        }
        if (body.Count > 0) await SendAsync(new HttpMethod("PATCH"), "/channels?broadcaster_id=" + Auth.UserId, body, ct);
        return warning;
    }

    static List<GameEntry> Games(JsonElement data) => data.EnumerateArray()
        .Where(g => long.TryParse(g.GetProperty("id").GetString(), out _))
        .Select(g => new GameEntry { Name = g.GetProperty("name").GetString() ?? "", TwitchId = long.Parse(g.GetProperty("id").GetString()!) }).ToList();

    public override async Task<List<GameEntry>> SearchGamesAsync(string query, CancellationToken ct) =>
        Games((await SendAsync(HttpMethod.Get, "/search/categories?first=20&query=" + Uri.EscapeDataString(query), null, ct)).GetProperty("data"));

    public override async Task<List<GameEntry>> ListGamesAsync(CancellationToken ct)
    {
        var all = new List<GameEntry>();
        string cursor = "";
        for (int page = 0; page < 5; page++) // the 500 most watched categories
        {
            var r = await SendAsync(HttpMethod.Get, "/games/top?first=100" + (cursor == "" ? "" : "&after=" + Uri.EscapeDataString(cursor)), null, ct);
            all.AddRange(Games(r.GetProperty("data")));
            cursor = r.TryGetProperty("pagination", out var p) && p.TryGetProperty("cursor", out var c) ? c.GetString() ?? "" : "";
            if (cursor == "") break;
        }
        return all;
    }

    /// <summary>titles of the latest past broadcasts (VODs); Twitch does not give the game there</summary>
    public override async Task<List<HistoryEntry>> RecentLivesAsync(CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Get, $"/videos?user_id={Auth.UserId}&type=archive&first={StreamingStore.HistorySize}", null, ct);
        return r.GetProperty("data").EnumerateArray().Select(v => new HistoryEntry
        {
            Title = v.GetProperty("title").GetString() ?? "",
            Date = DateTime.TryParse(v.GetProperty("created_at").GetString(), out var d) ? d.ToLocalTime() : DateTime.Now,
            Source = "Twitch (VOD)",
        }).ToList();
    }
}

/// <summary>Kick: OAuth 2.1 with PKCE through a local callback, public API (docs.kick.com).</summary>
sealed class KickPlatform : Platform
{
    static string Api => Environment.GetEnvironmentVariable("STREAMLAUNCHER_KICK_API") ?? "https://api.kick.com/public";
    static string OAuth => Environment.GetEnvironmentVariable("STREAMLAUNCHER_KICK_AUTH") ?? "https://id.kick.com/oauth";
    public const int CallbackPort = 43117;
    public const string RedirectUri = "http://localhost:43117/callback"; // to declare as-is in the Kick application settings
    const string Scope = "channel:read channel:write";

    public override string Id => "kick";
    public override string Name => "Kick";
    public override bool NeedsSecret => true;
    protected override long? IdOf(GameEntry g) => g.KickId;

    static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public override async Task ConnectAsync(Action<string> info, CancellationToken ct)
    {
        var verifier = B64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = B64Url(RandomNumberGenerator.GetBytes(16));
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{CallbackPort}/");
        listener.Start();

        info("Autorise l'application dans la page Kick qui s'ouvre…\n\n(en attente de ton autorisation)");
        OpenBrowser($"{OAuth}/authorize?client_id={Uri.EscapeDataString(Auth.ClientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(Scope)}&code_challenge={challenge}&code_challenge_method=S256&state={state}");

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
            if (q["error"] != null) throw new InvalidOperationException("Kick : " + q["error"]);
            if (!ok) continue; // favicon & co
            code = q["code"]!;
            break;
        }

        using var r = await Http.PostAsync(OAuth + "/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = Auth.ClientId, ["client_secret"] = Auth.ClientSecret,
            ["redirect_uri"] = RedirectUri, ["code_verifier"] = verifier,
        }), ct);
        Store(await ReadAsync("Kick", r, ct));
        var me = (await SendAsync(HttpMethod.Get, "/v1/channels", null, ct)).GetProperty("data")[0];
        Auth.UserId = me.GetProperty("broadcaster_user_id").GetRawText();
        Auth.UserName = me.GetProperty("slug").GetString() ?? "";
        Secrets.Save();
    }

    protected override async Task RefreshAsync(CancellationToken ct)
    {
        using var r = await Http.PostAsync(OAuth + "/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = Auth.Refresh, ["client_id"] = Auth.ClientId, ["client_secret"] = Auth.ClientSecret,
        }), ct);
        Store(await ReadAsync("Kick", r, ct));
    }

    async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, Api + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var r = await Http.SendAsync(req, ct);
        return await ReadAsync("Kick", r, ct);
    }

    public override async Task<LiveInfo> GetLiveAsync(CancellationToken ct)
    {
        var c = (await SendAsync(HttpMethod.Get, "/v1/channels", null, ct)).GetProperty("data")[0];
        return new LiveInfo
        {
            Title = c.GetProperty("stream_title").GetString() ?? "",
            GameName = c.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.Object ? cat.GetProperty("name").GetString() ?? "" : "",
            Tags = c.TryGetProperty("stream", out var s) && s.TryGetProperty("custom_tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new(),
        };
    }

    public override async Task<string?> SetLiveAsync(string? title, string? gameName, List<string>? tags, CancellationToken ct)
    {
        var body = new Dictionary<string, object>();
        string? warning = null;
        if (!string.IsNullOrEmpty(title)) body["stream_title"] = title;
        if (tags != null) body["custom_tags"] = tags;
        if (!string.IsNullOrWhiteSpace(gameName))
        {
            if (await ResolveGameAsync(gameName, ct) is { } id) body["category_id"] = id;
            else warning = $"jeu « {gameName} » introuvable sur Kick (non modifié)";
        }
        if (body.Count > 0) await SendAsync(new HttpMethod("PATCH"), "/v1/channels", body, ct);
        return warning;
    }

    static List<GameEntry> Games(JsonElement data) => data.EnumerateArray()
        .Select(g => new GameEntry { Name = g.GetProperty("name").GetString() ?? "", KickId = g.GetProperty("id").GetInt64() }).ToList();

    // the keyword search only exists in v1 (marked deprecated by Kick): the dictionary (v2 list) is the fallback
    public override async Task<List<GameEntry>> SearchGamesAsync(string query, CancellationToken ct) =>
        Games((await SendAsync(HttpMethod.Get, "/v1/categories?q=" + Uri.EscapeDataString(query), null, ct)).GetProperty("data"));

    public override async Task<List<GameEntry>> ListGamesAsync(CancellationToken ct)
    {
        var all = new List<GameEntry>();
        string cursor = "";
        for (int page = 0; page < 5; page++)
        {
            var r = await SendAsync(HttpMethod.Get, "/v2/categories?limit=1000" + (cursor == "" ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), null, ct);
            all.AddRange(Games(r.GetProperty("data")));
            cursor = r.TryGetProperty("pagination", out var p) && p.TryGetProperty("next_cursor", out var c) ? c.GetString() ?? "" : "";
            if (cursor == "") break;
        }
        return all;
    }
}

static class Streaming
{
    public static readonly Platform[] Platforms = { new TwitchPlatform(), new KickPlatform() };
    public static IEnumerable<Platform> Connected => Platforms.Where(p => p.Connected);

    /// <summary>tags the platforms accept: letters and digits only, 25 characters, 10 at most</summary>
    public static List<string> NormalizeTags(string text) => text.Split(new[] { ',', ';', '#', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(t => new string(t.Where(char.IsLetterOrDigit).ToArray()))
        .Where(t => t != "").Select(t => t.Length > 25 ? t[..25] : t)
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();

    /// <summary>same title, game and tags on every connected platform; null fields stay unchanged</summary>
    /// <returns>one line per platform (done, warning or error)</returns>
    public static async Task<List<(Platform P, string? Warning, Exception? Error)>> ApplyAsync(string? title, string? game, List<string>? tags, CancellationToken ct = default)
    {
        var results = new List<(Platform, string?, Exception?)>();
        foreach (var p in Connected)
        {
            try
            {
                results.Add((p, await p.SetLiveAsync(title, game, tags, ct), null));
                p.Mark(Health.Ok, $"{p.Name} : connexion opérationnelle ({p.UserName})");
            }
            catch (Exception e)
            {
                results.Add((p, null, e));
                p.Mark(Health.Error, $"{p.Name} : {e.Message}");
            }
        }
        return results;
    }

    /// <summary>remembers what the channel is currently set to (called when a live starts)</summary>
    public static async Task SnapshotAsync()
    {
        var p = Connected.FirstOrDefault();
        if (p == null) return;
        var info = await p.GetLiveAsync(CancellationToken.None);
        if (info.Title == "") return;
        StreamingStore.Current.AddHistory(new HistoryEntry { Title = info.Title, GameName = info.GameName, Tags = info.Tags, Source = p.Name });
    }

    /// <summary>games matching the text: the dictionary first, then the connected platforms (what they return joins the dictionary)</summary>
    public static async Task<List<GameEntry>> SearchGamesAsync(string text, CancellationToken ct)
    {
        var dict = GameDict.Current;
        if (text.Trim().Length >= 2)
        {
            foreach (var p in Connected)
                try { dict.Merge(await p.SearchGamesAsync(text, ct)); } catch (OperationCanceledException) { throw; } catch { }
            dict.Save();
        }
        return dict.Find(text);
    }

    /// <summary>re-reads the category lists of every connected platform into the dictionary</summary>
    public static async Task<int> RefreshGamesAsync(CancellationToken ct = default)
    {
        var dict = GameDict.Current;
        foreach (var p in Connected)
            try { dict.Merge(await p.ListGamesAsync(ct)); } catch (OperationCanceledException) { throw; } catch (Exception e) { RunLog.Write($"dictionnaire des jeux ({p.Name}) : {e.Message}"); }
        dict.Updated = DateTime.Now;
        dict.Save();
        return dict.Games.Count;
    }
}
