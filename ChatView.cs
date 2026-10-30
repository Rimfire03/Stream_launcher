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

    /// <returns>the badge image, or null while it is being downloaded (the view is repainted when it arrives)</returns>
    public static Image? Get(string key)
    {
        if (Cache.TryGetValue(key, out var img)) return img;
        if (urls.TryGetValue(key, out var url) && Loading.TryAdd(key, true))
            _ = Task.Run(async () =>
            {
                try
                {
                    using var ms = new MemoryStream(await Web.GetByteArrayAsync(url));
                    Cache[key] = new Bitmap(ms);
                }
                catch { Cache[key] = null; }
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
        ChatBadges.Loaded += () => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { Remeasure(); Invalidate(); }); };
    }

    public bool ShowBadges { get; set; } = true;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Remeasure(); // row heights depend on the width
    }

    void Remeasure()
    {
        for (int i = 0; i < Items.Count; i++) { var o = Items[i]; Items[i] = o; }
    }

    // ---- layout: the prefix (time, logo, badges, name, label) is drawn by hand; the text gets leading spaces so that its first line starts after it

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

    string Indented(ChatMessage m, int prefixWidth)
    {
        int space = Math.Max(1, TextRenderer.MeasureText("a a", body, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding).Width - TextRenderer.MeasureText("aa", body, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding).Width);
        return new string(' ', (prefixWidth - Pad) / space + 1) + m.Text;
    }

    protected override void OnMeasureItem(MeasureItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var m = (ChatMessage)Items[e.Index];
        var (px, _) = Prefix(m);
        var size = TextRenderer.MeasureText(Indented(m, px), body, new Size(Math.Max(100, ClientSize.Width - Pad * 2), 0), Flags);
        e.ItemHeight = Math.Max(Logo + 8, size.Height + 6);
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

        TextRenderer.DrawText(g, m.Time.ToString("HH:mm"), small, new Rectangle(r.X + Pad, r.Y + 4, 38, Logo), Ui.Muted, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        PlatformLogos.Draw(g, m.PlatformId, new Rectangle(r.X + 46, r.Y + 3, Logo, Logo));
        var (px, badges) = Prefix(m);
        int x = r.X + 46 + Logo + 4;
        foreach (var b in badges) { if (b != null) g.DrawImage(b, new Rectangle(x, r.Y + 3, Logo, Logo)); x += Logo + 2; }
        var nameColor = m.Mine ? ColorTranslator.FromHtml("#B79BFF") : ParseColor(m.Color);
        var nameSize = TextRenderer.MeasureText(m.Author, bold, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, m.Author, bold, new Rectangle(x, r.Y + 3, nameSize.Width + 2, Logo + 2), nameColor, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        x += nameSize.Width + 6;
        if (m.Extra != null)
        {
            var exSize = TextRenderer.MeasureText(m.Extra, bold, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, m.Extra, bold, new Rectangle(x, r.Y + 3, exSize.Width + 2, Logo + 2), accent == Color.Transparent ? ForeColor : accent, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        }
        TextRenderer.DrawText(g, Indented(m, px), body, new Rectangle(r.X + Pad, r.Y + 3, r.Width - Pad * 2, r.Height - 3), m.System ? ColorTranslator.FromHtml("#E0A030") : ForeColor, Flags);
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
        int i = Items.Add(m);
        Items[i] = m; // re-assigning forces the measure of the new row (it stays at the default height otherwise)
        while (Items.Count > MaxItems) Items.RemoveAt(0);
        if (follow) ScrollToEnd();
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
