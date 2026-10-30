using System.Text.RegularExpressions;
using System.Collections.Concurrent;

/// <summary>Draws the platform logo (same drawing for the on/off buttons and for each chat line).</summary>
static class PlatformLogos
{
    public static Color ColorOf(string id) => id switch
    {
        "twitch" => ColorTranslator.FromHtml("#9146FF"),
        "kick" => ColorTranslator.FromHtml("#53FC18"),
        "youtube" => ColorTranslator.FromHtml("#FF3B30"),
        _ => Color.Gray,
    };

    public static void Draw(Graphics g, string id, Rectangle r, bool on = true)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var back = on ? ColorOf(id) : ColorTranslator.FromHtml("#4B4A57");
        var glyph = on ? (id == "kick" ? Color.Black : Color.White) : ColorTranslator.FromHtml("#8A8799");
        using (var path = Ui.Round(r, Math.Max(3, r.Width / 4))) using (var br = new SolidBrush(back)) g.FillPath(br, path);
        using var gb = new SolidBrush(glyph);
        if (id == "youtube")
        {
            float w = r.Width, h = r.Height;
            g.FillPolygon(gb, new[] { new PointF(r.X + w * 0.38f, r.Y + h * 0.28f), new PointF(r.X + w * 0.38f, r.Y + h * 0.72f), new PointF(r.X + w * 0.75f, r.Y + h * 0.5f) });
        }
        else
        {
            using var f = new Font("Segoe UI", r.Width * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, id == "twitch" ? "T" : id == "kick" ? "K" : "i", f, r, glyph, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>A drawn cog in the Android / Material "settings" style (chunky body, 8 flared teeth, big round hole), as a bitmap.</summary>
static class GearIcon
{
    public static Bitmap Make(Color color, int size = 18)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float c = size / 2f, outer = size * 0.47f, inner = size * 0.36f;
        var pts = new List<PointF>();
        for (int i = 0; i < 8; i++) // each tooth: wide at the body, narrower at the tip
            foreach (var (a, r) in new[] { (-0.19, inner), (-0.11, outer), (0.11, outer), (0.19, inner) })
            {
                double ang = (i + a) * Math.PI / 4;
                pts.Add(new PointF(c + (float)Math.Cos(ang) * r, c + (float)Math.Sin(ang) * r));
            }
        using var br = new SolidBrush(color);
        using var pen = new Pen(color, size * 0.06f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        g.FillPolygon(br, pts.ToArray());
        g.DrawPolygon(pen, pts.ToArray()); // rounds the corners of the teeth
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        using var hole = new SolidBrush(Color.Transparent);
        g.FillEllipse(hole, c - size * 0.17f, c - size * 0.17f, size * 0.34f, size * 0.34f);
        return bmp;
    }
}

/// <summary>Twitch badge images (broadcaster, moderator, subscriber months...): urls from Helix, bitmaps downloaded once and cached.</summary>
static class ChatBadges
{
    static readonly HttpClient Web = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly ConcurrentDictionary<string, Image?> Cache = new();
    static readonly ConcurrentDictionary<string, bool> Loading = new();
    static Dictionary<string, string> urls = new();
    public static event Action? Loaded;
    public static void RaiseLoaded() => Loaded?.Invoke();

    public static void SetUrls(Dictionary<string, string> map) { urls = map; Loaded?.Invoke(); }

    public static bool Known(string key) => urls.ContainsKey(key);

    /// <summary>true when the image could not be downloaded (so the caller falls back to text)</summary>
    public static bool Failed(string url) => Cache.TryGetValue(url, out var i) && i == null;

    /// <returns>the badge image, or null while it is being downloaded (the view is repainted when it arrives)</returns>
    public static Image? Get(string key) => urls.TryGetValue(key, out var url) ? GetUrl(url) : null;

    /// <returns>the image at this url (badge or emote), or null while it downloads or when it failed (see <see cref="Failed"/>)</returns>
    public static Image? GetUrl(string url)
    {
        if (Cache.TryGetValue(url, out var img)) return img;
        if (Loading.TryAdd(url, true))
            _ = Task.Run(async () =>
            {
                try
                {
                    using var ms = new MemoryStream(await Web.GetByteArrayAsync(url));
                    using var src = Image.FromStream(ms);
                    Cache[url] = new Bitmap(src); // a copy: a bitmap read from a closed stream fails when drawn
                }
                catch (Exception e) { RunLog.Write($"image {url} : {e.Message}"); Cache[url] = null; }
                Loaded?.Invoke();
            });
        return null;
    }
}

/// <summary>Animated GIFs linked in messages (direct .gif, Giphy and Tenor pages): resolved to an image url, downloaded once, kept animated.</summary>
static class ChatGifs
{
    static readonly HttpClient Web = new() { Timeout = TimeSpan.FromSeconds(15), DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamLauncher" } } };
    sealed record Gif(Image Image, MemoryStream Stream); // GDI+ needs the stream to stay open as long as the image lives
    static readonly ConcurrentDictionary<string, Gif?> Cache = new();
    static readonly ConcurrentDictionary<string, bool> Loading = new();
    const int MaxBytes = 12_000_000;

    static readonly Regex Direct = new(@"https?://\S+?\.gif(?:\?\S*)?(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Giphy = new(@"https?://(?:www\.)?giphy\.com/(?:gifs|stickers)/(?:[^\s/?]*-)?(?<id>[A-Za-z0-9]+)(?=[/?\s]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TenorPage = new(@"https?://(?:www\.)?tenor\.com/\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TenorMedia = new(@"https://(?:media|c)\d*\.tenor\.com/[^""'\s<>]+?\.gif", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <returns>the link of the GIF in the text (an image, or a Giphy/Tenor page), or null</returns>
    public static string? Find(string text)
    {
        var m = Direct.Match(text);
        if (m.Success) return m.Value;
        m = Giphy.Match(text);
        if (m.Success) return $"https://i.giphy.com/media/{m.Groups["id"].Value}/giphy.gif";
        m = TenorPage.Match(text);
        return m.Success ? m.Value : null;
    }

    public static bool Failed(string url) => Cache.TryGetValue(url, out var g) && g == null;

    /// <returns>the animated image, or null while it downloads (the view is told through <see cref="ChatBadges.Loaded"/>) or when it failed</returns>
    public static Image? Get(string url)
    {
        if (Cache.TryGetValue(url, out var g)) return g?.Image;
        if (Loading.TryAdd(url, true))
            _ = Task.Run(async () =>
            {
                try
                {
                    var src = url;
                    if (TenorPage.IsMatch(url) && !url.Contains("media.tenor.com", StringComparison.OrdinalIgnoreCase)) // page: its html names the image
                    {
                        var mm = TenorMedia.Match(await Web.GetStringAsync(url));
                        if (!mm.Success) throw new InvalidOperationException("pas d'image GIF dans la page");
                        src = mm.Value;
                    }
                    var bytes = await Web.GetByteArrayAsync(src);
                    if (bytes.Length > MaxBytes) throw new InvalidOperationException("GIF trop gros");
                    var ms = new MemoryStream(bytes);
                    Cache[url] = new Gif(Image.FromStream(ms), ms);
                }
                catch (Exception e) { RunLog.Write($"gif {url} : {e.Message}"); Cache[url] = null; }
                ChatBadges.RaiseLoaded();
            });
        return null;
    }
}

/// <summary>The chat itself: one row per message with time, platform logo, badges, name, then the text (wrapped). Drawn by hand in one double-buffered pass (a ListBox repaints the whole list and flickers).</summary>
sealed class ChatView : Control
{
    const int Pad = 8, Logo = 16, MaxItems = 600;
    readonly Font body = new("Segoe UI", 10f), bold = new("Segoe UI", 10f, FontStyle.Bold), small = new("Segoe UI", 8.5f);

    readonly List<ChatMessage> items = new();
    readonly List<int> heights = new();
    readonly VScrollBar bar = new() { Dock = DockStyle.Right, SmallChange = 30 };
    int total, offset; // height of all rows, scroll position in pixels

    public ChatView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = ColorTranslator.FromHtml("#0E0E10");
        ForeColor = ColorTranslator.FromHtml("#EFEFF1");
        Controls.Add(bar);
        bar.Scroll += (_, e) => { offset = Math.Clamp(e.NewValue, 0, MaxOffset); Invalidate(); };
        ChatBadges.Loaded += () =>
        {
            if (!IsHandleCreated || IsDisposed || Interlocked.Exchange(ref pending, 1) == 1) return; // several images often arrive together: one pass
            BeginInvoke(() => { Interlocked.Exchange(ref pending, 0); Remeasure(); });
        };
    }

    int pending;

    public bool ShowBadges { get; set; } = true;

    int Wide => Math.Max(80, ClientSize.Width - bar.Width);
    int MaxOffset => Math.Max(0, total - ClientSize.Height);
    bool AtBottom() => offset >= MaxOffset - 40;

    int RowHeight(ChatMessage m) => Flow(m, Prefix(m).Width, Wide).Height;

    void Sync()
    {
        int page = Math.Max(1, ClientSize.Height);
        offset = Math.Clamp(offset, 0, MaxOffset);
        bar.LargeChange = page;
        bar.Maximum = Math.Max(total, page) - 1;
        bar.Value = Math.Min(offset, Math.Max(0, bar.Maximum - page + 1));
        bar.Enabled = MaxOffset > 0;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Remeasure(); // row heights depend on the width
    }

    void Remeasure()
    {
        bool follow = AtBottom();
        total = 0;
        for (int i = 0; i < items.Count; i++) { heights[i] = RowHeight(items[i]); total += heights[i]; }
        if (follow) offset = MaxOffset;
        Sync();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        offset = Math.Clamp(offset - e.Delta / 120 * 60, 0, MaxOffset);
        Sync();
    }

    // ---- layout: the prefix (time, logo, badges, name, label) is drawn by hand, then the text is flowed word by word and emote by emote

    const int Emote = 24;
    int spaceW, bodyH;

    void Metrics()
    {
        if (spaceW > 0) return;
        spaceW = Math.Max(3, TextRenderer.MeasureText("a a", body, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding).Width - TextRenderer.MeasureText("aa", body, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding).Width);
        bodyH = TextRenderer.MeasureText("Ag", body, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding).Height;
    }

    (int Width, List<Image?> Badges) Prefix(ChatMessage m)
    {
        int x = 46 + Logo + 4;
        var imgs = new List<Image?>();
        if (ShowBadges && m.Badges != null)
            foreach (var b in m.Badges) if (ChatBadges.Known(b)) { imgs.Add(ChatBadges.Get(b)); x += Logo + 2; } // room is kept while the image downloads
        x += TextRenderer.MeasureText(m.Author, bold, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding).Width + 6;
        if (m.Extra != null) x += TextRenderer.MeasureText(m.Extra, bold, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding).Width + 8;
        return (x, imgs);
    }

    /// <summary>one word or one emote, placed in a row (x, and the line it sits on)</summary>
    sealed record Piece(string? Word, Image? Image, int X, int Line, int W, int H, bool Gif = false);

    /// <returns>the pieces, the height of each line, and the row height</returns>
    (List<Piece> Pieces, List<int> Lines, int Height) Flow(ChatMessage m, int prefixWidth, int width)
    {
        Metrics();
        int left = Pad, right = Math.Max(left + 60, width - Pad);
        var pieces = new List<Piece>();
        var lines = new List<int> { bodyH };
        int line = 0, x = prefixWidth;

        void NewLine() { line++; lines.Add(bodyH); x = left; }
        void Place(string? word, Image? img, int w, int h, bool gif = false)
        {
            if (x + w > right && x > (line == 0 ? prefixWidth : left)) NewLine();
            pieces.Add(new Piece(word, img, x, line, w, h, gif));
            lines[line] = Math.Max(lines[line], h);
            x += w + spaceW;
        }
        int Measure(string s) => TextRenderer.MeasureText(s, body, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        void Word(string word)
        {
            int w = Measure(word);
            if (w <= right - left) { Place(word, null, w, bodyH); return; }
            // longer than a whole line: cut it
            int from = 0;
            while (from < word.Length)
            {
                int n = word.Length - from;
                while (n > 1 && Measure(word.Substring(from, n)) > right - Math.Max(x, left)) n--;
                var part = word.Substring(from, n);
                Place(part, null, Measure(part), bodyH);
                from += n;
                if (from < word.Length) NewLine();
            }
        }
        void Words(string s) { foreach (var w in s.Split(' ', StringSplitOptions.RemoveEmptyEntries)) Word(w); }

        var spans = m.Emotes != null && ChatKinds.Shown(m.PlatformId, "emotes") ? m.Emotes : null;
        int pos = 0;
        if (spans != null)
            foreach (var sp in spans)
            {
                if (sp.Start < pos || sp.Start + sp.Length > m.Text.Length) continue;
                Words(m.Text[pos..sp.Start]);
                var img = ChatBadges.GetUrl(sp.Url);
                if (img == null && ChatBadges.Failed(sp.Url)) Words(m.Text.Substring(sp.Start, sp.Length)); // no image: show the name
                else Place(null, img, img == null ? Emote : Math.Max(8, (int)Math.Round(Emote * (double)img.Width / img.Height)), Emote + 2);
                pos = sp.Start + sp.Length;
            }
        Words(m.Text[pos..]);

        // a GIF link: the animation is shown under the text (the link stays readable)
        if (m.Kind == "gif" && ChatGifs.Find(m.Text) is { } gifUrl && ChatGifs.Get(gifUrl) is { } gif)
        {
            double s = Math.Min(1.0, Math.Min(Math.Min(280.0, right - left) / gif.Width, 200.0 / gif.Height));
            if (x > (line == 0 ? prefixWidth : left)) NewLine();
            Place(null, gif, Math.Max(8, (int)(gif.Width * s)), Math.Max(8, (int)(gif.Height * s)), gif: true);
        }

        int height = 6 + lines.Sum(l => l + 1);
        return (pieces, lines, Math.Max(Logo + 8, height));
    }

    // ---- animated GIFs: ImageAnimator moves the frames on, the view repaints while one is on screen
    readonly HashSet<Image> animated = new();
    bool gifVisible;
    int framePending;

    void Animate(Image img)
    {
        if (!ImageAnimator.CanAnimate(img) || !animated.Add(img)) return;
        ImageAnimator.Animate(img, (_, _) =>
        {
            if (!gifVisible || !IsHandleCreated || IsDisposed || Interlocked.Exchange(ref framePending, 1) == 1) return;
            BeginInvoke(() => { Interlocked.Exchange(ref framePending, 0); ImageAnimator.UpdateFrames(); Invalidate(); });
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        gifVisible = false;
        int y = -offset;
        for (int i = 0; i < items.Count && y < ClientSize.Height; i++)
        {
            if (y + heights[i] > 0) DrawRow(g, items[i], new Rectangle(0, y, Wide, heights[i]));
            y += heights[i];
        }
    }

    void DrawRow(Graphics g, ChatMessage m, Rectangle r)
    {
        bool special = m.Kind != "message" || m.System;
        var accent = m.System ? ColorTranslator.FromHtml("#D64535") : m.Kind switch
        {
            "message" => Color.Transparent, "gif" => ColorTranslator.FromHtml("#3B6FD4"),
            "highlight" => ColorTranslator.FromHtml("#9146FF"), "bits" or "superchat" or "sticker" => ColorTranslator.FromHtml("#E0A030"),
            "reward" => ColorTranslator.FromHtml("#2EB8A6"), "raid" => ColorTranslator.FromHtml("#D9822B"), _ => ColorTranslator.FromHtml("#E0508A"),
        };
        using (var bg = new SolidBrush(special ? Color.FromArgb(40, accent) : m.Mine ? ColorTranslator.FromHtml("#17141F") : BackColor)) g.FillRectangle(bg, r);
        if (special) using (var bar = new SolidBrush(accent)) g.FillRectangle(bar, r.X, r.Y, 3, r.Height);

        var (px, badges) = Prefix(m);
        var (pieces, lines, _) = Flow(m, px, r.Width);
        int top = r.Y + 3 + (lines[0] - bodyH) / 2; // the prefix is centred on the first line when an emote makes it taller

        TextRenderer.DrawText(g, m.Time.ToString("HH:mm"), small, new Rectangle(r.X + Pad, top + 1, 38, Logo), Ui.Muted, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        PlatformLogos.Draw(g, m.PlatformId, new Rectangle(r.X + 46, top, Logo, Logo));
        int x = r.X + 46 + Logo + 4;
        foreach (var b in badges) { if (b != null) g.DrawImage(b, new Rectangle(x, top, Logo, Logo)); x += Logo + 2; }
        var nameColor = m.Mine ? ColorTranslator.FromHtml("#B79BFF") : ParseColor(m.Color);
        var nameSize = TextRenderer.MeasureText(m.Author, bold, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, m.Author, bold, new Rectangle(x, top, nameSize.Width + 2, Logo + 2), nameColor, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        x += nameSize.Width + 6;
        if (m.Extra != null)
        {
            var exSize = TextRenderer.MeasureText(m.Extra, bold, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, m.Extra, bold, new Rectangle(x, top, exSize.Width + 2, Logo + 2), accent == Color.Transparent ? ForeColor : accent, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        }

        var color = m.System ? ColorTranslator.FromHtml("#E0A030") : ForeColor;
        var lineTop = new int[lines.Count];
        int y = r.Y + 3;
        for (int i = 0; i < lines.Count; i++) { lineTop[i] = y; y += lines[i] + 1; }
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        foreach (var p in pieces)
        {
            int py = lineTop[p.Line] + (lines[p.Line] - p.H) / 2;
            if (p.Word != null) TextRenderer.DrawText(g, p.Word, body, new Point(r.X + p.X, py), color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            else if (p.Image != null && p.Gif) { Animate(p.Image); g.DrawImage(p.Image, new Rectangle(r.X + p.X, py, p.W, p.H)); gifVisible = true; }
            else if (p.Image != null) g.DrawImage(p.Image, new Rectangle(r.X + p.X, py + 1, p.W, Emote));
        }
    }

    static Color ParseColor(string? html)
    {
        try { return html == null ? ColorTranslator.FromHtml("#9AA0A6") : ColorTranslator.FromHtml(html); } catch { return ColorTranslator.FromHtml("#9AA0A6"); }
    }

    // ---- content

    public void AddMessage(ChatMessage m)
    {
        bool follow = AtBottom();
        int h = RowHeight(m);
        items.Add(m); heights.Add(h); total += h;
        while (items.Count > MaxItems) { total -= heights[0]; offset -= heights[0]; items.RemoveAt(0); heights.RemoveAt(0); }
        if (follow) offset = MaxOffset;
        Sync();
    }

    public void ScrollToEnd() { offset = MaxOffset; Sync(); }

    public void ClearMessages() { items.Clear(); heights.Clear(); total = offset = 0; Sync(); }

    /// <summary>the messages in the list, for tests</summary>
    public string DumpText() => string.Join(Environment.NewLine, items.Select(m => $"{m.Time:HH:mm} [{m.PlatformId}] {m.Author}{(m.Extra != null ? " <" + m.Extra + ">" : "")}{(m.Badges != null ? " {" + string.Join(",", m.Badges) + "}" : "")}: {m.Text}"));
}
