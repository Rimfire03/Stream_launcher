/// <summary>Recommended tags: the ones already used (history, favourites; most frequent first), then a built-in list.</summary>
static class TagDictionary
{
    static readonly string[] Builtin =
    {
        "Francais", "French", "English", "FR", "Chill", "Fun", "Detente", "Gaming", "Gamer", "Live", "Stream", "Streamer", "SmallStreamer", "Communaute", "Community",
        "BonneAmbiance", "Bienveillant", "Famille", "Humour", "Rire", "Decouverte", "Debutant", "Casual", "Competitif", "Ranked", "Tryhard", "Speedrun", "Challenge",
        "Coop", "Multijoueur", "Multiplayer", "Solo", "Histoire", "Aventure", "Action", "RPG", "FPS", "TPS", "MMO", "MOBA", "BattleRoyale", "Strategie", "Simulation",
        "Survie", "Horreur", "Puzzle", "Plateforme", "Course", "Sport", "Combat", "Indie", "Retro", "Sandbox", "Construction", "Gestion", "Roguelike", "OpenWorld",
        "Platine", "Hardcore", "Blind", "PremierePartie", "Gameplay", "Tutoriel", "Astuces", "Guide", "Test", "Review", "Nouveaute", "Sortie", "Event", "Tournoi",
        "JustChatting", "Discussion", "Musique", "Music", "Dessin", "Art", "Creation", "Cuisine", "IRL", "Podcast", "Variete", "Rediffusion", "NoSpoil", "Vtuber",
        "Mods", "Modding", "PC", "Console", "Xbox", "PlayStation", "Nintendo", "Switch", "Steam", "VR", "Minecraft", "Fortnite", "Valorant", "LeagueOfLegends",
        "CounterStrike", "GTA", "Zelda", "Pokemon", "EldenRing", "Skyrim", "Cyberpunk", "RocketLeague", "Apex", "Warzone", "Overwatch", "Rust", "Lethal", "Palworld",
    };

    public static List<string> Suggest(string typed, IEnumerable<string> already, int max = 8)
    {
        var used = StreamingStore.Current.History.SelectMany(h => h.Tags).Concat(StreamingStore.Current.Favorites.SelectMany(f => f.Tags))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key);
        var taken = new HashSet<string>(already, StringComparer.OrdinalIgnoreCase);
        var all = used.Concat(Builtin).Distinct(StringComparer.OrdinalIgnoreCase).Where(t => !taken.Contains(t)).ToList();
        if (typed == "") return all.Take(max).ToList();
        return all.Where(t => t.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Concat(all.Where(t => t.Contains(typed, StringComparison.OrdinalIgnoreCase) && !t.StartsWith(typed, StringComparison.OrdinalIgnoreCase)))
            .Take(max).ToList();
    }
}

/// <summary>Tags as small blocks, each removable with its ×; a text field with suggestions adds new ones (Enter, comma, space or Tab).</summary>
sealed class TagEditor : FlowLayoutPanel
{
    const int MaxTags = 10;
    readonly List<string> tags = new();
    readonly TextBox input = new() { BorderStyle = BorderStyle.None, Font = Ui.Base, Width = 150, Margin = new Padding(4, 7, 0, 0), BackColor = Color.White };
    readonly ListBox list = new() { BorderStyle = BorderStyle.None, Font = Ui.Base, IntegralHeight = true, BackColor = Color.White };
    readonly ToolStripDropDown popup = new() { AutoClose = false, Padding = Padding.Empty, Margin = Padding.Empty, DropShadowEnabled = true };

    public TagEditor()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = true;
        FlowDirection = FlowDirection.LeftToRight;
        BackColor = Color.White;
        MinimumSize = new Size(0, 34);
        Padding = new Padding(4, 3, 4, 3);
        Controls.Add(input);
        popup.Items.Add(new ToolStripControlHost(list) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false });
        input.TextChanged += (_, _) => OnTyped();
        input.KeyDown += InputKeyDown;
        input.LostFocus += (_, _) => { Commit(); popup.Close(); };
        list.MouseDown += (_, e) => { int i = list.IndexFromPoint(e.Location); if (i >= 0) { Add((string)list.Items[i]!); input.Focus(); } };
        Click += (_, _) => input.Focus();
        Paint += (_, e) => { using var p = new Pen(Ui.Border); e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); };
        Resize += (_, _) => input.Width = Math.Max(80, Math.Min(220, Width - 20));
    }

    public override string Text
    {
        get => string.Join(", ", tags);
        set { tags.Clear(); tags.AddRange(Streaming.NormalizeTags(value ?? "")); Rebuild(); }
    }

    public List<string> Tags => tags.ToList();

    void Rebuild()
    {
        SuspendLayout();
        foreach (var c in Controls.OfType<Chip>().ToList()) { Controls.Remove(c); c.Dispose(); }
        for (int i = 0; i < tags.Count; i++)
        {
            var t = tags[i];
            var chip = new Chip(t) { Margin = new Padding(2, 3, 2, 3) };
            chip.Removed += () => { tags.Remove(t); Rebuild(); input.Focus(); };
            Controls.Add(chip);
            Controls.SetChildIndex(chip, i);
        }
        input.Visible = tags.Count < MaxTags;
        ResumeLayout(true);
    }

    void Add(string raw)
    {
        foreach (var t in Streaming.NormalizeTags(raw))
            if (tags.Count < MaxTags && !tags.Contains(t, StringComparer.OrdinalIgnoreCase)) tags.Add(t);
        input.Text = "";
        popup.Close();
        Rebuild();
    }

    void Commit() { if (input.Text.Trim() != "") Add(input.Text); }

    void OnTyped()
    {
        var s = input.Text;
        int cut = s.IndexOfAny(new[] { ',', ';', ' ', '\n' });
        if (cut >= 0) { var head = s[..cut]; var rest = s[(cut + 1)..]; input.Text = ""; if (head.Trim() != "") Add(head); input.Text = rest; input.SelectionStart = rest.Length; return; }
        ShowSuggestions(s.Trim().TrimStart('#'));
    }

    void ShowSuggestions(string typed)
    {
        if (!input.Focused) return;
        var found = TagDictionary.Suggest(typed, tags);
        if (typed == "" || found.Count == 0) { popup.Close(); return; }
        list.Items.Clear();
        list.Items.AddRange(found.Cast<object>().ToArray());
        list.SelectedIndex = 0;
        list.Size = new Size(Math.Max(160, input.Width + 30), list.ItemHeight * found.Count + 2);
        popup.Size = list.Size + new Size(2, 2);
        popup.Show(input, new Point(0, input.Height + 2));
    }

    void InputKeyDown(object? s, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && !popup.Visible) { ShowSuggestions(input.Text.Trim()); if (!popup.Visible) { list.Items.Clear(); list.Items.AddRange(TagDictionary.Suggest("", tags).Cast<object>().ToArray()); if (list.Items.Count > 0) { list.SelectedIndex = 0; list.Size = new Size(190, list.ItemHeight * list.Items.Count + 2); popup.Size = list.Size + new Size(2, 2); popup.Show(input, new Point(0, input.Height + 2)); } } e.Handled = true; }
        else if (e.KeyCode == Keys.Down && popup.Visible) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Up && popup.Visible) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
        else if (e.KeyCode is Keys.Enter or Keys.Tab && (popup.Visible || input.Text.Trim() != "")) { Add(popup.Visible && list.SelectedItem != null ? (string)list.SelectedItem : input.Text); e.Handled = e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Escape && popup.Visible) { popup.Close(); e.Handled = e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Back && input.Text == "" && tags.Count > 0) { tags.RemoveAt(tags.Count - 1); Rebuild(); e.Handled = true; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) popup.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>one tag block: rounded, with a × that removes it</summary>
    sealed class Chip : Control
    {
        readonly string text;
        bool overX;
        Rectangle xRect;
        public event Action? Removed;

        public Chip(string t)
        {
            text = t;
            Font = Ui.Base;
            Cursor = Cursors.Default;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            var w = TextRenderer.MeasureText(t, Font, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding).Width;
            Size = new Size(w + 36, 26);
            xRect = new Rectangle(Width - 22, 4, 18, 18);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.White);
            using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), 8)) using (var br = new SolidBrush(Ui.Selected)) g.FillPath(br, path);
            TextRenderer.DrawText(g, text, Font, new Rectangle(10, 0, Width - 30, Height), Ui.Primary, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if (overX) using (var br = new SolidBrush(Color.FromArgb(40, Ui.Danger))) g.FillEllipse(br, xRect);
            using var pen = new Pen(overX ? Ui.Danger : Ui.Muted, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            var c = new PointF(xRect.X + xRect.Width / 2f, xRect.Y + xRect.Height / 2f);
            g.DrawLine(pen, c.X - 3.5f, c.Y - 3.5f, c.X + 3.5f, c.Y + 3.5f);
            g.DrawLine(pen, c.X - 3.5f, c.Y + 3.5f, c.X + 3.5f, c.Y - 3.5f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool o = xRect.Contains(e.Location);
            if (o != overX) { overX = o; Cursor = o ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { overX = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (xRect.Contains(e.Location)) Removed?.Invoke();
            base.OnMouseUp(e);
        }
    }
}
