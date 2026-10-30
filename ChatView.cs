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

/// <summary>The chat itself: one owner-drawn row per message with time, platform logo, badges, name, then the text (wrapped).</summary>
sealed class ChatView : ListBox
{
    const int Pad = 8, Logo = 16, MaxItems = 600;
    readonly Font body = new("Segoe UI", 10f), bold = new("Segoe UI", 10f, FontStyle.Bold), small = new("Segoe UI", 8.5f);
    static readonly TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top;

    public ChatView()
    {
        DrawMode = DrawMode.OwnerDrawVariable;
        IntegralHeight = false;
        BorderStyle = BorderStyle.None;
        BackColor = ColorTranslator.FromHtml("#0E0E10");
        ForeColor = ColorTranslator.FromHtml("#EFEFF1");
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        ChatBadges.Loaded += () =>
        {
            if (!IsHandleCreated || IsDisposed || Interlocked.Exchange(ref pending, 1) == 1) return; // several images often arrive together: one pass
            BeginInvoke(() => { Interlocked.Exchange(ref pending, 0); Frozen(Remeasure); });
        };
    }

    int pending;

    [System.Runtime.InteropServices.DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    const int WM_SETREDRAW = 0x000B;

    /// <summary>runs a change of the list with the painting frozen, then repaints once</summary>
    void Frozen(Action change)
    {
        if (!IsHandleCreated) { change(); return; }
        SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try { change(); }
        finally { SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero); Invalidate(); }
    }

    public bool ShowBadges { get; set; } = true;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Frozen(Remeasure); // row heights depend on the width
    }

    void Remeasure()
    {
        for (int i = 0; i < Items.Count; i++) { var o = Items[i]; Items[i] = o; }
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
    sealed record Piece(string? Word, Image? Image, int X, int Line, int W, int H);

    /// <returns>the pieces, the height of each line, and the row height</returns>
    (List<Piece> Pieces, List<int> Lines, int Height) Flow(ChatMessage m, int prefixWidth, int width)
    {
        Metrics();
        int left = Pad, right = Math.Max(left + 60, width - Pad);
        var pieces = new List<Piece>();
        var lines = new List<int> { bodyH };
        int line = 0, x = prefixWidth;

        void NewLine() { line++; lines.Add(bodyH); x = left; }
        void Place(string? word, Image? img, int w, int h)
        {
            if (x + w > right && x > (line == 0 ? prefixWidth : left)) NewLine();
            pieces.Add(new Piece(word, img, x, line, w, h));
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

        int height = 6 + lines.Sum(l => l + 1);
        return (pieces, lines, Math.Max(Logo + 8, height));
    }

    protected override void OnMeasureItem(MeasureItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var m = (ChatMessage)Items[e.Index];
        var (px, _) = Prefix(m);
        e.ItemHeight = Flow(m, px, ClientSize.Width).Height;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var m = (ChatMessage)Items[e.Index];
        var g = e.Graphics;
        var r = e.Bounds;
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
        var (pieces, lines, _) = Flow(m, px, ClientSize.Width);
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
            else if (p.Image != null) g.DrawImage(p.Image, new Rectangle(r.X + p.X, py + 1, p.W, Emote));
        }
    }

    static Color ParseColor(string? html)
    {
        try { return html == null ? ColorTranslator.FromHtml("#9AA0A6") : ColorTranslator.FromHtml(html); } catch { return ColorTranslator.FromHtml("#9AA0A6"); }
    }

    // ---- content

    bool AtBottom()
    {
        if (Items.Count == 0) return true;
        var last = GetItemRectangle(Items.Count - 1);
        return last.Bottom <= ClientSize.Height + 40;
    }

    public void AddMessage(ChatMessage m)
    {
        bool follow = AtBottom();
        Frozen(() =>
        {
            int i = Items.Add(m);
            Items[i] = m; // re-assigning forces the measure of the new row (it stays at the default height otherwise)
            while (Items.Count > MaxItems) Items.RemoveAt(0);
            if (follow) ScrollToEnd();
        });
    }

    public void ScrollToEnd()
    {
        if (Items.Count == 0) return;
        int top = Items.Count - 1, used = GetItemHeight(top);
        while (top > 0 && used + GetItemHeight(top - 1) <= ClientSize.Height) { top--; used += GetItemHeight(top); }
        TopIndex = top;
    }

    public void ClearMessages() => Items.Clear();

    /// <summary>the messages in the list, for tests</summary>
    public string DumpText() => string.Join(Environment.NewLine, Items.Cast<ChatMessage>().Select(m => $"{m.Time:HH:mm} [{m.PlatformId}] {m.Author}{(m.Extra != null ? " <" + m.Extra + ">" : "")}{(m.Badges != null ? " {" + string.Join(",", m.Badges) + "}" : "")}: {m.Text}"));
}
