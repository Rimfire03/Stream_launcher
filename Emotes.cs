using System.Text.Json;
using System.Collections.Concurrent;

/// <summary>Emote names for the unified chat: 7TV, BetterTTV and FrankerFaceZ emotes of the streamer's Twitch channel (plain words for Twitch, drawn as images here),
/// plus every emote name seen in the chat (offered while typing).</summary>
static class Emotes
{
    public sealed record Info(string Name, string Url, bool Animated, string Source);

    static readonly HttpClient Web = new() { Timeout = TimeSpan.FromSeconds(15), DefaultRequestHeaders = { { "User-Agent", "StreamLauncher" } } };
    static readonly ConcurrentDictionary<string, Info> thirdParty = new();
    static readonly ConcurrentDictionary<string, Info> known = new();
    static int loaded;

    public static bool TryThirdParty(string word, out Info info) => thirdParty.TryGetValue(word, out info!);

    /// <summary>every emote name that can be typed, third party first</summary>
    public static IEnumerable<Info> All => thirdParty.Values.Concat(known.Values).GroupBy(i => i.Name).Select(g => g.First()).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);

    public static void Seen(string name, string url, string source) => known.TryAdd(name, new Info(name, url, false, source));

    /// <summary>Kick wants "[emote:id:name]" in a message: the names known from Kick chat are turned into that</summary>
    public static string ToKick(string text) => string.Join(' ', text.Split(' ').Select(w =>
        known.TryGetValue(w, out var i) && i.Source == "kick" && KickId.Match(i.Url) is { Success: true } m ? $"[emote:{m.Groups[1].Value}:{w}]" : w));

    static readonly System.Text.RegularExpressions.Regex KickId = new(@"/emotes/(\d+)/", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>downloads the lists once (global ones, and those of the channel when the Twitch account is known)</summary>
    public static async Task LoadAsync(string? twitchUserId, TwitchPlatform? twitch)
    {
        if (Interlocked.Exchange(ref loaded, 1) == 1) return;
        var jobs = new List<Task>
        {
            Guard("7TV global", () => SevenTv("https://7tv.io/v3/emote-sets/global", false)),
            Guard("BTTV global", () => Bttv("https://api.betterttv.net/3/cached/emotes/global", false)),
            Guard("FFZ global", Ffz("https://api.frankerfacez.com/v1/set/global")),
        };
        if (!string.IsNullOrEmpty(twitchUserId))
        {
            jobs.Add(Guard("7TV chaîne", () => SevenTv($"https://7tv.io/v3/users/twitch/{twitchUserId}", true)));
            jobs.Add(Guard("BTTV chaîne", () => Bttv($"https://api.betterttv.net/3/cached/users/twitch/{twitchUserId}", true)));
            jobs.Add(Guard("FFZ chaîne", Ffz($"https://api.frankerfacez.com/v1/room/id/{twitchUserId}")));
        }
        if (twitch is { Connected: true })
            jobs.Add(Guard("emotes Twitch", async () =>
            {
                foreach (var (name, url) in await twitch.EmotesAsync(CancellationToken.None)) known.TryAdd(name, new Info(name, url, false, "twitch"));
            }));
        await Task.WhenAll(jobs);
        ChatBadges.RaiseLoaded(); // the chat measures its rows again with the new names
    }

    static Task Guard(string what, Func<Task> job) => Run(what, job);

    static async Task Run(string what, Func<Task> job)
    {
        try { await job(); }
        catch (Exception e) { RunLog.Write($"emotes {what} : {e.Message}"); }
    }

    static async Task<JsonElement> Get(string url)
    {
        using var doc = JsonDocument.Parse(await Web.GetStringAsync(url));
        return doc.RootElement.Clone();
    }

    static void Add(string name, string url, bool animated, string source)
    {
        var info = new Info(name, url, animated, source);
        thirdParty[name] = info;
    }

    static async Task SevenTv(string url, bool user)
    {
        var root = await Get(url);
        var set = user ? root.GetProperty("emote_set") : root;
        if (!set.TryGetProperty("emotes", out var list) || list.ValueKind != JsonValueKind.Array) return;
        foreach (var e in list.EnumerateArray())
        {
            var data = e.GetProperty("data");
            bool animated = data.TryGetProperty("animated", out var a) && a.ValueKind == JsonValueKind.True;
            var host = data.GetProperty("host").GetProperty("url").GetString() ?? "";
            Add(e.GetProperty("name").GetString() ?? "", $"https:{host}/2x.{(animated ? "gif" : "png")}", animated, "7tv"); // WebP is not readable by GDI+
        }
    }

    static async Task Bttv(string url, bool user)
    {
        var root = await Get(url);
        IEnumerable<JsonElement> all = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray()
            : new[] { "channelEmotes", "sharedEmotes" }.Where(n => root.TryGetProperty(n, out _)).SelectMany(n => root.GetProperty(n).EnumerateArray());
        foreach (var e in all)
        {
            bool gif = e.TryGetProperty("imageType", out var t) && t.GetString() == "gif";
            Add(e.GetProperty("code").GetString() ?? "", $"https://cdn.betterttv.net/emote/{e.GetProperty("id").GetString()}/2x.{(gif ? "gif" : "png")}", gif, "bttv");
        }
    }

    static Func<Task> Ffz(string url) => async () =>
    {
        var root = await Get(url);
        foreach (var set in root.GetProperty("sets").EnumerateObject())
            foreach (var e in set.Value.GetProperty("emoticons").EnumerateArray())
            {
                var urls = e.GetProperty("urls");
                var u = urls.TryGetProperty("2", out var two) ? two.GetString() : urls.EnumerateObject().First().Value.GetString();
                if (u != null) Add(e.GetProperty("name").GetString() ?? "", u.StartsWith("//") ? "https:" + u : u, false, "ffz");
            }
    };
}

/// <summary>Small window with the known emotes (search box on top); a click gives the name back.</summary>
sealed class EmotePicker : Form
{
    public event Action<string>? Picked;
    readonly FlowLayoutPanel grid = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ColorTranslator.FromHtml("#18181B"), Padding = new Padding(4) };
    readonly TextBox search = new() { Dock = DockStyle.Top, Font = new Font("Segoe UI", 10f), PlaceholderText = "Chercher une emote…" };
    readonly ToolTip tip = new();

    public EmotePicker()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        Size = new Size(372, 300);
        BackColor = ColorTranslator.FromHtml("#18181B");
        Controls.Add(grid);
        Controls.Add(search);
        search.TextChanged += (_, _) => Fill();
        Deactivate += (_, _) => Close();
        Shown += (_, _) => { search.Focus(); Fill(); };
        int pending = 0;
        void refresh() // images that were still downloading: fill the empty frames (the grid itself is not rebuilt)
        {
            if (!IsHandleCreated || IsDisposed || Interlocked.Exchange(ref pending, 1) == 1) return;
            BeginInvoke(() =>
            {
                Interlocked.Exchange(ref pending, 0);
                foreach (PictureBox pb in grid.Controls) if (pb.Image == null && pb.Tag is string url) pb.Image = ChatBadges.GetUrl(url);
            });
        }
        ChatBadges.Loaded += refresh;
        FormClosed += (_, _) => ChatBadges.Loaded -= refresh;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    void Fill()
    {
        var q = search.Text.Trim();
        grid.SuspendLayout();
        foreach (Control c in grid.Controls) c.Dispose();
        grid.Controls.Clear();
        foreach (var info in Emotes.All.Where(i => q == "" || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(300))
        {
            var pb = new PictureBox { Width = 34, Height = 34, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(2), Cursor = Cursors.Hand, Image = ChatBadges.GetUrl(info.Url), Tag = info.Url };
            tip.SetToolTip(pb, info.Name);
            var name = info.Name;
            pb.Click += (_, _) => { Picked?.Invoke(name); Close(); };
            grid.Controls.Add(pb);
        }
        grid.ResumeLayout();
    }
}
