using System.Media;

/// <summary>Unified chat window: every connected platform in one stream; a click on a platform logo switches it off or on;
/// a message typed here is written on every platform that is switched on.</summary>
sealed class ChatWindow : Form
{
    readonly StreamingStore store = StreamingStore.Current;
    readonly ChatSource[] sources = { new TwitchChatSource(), new KickChatSource(), new YouTubeChatSource() };
    readonly ChatView view = new() { Dock = DockStyle.Fill };
    readonly TextBox input = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f), MaxLength = 500 };
    readonly Button send;
    readonly Label counter = new() { AutoSize = false, Width = 56, Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, ForeColor = Ui.Muted, Font = Ui.Small };
    readonly ToolTip tip = new() { InitialDelay = 300 };
    readonly List<ChatToggle> toggles = new();
    readonly System.Windows.Forms.Timer viewerTimer = new() { Interval = 60_000 }; // spectators: one look a minute
    readonly Panel searchBar = new() { Dock = DockStyle.Top, Height = 34, BackColor = ColorTranslator.FromHtml("#1B1A24"), Visible = false, Padding = new Padding(10, 5, 10, 5) };
    readonly TextBox searchBox = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f), PlaceholderText = "Chercher un pseudo ou un mot (@pseudo : ses messages)" };
    readonly ListBox suggest = new() { Visible = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26, BorderStyle = BorderStyle.FixedSingle, BackColor = ColorTranslator.FromHtml("#1F1E27"), ForeColor = Color.White, TabStop = false, Width = 260 };
    readonly Button emoteBtn = new() { Text = "☺", Dock = DockStyle.Right, Width = 34, FlatStyle = FlatStyle.Flat, ForeColor = Ui.Text, BackColor = Ui.Bg, Font = new Font("Segoe UI Symbol", 13f), Cursor = Cursors.Hand, TabStop = false };
    ChatMessage? replyTo; // the message the next one answers (sent on its platform only)
    readonly Panel replyBar = new() { Dock = DockStyle.Bottom, Height = 26, BackColor = ColorTranslator.FromHtml("#1B1A24"), Visible = false, Padding = new Padding(10, 0, 6, 0) };
    readonly Label replyText = new() { Dock = DockStyle.Fill, ForeColor = Ui.Muted, Font = Ui.Small, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    DateTime lastActivity = DateTime.MinValue; // last message that counted (for the "quiet moment" sound)

    public ChatWindow()
    {
        Text = "Chat unifié";
        Size = new Size(560, 720);
        MinimumSize = new Size(420, 360);
        StartPosition = FormStartPosition.CenterParent;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        // top bar: platform logos (click = switch off / on), settings and "always on top"
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Ui.Header, Padding = new Padding(12, 9, 8, 6), WrapContents = true };
        foreach (var s in sources)
        {
            var t = new ChatToggle(s.PlatformId) { On = !store.ChatOff.Contains(s.PlatformId), Margin = new Padding(0, 0, 10, 0) };
            var src = s;
            t.Click += (_, _) => SwitchPlatform(src, t);
            toggles.Add(t);
            top.Controls.Add(t);
        }
        var settings = Ui.Btn("Réglages", Ui.Kind.Secondary, (_, _) => ShowSettings());
        settings.Image = GearIcon.Make(settings.ForeColor);
        settings.TextImageRelation = TextImageRelation.ImageBeforeText;
        settings.Margin = new Padding(8, 2, 8, 0);
        top.Controls.Add(settings);
        var pin = new CheckBox { Text = "Au premier plan", AutoSize = true, ForeColor = Color.White, Font = Ui.Small, Margin = new Padding(4, 12, 0, 0) };
        pin.CheckedChanged += (_, _) => TopMost = pin.Checked;
        top.Controls.Add(pin);
        var find = Ui.Btn("🔍", Ui.Kind.Ghost, (_, _) => ToggleSearch());
        find.Margin = new Padding(0, 2, 0, 0);
        tip.SetToolTip(find, "Chercher dans le chat (Ctrl+F)");
        top.Controls.Add(find);
        Button live = null!;
        live = Ui.Btn("⚡ Live", Ui.Kind.Secondary, (_, _) => ShowLiveMenu(live));
        live.Margin = new Padding(8, 2, 0, 0);
        tip.SetToolTip(live, "Clip, marqueur, raid, shoutout, annonce");
        top.Controls.Add(live);

        // bottom bar: the message and "Envoyer"
        send = Ui.Btn("Envoyer", Ui.Kind.Primary, async (_, _) => await SendAll());
        send.Dock = DockStyle.Right;
        send.Margin = new Padding(0);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Ui.Bg, Padding = new Padding(10, 7, 10, 7) };
        bottom.Controls.Add(input);
        emoteBtn.FlatAppearance.BorderSize = 0;
        emoteBtn.Click += (_, _) => OpenPicker();
        tip.SetToolTip(emoteBtn, "Emotes");
        bottom.Controls.Add(emoteBtn);
        bottom.Controls.Add(counter);
        bottom.Controls.Add(send);

        var replyCancel = new Label { Text = "✕", Dock = DockStyle.Right, Width = 24, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
        replyCancel.Click += (_, _) => SetReply(null);
        replyBar.Controls.Add(replyText);
        replyBar.Controls.Add(replyCancel);

        Controls.Add(view);
        Controls.Add(replyBar);
        Controls.Add(bottom);
        searchBar.Controls.Add(searchBox);
        Controls.Add(searchBar);
        Controls.Add(top);
        searchBox.TextChanged += (_, _) => view.SetFilter(searchBox.Text);
        searchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; ToggleSearch(); } };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.F) { e.SuppressKeyPress = true; ToggleSearch(); } };
        Controls.Add(suggest);
        suggest.BringToFront();
        suggest.DrawItem += DrawSuggestion;
        suggest.MouseDown += (_, e) => { if (suggest.IndexFromPoint(e.Location) is >= 0 and var i) { suggest.SelectedIndex = i; AcceptSuggestion(); } };

        input.KeyDown += async (_, e) =>
        {
            if (suggest.Visible)
            {
                if (e.KeyCode is Keys.Down or Keys.Up) { e.SuppressKeyPress = true; suggest.SelectedIndex = Math.Clamp(suggest.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1), 0, suggest.Items.Count - 1); return; }
                if (e.KeyCode is Keys.Tab or Keys.Enter) { e.SuppressKeyPress = true; AcceptSuggestion(); return; }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; suggest.Visible = false; return; }
            }
            if (e.KeyCode == Keys.Escape && replyTo != null) { e.SuppressKeyPress = true; SetReply(null); }
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendAll(); }
        };
        input.TextChanged += (_, _) => { counter.Text = input.TextLength == 0 ? "" : $"{input.TextLength}/500"; UpdateSuggestions(); };
        input.PreviewKeyDown += (_, e) => { if (suggest.Visible && e.KeyCode == Keys.Tab) e.IsInputKey = true; };

        view.ShowBadges = ChatKinds.Shown("twitch", "badges");
        view.AuthorRightClicked += ShowAuthorMenu;
        foreach (var s in sources)
        {
            s.Message += m => OnUi(() => Append(m));
            s.StatusChanged += () => OnUi(RefreshToggles);
            if (!store.ChatOff.Contains(s.PlatformId)) s.Start();
        }
        RefreshToggles();
        LoadBadges();
        var tw = Streaming.Platforms.OfType<TwitchPlatform>().FirstOrDefault();
        _ = Emotes.LoadAsync(tw?.UserId, tw);
        viewerTimer.Tick += async (_, _) => await RefreshViewers();
        viewerTimer.Start();
        _ = RefreshViewers();
        FormClosed += (_, _) => viewerTimer.Dispose();
        Shown += (_, _) => input.Focus();
        FormClosed += (_, _) => { foreach (var s in sources) s.Dispose(); };
        AddSystem("Chat unifié : écris ici, le message part sur toutes les plateformes allumées. Clique sur un logo pour l'éteindre ; Réglages : son, utilisateurs exclus, ce qui s'affiche.");
    }

    // a ListBox repaints everything when its content changes: composited painting of the window (double buffer including children) removes the flicker
    // (not on the list itself: the handle of an owner-drawn ListBox cannot be created with this style)
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; /* WS_EX_COMPOSITED */ return cp; }
    }

    void OnUi(Action a)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(a);
    }

    /// <summary>Twitch badge images (urls from Helix) for the badges shown next to names</summary>
    void LoadBadges() => _ = Task.Run(async () =>
    {
        try
        {
            var tw = (TwitchPlatform)Streaming.Platforms.First(p => p.Id == "twitch");
            if (tw.Connected && tw.HasScope("chat:read")) ChatBadges.SetUrls(await tw.BadgeUrlsAsync(CancellationToken.None));
            else RunLog.Write("badges Twitch : compte non connecté ou autorisation chat:read absente (reconnecte le compte)");
        }
        catch (Exception e) { RunLog.Write("badges Twitch : " + e.Message); }
    });

    // ---- platforms on / off

    void SwitchPlatform(ChatSource s, ChatToggle t)
    {
        t.On = !t.On;
        if (t.On) { store.ChatOff.Remove(s.PlatformId); s.Start(); }
        else { if (!store.ChatOff.Contains(s.PlatformId)) store.ChatOff.Add(s.PlatformId); s.Stop(); }
        store.Save();
        RefreshToggles();
    }

    void RefreshToggles()
    {
        for (int i = 0; i < sources.Length; i++)
        {
            var s = sources[i];
            var t = toggles[i];
            var name = Streaming.Platforms.First(p => p.Id == s.PlatformId).Name;
            t.Dot = !t.On ? Color.Gray : s.Reading ? ColorTranslator.FromHtml("#2E9D5B") : s.Status.StartsWith("Erreur") || s.Status.Contains("Reconnecte") || s.Status.Contains("indisponible") ? ColorTranslator.FromHtml("#D64535") : ColorTranslator.FromHtml("#E0A030");
            tip.SetToolTip(t, $"{name} : " + (t.On ? s.Status : "désactivé (clique pour l'allumer)"));
            t.Invalidate();
        }
    }

    // ---- display

    void Append(ChatMessage m)
    {
        if (InvokeRequired) { OnUi(() => Append(m)); return; } // always written from the UI thread, one message at a time
        try
        {
            if (store.ChatOff.Contains(m.PlatformId) || !ChatKinds.Shown(m.PlatformId, m.Kind)) return;
            view.AddMessage(m);
            if (m.Emotes != null) foreach (var sp in m.Emotes) if (sp.Start + sp.Length <= m.Text.Length) Emotes.Seen(m.Text.Substring(sp.Start, sp.Length), sp.Url, m.PlatformId);
            if (!m.Mine && !m.System) MaybePlaySound(m);
        }
        catch (Exception e) { RunLog.Write("chat : affichage impossible : " + e); }
    }

    void AddSystem(string text) => view.AddMessage(new ChatMessage(DateTime.Now, "system", "Stream Launcher", text));

    // ---- typed moderation commands: /ban name, /unban name, /timeout name [seconds], /untimeout name, /clear, /mod, /unmod, /vip, /unvip

    static readonly Dictionary<string, (ModKind Kind, bool NeedsUser, string Done)> Commands = new()
    {
        ["ban"] = (ModKind.Ban, true, "banni"), ["unban"] = (ModKind.Unban, true, "débanni"), ["untimeout"] = (ModKind.Unban, true, "n'est plus exclu"),
        ["timeout"] = (ModKind.Timeout, true, "exclu"), ["clear"] = (ModKind.Clear, false, "chat vidé"),
        ["mod"] = (ModKind.Mod, true, "est modérateur"), ["unmod"] = (ModKind.Unmod, true, "n'est plus modérateur"),
        ["vip"] = (ModKind.Vip, true, "est VIP"), ["unvip"] = (ModKind.Unvip, true, "n'est plus VIP"),
    };

    /// <returns>true when the text was a moderation command (then it is not sent as a message)</returns>
    async Task<bool> TryCommandAsync(string text)
    {
        if (!text.StartsWith('/')) return false;
        var parts = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Commands.TryGetValue(parts[0].ToLowerInvariant(), out var cmd)) return false; // /me and the like go through as text
        if (cmd.NeedsUser && parts.Length < 2) { AddSystem($"/{parts[0]} : il faut un pseudo (exemple : /{parts[0]} pseudo)."); return true; }
        int seconds = cmd.Kind == ModKind.Timeout && parts.Length > 2 && int.TryParse(parts[2], out var sec) ? sec : 600;
        var login = cmd.NeedsUser ? parts[1].TrimStart('@') : "";
        foreach (var src in sources.Where(s => !store.ChatOff.Contains(s.PlatformId)))
        {
            var name = PlatformName(src.PlatformId);
            try
            {
                string? id = null;
                if (cmd.NeedsUser)
                {
                    if (src.PlatformId == "youtube") { AddSystem($"{name} : les commandes avec un pseudo ne sont pas disponibles (utilise le clic droit sur son message)."); continue; }
                    id = await src.UserIdAsync(login, CancellationToken.None);
                    if (id == null) { AddSystem($"{name} : pseudo « {login} » introuvable."); continue; }
                }
                var target = new ChatMessage(DateTime.Now, src.PlatformId, login, "", UserId: id);
                if (src.ModerationBlocked(cmd.Kind, target) is { } why) { AddSystem($"{name} : impossible ({why})."); continue; }
                await src.ModerateAsync(cmd.Kind, target, seconds, CancellationToken.None);
                AddSystem($"{name} : {(cmd.NeedsUser ? login + " " : "")}{cmd.Done}");
            }
            catch (Exception e) { AddSystem($"{name} : {e.Message}"); }
        }
        return true;
    }

    // ---- emotes: suggestions while typing a word, and a picker

    List<Emotes.Info> suggested = new();

    void UpdateSuggestions()
    {
        int caret = input.SelectionStart;
        var before = input.Text[..Math.Min(caret, input.TextLength)];
        var word = before[(before.LastIndexOf(' ') + 1)..];
        if (word.Length < 2 || word.StartsWith('/') || word.StartsWith('@')) { suggest.Visible = false; return; }
        suggested = Emotes.All.Where(i => i.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase) && i.Name != word).Take(8).ToList();
        if (suggested.Count == 0) { suggest.Visible = false; return; }
        suggest.Items.Clear();
        foreach (var i in suggested) suggest.Items.Add(i.Name);
        suggest.SelectedIndex = 0;
        suggest.Height = suggested.Count * suggest.ItemHeight + 4;
        var anchor = replyBar.Visible ? replyBar : input.Parent!;
        suggest.Location = new Point(10, anchor.Top - suggest.Height);
        suggest.Visible = true;
        suggest.BringToFront();
    }

    void DrawSuggestion(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= suggested.Count) return;
        e.DrawBackground();
        var info = suggested[e.Index];
        var img = ChatBadges.GetUrl(info.Url);
        if (img != null) e.Graphics.DrawImage(img, new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, 20, 20));
        TextRenderer.DrawText(e.Graphics, info.Name, e.Font, new Point(e.Bounds.X + 30, e.Bounds.Y + 4), e.ForeColor);
    }

    void AcceptSuggestion()
    {
        if (suggest.SelectedIndex < 0 || suggest.SelectedIndex >= suggested.Count) return;
        int caret = input.SelectionStart;
        var before = input.Text[..caret];
        int start = before.LastIndexOf(' ') + 1;
        input.Text = input.Text[..start] + suggested[suggest.SelectedIndex].Name + " " + input.Text[caret..];
        input.SelectionStart = start + suggested[suggest.SelectedIndex].Name.Length + 1;
        suggest.Visible = false;
        input.Focus();
    }

    void OpenPicker()
    {
        var p = new EmotePicker();
        var at = input.PointToScreen(new Point(0, 0));
        p.Location = new Point(Math.Max(0, at.X), Math.Max(0, at.Y - p.Height - 4));
        p.Picked += name =>
        {
            int caret = input.SelectionStart;
            input.Text = input.Text[..caret] + name + " " + input.Text[caret..];
            input.SelectionStart = caret + name.Length + 1;
        };
        p.FormClosed += (_, _) => input.Focus();
        p.Show(this);
    }

    void ToggleSearch(string? text = null)
    {
        bool show = text != null || !searchBar.Visible;
        searchBar.Visible = show;
        if (show) { searchBox.Text = text ?? searchBox.Text; searchBox.Focus(); searchBox.SelectAll(); }
        else { searchBox.Clear(); view.SetFilter(""); input.Focus(); }
    }

    // ---- spectators next to each logo

    async Task RefreshViewers()
    {
        for (int i = 0; i < sources.Length; i++)
        {
            var p = Streaming.Platforms.First(x => x.Id == sources[i].PlatformId);
            int? n = null;
            if (p.Connected && !store.ChatOff.Contains(p.Id))
                try { n = await p.ViewersAsync(CancellationToken.None); }
                catch (Exception e) { RunLog.Write($"spectateurs {p.Name} : {e.Message}"); }
            if (IsDisposed) return;
            toggles[i].Viewers = n;
            toggles[i].Invalidate();
        }
    }

    // ---- live actions (Twitch: the APIs of Kick and YouTube do not offer them)

    void ShowLiveMenu(Control anchor)
    {
        var tw = Streaming.Platforms.OfType<TwitchPlatform>().First();
        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        ToolStripMenuItem Item(string text, string scope, Func<Task> run)
        {
            var blocked = !tw.Connected ? "compte Twitch non connecté" : tw.HasScope(scope) ? null : "reconnecte Twitch (nouveau droit)";
            var item = new ToolStripMenuItem(blocked == null ? text : text + " (indisponible)") { Enabled = blocked == null, ToolTipText = blocked };
            item.Click += async (_, _) =>
            {
                try { await run(); }
                catch (Exception e) { AddSystem($"Twitch : {e.Message}"); }
            };
            return item;
        }
        menu.Items.Add(new ToolStripLabel("Twitch seulement") { ForeColor = Ui.Muted });
        menu.Items.Add(Item("Créer un clip", "clips:edit", async () =>
        {
            var url = await tw.ClipAsync(CancellationToken.None);
            try { Clipboard.SetText(url); } catch { }
            AddSystem($"Twitch : clip créé (adresse copiée) : {url}");
        }));
        menu.Items.Add(Item("Poser un marqueur…", "channel:manage:broadcast", async () =>
        {
            if (PromptDialog.Ask(this, "Marqueur", "Description (facultative) :") is not { } text) return;
            await tw.MarkerAsync(text, CancellationToken.None);
            AddSystem("Twitch : marqueur posé dans le live.");
        }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Raid vers…", "channel:manage:raids", async () =>
        {
            if (PromptDialog.Ask(this, "Raid", "Pseudo de la chaîne à raider :") is not { Length: > 0 } login) return;
            var id = await tw.UserIdByLoginAsync(login, CancellationToken.None) ?? throw new InvalidOperationException($"pseudo « {login} » introuvable");
            if (MessageBox.Show(this, $"Lancer un raid vers {login} ?", "Raid", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await tw.RaidAsync(id, CancellationToken.None);
            AddSystem($"Twitch : raid vers {login} lancé (il part dans environ 90 secondes).");
        }));
        menu.Items.Add(Item("Annuler le raid", "channel:manage:raids", async () =>
        {
            await tw.CancelRaidAsync(CancellationToken.None);
            AddSystem("Twitch : raid annulé.");
        }));
        menu.Items.Add(Item("Shoutout vers…", "moderator:manage:shoutouts", async () =>
        {
            if (PromptDialog.Ask(this, "Shoutout", "Pseudo de la chaîne à mettre en avant :") is not { Length: > 0 } login) return;
            var id = await tw.UserIdByLoginAsync(login, CancellationToken.None) ?? throw new InvalidOperationException($"pseudo « {login} » introuvable");
            await tw.ShoutoutAsync(id, CancellationToken.None);
            AddSystem($"Twitch : shoutout envoyé à {login}.");
        }));
        var ann = new ToolStripMenuItem("Annonce");
        foreach (var (label, color) in new[] { ("Couleur de la chaîne", "primary"), ("Bleue", "blue"), ("Verte", "green"), ("Orange", "orange"), ("Violette", "purple") })
            ann.DropDownItems.Add(Item(label, "moderator:manage:announcements", async () =>
            {
                if (PromptDialog.Ask(this, "Annonce", "Texte de l'annonce (500 caractères) :") is not { Length: > 0 } text) return;
                await tw.AnnounceAsync(text, color, CancellationToken.None);
                AddSystem("Twitch : annonce envoyée.");
            }));
        menu.Items.Add(ann);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    void SetReply(ChatMessage? m)
    {
        replyTo = m;
        replyBar.Visible = m != null;
        if (m != null) replyText.Text = $"↪ Réponse à {m.Author} sur {PlatformName(m.PlatformId)} : {m.Text}";
        input.Focus();
    }

    // ---- right click on a name: copy it, and moderate (timeout with the usual durations, ban, unban, delete the message)

    static readonly (string Label, int Seconds)[] Timeouts =
    {
        ("1 minute", 60), ("5 minutes", 300), ("10 minutes", 600), ("30 minutes", 1800), ("1 heure", 3600),
        ("6 heures", 21600), ("1 jour", 86400), ("1 semaine", 604800), ("2 semaines", 1209600),
    };

    // what was changed from this window wins over the badges of older messages
    readonly Dictionary<string, bool> roles = new();

    bool IsRole(ChatMessage m, string role)
    {
        if (roles.TryGetValue($"{m.PlatformId}:{m.UserId}:{role}", out var known)) return known;
        return m.Badges?.Any(b => role == "mod" ? b.StartsWith("moderator/") || b.StartsWith("lead_moderator/") || b == "yt-moderator" : b.StartsWith("vip/")) == true; // Twitch lead moderators carry their own badge
    }

    void ShowAuthorMenu(ChatMessage m, Point at)
    {
        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        menu.Items.Add($"Copier le pseudo ({m.Author})", null, (_, _) => { try { Clipboard.SetText(m.Author); } catch (Exception e) { RunLog.Write("presse-papier : " + e.Message); } });
        menu.Items.Add($"Voir ses messages", null, (_, _) => ToggleSearch("@" + m.Author));
        var src = sources.FirstOrDefault(s => s.PlatformId == m.PlatformId);
        if (src != null && !m.Mine && m.Kind != "automod") menu.Items.Insert(0, new ToolStripMenuItem("Répondre", null, (_, _) => SetReply(m)));
        if (src != null && !m.Mine)
        {
            menu.Items.Add(new ToolStripSeparator());
            bool modNow = IsRole(m, "mod");
            bool held = m.Kind == "automod";
            ToolStripMenuItem Action(string text, ModKind kind, int seconds, string done, bool confirm = false)
            {
                var blocked = src.ModerationBlocked(kind, m);
                if (blocked == null && m.PlatformId == "twitch" && modNow && kind is ModKind.Timeout or ModKind.Ban) blocked = "Twitch refuse d'exclure un modérateur : retire d'abord son rôle de modérateur";
                var item = new ToolStripMenuItem(blocked == null ? text : text + " (indisponible)") { Enabled = blocked == null, ToolTipText = blocked };
                item.Click += async (_, _) =>
                {
                    if (confirm && MessageBox.Show(this, $"{text} {m.Author} sur {PlatformName(m.PlatformId)} ?", "Modération", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    try
                    {
                        await src.ModerateAsync(kind, m, seconds, CancellationToken.None);
                        if (kind is ModKind.Mod or ModKind.Unmod) roles[$"{m.PlatformId}:{m.UserId}:mod"] = kind == ModKind.Mod;
                        if (kind is ModKind.Vip or ModKind.Unvip) roles[$"{m.PlatformId}:{m.UserId}:vip"] = kind == ModKind.Vip;
                        AddSystem($"{PlatformName(m.PlatformId)} : {m.Author} {done}");
                    }
                    catch (Exception e)
                    {
                        RunLog.Write($"modération {kind} {m.Author} : {e.Message}");
                        AddSystem($"Modération impossible ({PlatformName(m.PlatformId)}, {m.Author}) : {e.Message}");
                    }
                };
                return item;
            }
            if (held)
            {
                menu.Items.Add(Action("Autoriser le message", ModKind.Allow, 0, "message autorisé"));
                menu.Items.Add(Action("Refuser le message", ModKind.Deny, 0, "message refusé"));
                menu.Show(view, at);
                return;
            }
            menu.Items.Add(Action("Supprimer le message", ModKind.Delete, 0, "message supprimé"));
            var timeout = new ToolStripMenuItem("Exclure temporairement");
            foreach (var (label, seconds) in Timeouts)
            {
                if (m.PlatformId == "kick" && seconds > 604800) continue; // Kick stops at 7 days
                timeout.DropDownItems.Add(Action(label, ModKind.Timeout, seconds, $"exclu pour {label}"));
            }
            if (timeout.DropDownItems.Cast<ToolStripMenuItem>().All(i => !i.Enabled)) { timeout.Enabled = false; timeout.ToolTipText = src.ModerationBlocked(ModKind.Timeout, m); }
            menu.Items.Add(timeout);
            menu.Items.Add(Action("Bannir définitivement", ModKind.Ban, 0, "banni", confirm: true));
            menu.Items.Add(Action("Débannir", ModKind.Unban, 0, "débanni"));
            menu.Items.Add(new ToolStripSeparator());
            bool isMod = modNow, isVip = IsRole(m, "vip");
            if (m.Badges?.Contains("broadcaster/1") != true) // the streamer is neither promoted nor demoted
            {
                menu.Items.Add(isMod ? Action(m.Badges?.Any(b => b.StartsWith("lead_moderator/")) == true ? "Retirer modérateur principal" : "Retirer modérateur", ModKind.Unmod, 0, "n'est plus modérateur") : Action("Nommer modérateur", ModKind.Mod, 0, "est modérateur"));
                if (isVip) menu.Items.Add(Action("Retirer VIP", ModKind.Unvip, 0, "n'est plus VIP"));
                else if (!isMod) menu.Items.Add(Action("Nommer VIP", ModKind.Vip, 0, "est VIP")); // a moderator cannot be VIP
            }
        }
        menu.Show(view, at);
    }

    static string PlatformName(string id) => id switch { "twitch" => "Twitch", "kick" => "Kick", "youtube" => "YouTube", _ => id };

    // ---- sound: one sound when a message arrives after a quiet moment (users in the exclusion list never count)

    void MaybePlaySound(ChatMessage m)
    {
        if (store.ChatExcluded.Any(u => u.Trim().Equals(m.Author, StringComparison.OrdinalIgnoreCase))) return;
        var now = DateTime.Now;
        if (store.ChatSoundOn && (now - lastActivity).TotalSeconds >= Math.Max(1, store.ChatSoundDelay)) ChatSound.Play(store.ChatSoundFile);
        lastActivity = now;
    }

    void ShowSettings()
    {
        using var d = new ChatSettingsDialog();
        if (d.ShowDialog(this) != DialogResult.OK) return;
        view.ShowBadges = ChatKinds.Shown("twitch", "badges");
        view.Invalidate();
    }

    // ---- sending: the same message on every platform that is switched on

    async Task SendAll()
    {
        var text = input.Text.Trim();
        if (text == "") return;
        if (await TryCommandAsync(text)) { input.Clear(); return; }
        var parent = replyTo; // an answer goes to the platform of the message it answers
        var targets = sources.Where(s => !store.ChatOff.Contains(s.PlatformId) && (parent == null || s.PlatformId == parent.PlatformId)).ToList();
        if (targets.Count == 0) { AddSystem("Aucune plateforme allumée : clique sur un logo pour en allumer une."); return; }
        send.Enabled = false;
        input.Enabled = false;
        try
        {
            var results = await Task.WhenAll(targets.Select(async s =>
            {
                try
                {
                    if (parent != null) await s.SendReplyAsync(text, parent, CancellationToken.None);
                    else await s.SendAsync(text, CancellationToken.None);
                    return (s, (string?)null);
                }
                catch (Exception e) { return (s, e.Message); }
            }));
            var ok = results.Where(r => r.Item2 == null).ToList();
            if (ok.Count > 0)
            {
                var names = ok.Select(r => Streaming.Platforms.First(p => p.Id == r.s.PlatformId).Name);
                Append(new ChatMessage(DateTime.Now, ok[0].s.PlatformId, "Moi (" + string.Join(" + ", names) + ")", text, null, Mine: true, ReplyName: parent?.Author, ReplyBody: parent?.Text));
                input.Clear();
                SetReply(null);
            }
            foreach (var (s, err) in results.Where(r => r.Item2 != null))
                Append(new ChatMessage(DateTime.Now, s.PlatformId, "Envoi impossible", err!, null, System: true));
            if (targets.Any(s => s.PlatformId == "youtube") && text.Length > 200) AddSystem("YouTube : message raccourci à 200 caractères.");
        }
        finally
        {
            send.Enabled = true;
            input.Enabled = true;
            input.Focus();
        }
    }

    /// <summary>text of the list, for tests</summary>
    public string Dump() => view.DumpText();
}

/// <summary>The sound played by the chat: the one shipped with the application, or a .wav chosen by the user.</summary>
static class ChatSound
{
    public static int PlayCount; // sounds started since launch (tests)

    public static void Play(string customFile)
    {
        Interlocked.Increment(ref PlayCount);
        _ = Task.Run(() => PlayNow(customFile));
    }

    static void PlayNow(string customFile)
    {
        try
        {
            if (customFile != "" && File.Exists(customFile)) { using var p = new SoundPlayer(customFile); p.PlaySync(); return; }
            using var s = typeof(ChatSound).Assembly.GetManifestResourceStream("chat-default.wav");
            if (s != null) { using var p = new SoundPlayer(s); p.PlaySync(); }
        }
        catch (Exception e) { RunLog.Write("son du chat : " + e.Message); }
    }
}

/// <summary>Chat settings: the sound (delay, file, excluded users) and what is displayed besides the text, per platform.</summary>
sealed class ChatSettingsDialog : Form
{
    readonly StreamingStore store = StreamingStore.Current;
    readonly CheckBox soundOn = new() { Text = "Jouer un son quand un message arrive après un moment de silence", AutoSize = true };
    readonly NumericUpDown delay = new() { Minimum = 1, Maximum = 3600, Width = 70 };
    readonly TextBox file = new() { ReadOnly = true, Width = 300 };
    readonly TextBox excluded = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 440, Height = 90 };
    readonly Dictionary<string, CheckBox> kinds = new();

    public ChatSettingsDialog()
    {
        Text = "Réglages du chat";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 460);
        Font = Ui.Base;

        var tabs = new TabControl { Left = 8, Top = 8, Width = 484, Height = 400 };

        // -- sound
        var snd = new TabPage("Son") { Padding = new Padding(10) };
        soundOn.Checked = store.ChatSoundOn;
        delay.Value = Math.Clamp(store.ChatSoundDelay, 1, 3600);
        file.Text = store.ChatSoundFile;
        var browse = Ui.Btn("Parcourir…", Ui.Kind.Secondary, (_, _) =>
        {
            using var o = new OpenFileDialog { Filter = "Son WAV|*.wav", Title = "Son du chat" };
            if (o.ShowDialog(this) == DialogResult.OK) file.Text = o.FileName;
        });
        var reset = Ui.Btn("Son par défaut", Ui.Kind.Ghost, (_, _) => file.Text = "");
        var test = Ui.Btn("▶ Tester", Ui.Kind.Secondary, (_, _) => ChatSound.Play(file.Text));
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var delayRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        delayRow.Controls.AddRange(new Control[] { new Label { Text = "Silence minimum (secondes) :", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, delay });
        var fileRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        fileRow.Controls.AddRange(new Control[] { file, browse });
        var btnRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        btnRow.Controls.AddRange(new Control[] { test, reset });
        excluded.Text = string.Join(Environment.NewLine, store.ChatExcluded);
        flow.Controls.Add(soundOn);
        flow.Controls.Add(delayRow);
        flow.Controls.Add(new Label { Text = "Le son joué par défaut est celui de l'application ; choisis un fichier .wav pour le remplacer (laisse vide pour le défaut).", AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(0, 10, 0, 2) });
        flow.Controls.Add(fileRow);
        flow.Controls.Add(btnRow);
        flow.Controls.Add(new Label { Text = "Utilisateurs exclus (un par ligne : bots, comptes dont les messages ne doivent pas déclencher le son) :", AutoSize = true, MaximumSize = new Size(440, 0), Font = Ui.Bold, Margin = new Padding(0, 14, 0, 2) });
        flow.Controls.Add(excluded);
        snd.Controls.Add(flow);
        tabs.TabPages.Add(snd);

        // -- what is displayed, per platform
        foreach (var p in Streaming.Platforms)
        {
            var page = new TabPage(p.Name) { Padding = new Padding(10) };
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            list.Controls.Add(new Label { Text = "Ce qui s'affiche en plus des messages (événements et alertes : décochés par défaut) :", AutoSize = true, Font = Ui.Bold, Margin = new Padding(0, 0, 0, 6) });
            foreach (var (kind, label) in ChatKinds.ByPlatform[p.Id])
            {
                var cb = new CheckBox { Text = label, AutoSize = true, Checked = ChatKinds.Shown(p.Id, kind), Margin = new Padding(0, 2, 0, 2) };
                kinds[p.Id + ":" + kind] = cb;
                list.Controls.Add(cb);
            }
            list.Controls.Add(new Label
            {
                Text = p.Id switch
                {
                    "twitch" => "Récompenses, followers, hype train, sondages, prédictions et AutoMod demandent des droits Twitch : bouton « Reconnecter » dans Infos du live.",
                    "kick" => "Les événements Kick arrivent par les webhooks officiels de Kick, via le relais Cloudflare.",
                    _ => "Les Super Chats et autres événements dépendent de ta diffusion en cours.",
                },
                AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(0, 12, 0, 0),
            });
            page.Controls.Add(list);
            tabs.TabPages.Add(page);
        }
        Controls.Add(tabs);

        var ok = Ui.Btn("Enregistrer", Ui.Kind.Primary, (_, _) => { Save(); DialogResult = DialogResult.OK; });
        ok.Left = 16; ok.Top = 416;
        var cancel = Ui.Btn("Annuler", Ui.Kind.Secondary, (_, _) => DialogResult = DialogResult.Cancel);
        cancel.Left = 140; cancel.Top = 416;
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    void Save()
    {
        store.ChatSoundOn = soundOn.Checked;
        store.ChatSoundDelay = (int)delay.Value;
        store.ChatSoundFile = file.Text;
        store.ChatExcluded = excluded.Text.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool IsEvent(string key) => ChatKinds.EventKinds.Contains(key[(key.IndexOf(':') + 1)..]);
        store.ChatHidden = kinds.Where(k => !k.Value.Checked && !IsEvent(k.Key)).Select(k => k.Key).ToList();
        store.ChatShown = kinds.Where(k => k.Value.Checked && IsEvent(k.Key)).Select(k => k.Key).ToList();
        store.Save();
    }
}

/// <summary>Platform logo used as an on/off button: coloured badge with a status dot; greyed out when off.</summary>
sealed class ChatToggle : Control
{
    readonly string id;
    public bool On { get; set; } = true;
    public Color Dot { get; set; } = Color.Gray;
    /// <summary>people watching (shown right of the logo), null when not live</summary>
    public int? Viewers { get; set; }

    static string Short(int n) => n >= 10_000 ? $"{n / 1000}k" : n >= 1000 ? $"{n / 1000.0:0.0}k" : n.ToString();

    public ChatToggle(string id)
    {
        this.id = id;
        Size = new Size(78, 36);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PlatformLogos.Draw(g, id, new Rectangle(2, 2, 30, 30), On);
        if (!On) using (var pen = new Pen(ColorTranslator.FromHtml("#D64535"), 2)) g.DrawLine(pen, 5, 29, 29, 5);
        using (var ring = new SolidBrush(Ui.Header)) g.FillEllipse(ring, 25, 24, 14, 14);
        using (var dot = new SolidBrush(Dot)) g.FillEllipse(dot, 27, 26, 10, 10);
        using var f = new Font("Segoe UI", 9f, FontStyle.Bold);
        TextRenderer.DrawText(g, Viewers == null ? "" : Short(Viewers.Value), f, new Rectangle(42, 0, 36, 32), On ? Color.White : Ui.Muted, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Small question box: one line of text.</summary>
sealed class PromptDialog : Form
{
    readonly TextBox box = new() { Dock = DockStyle.Top, Font = new Font("Segoe UI", 10.5f) };

    PromptDialog(string title, string label)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(400, 110);
        var lbl = new Label { Text = label, Dock = DockStyle.Top, Height = 28, Font = Ui.Bold, Padding = new Padding(0, 4, 0, 0) };
        var ok = Ui.Btn("OK", Ui.Kind.Primary, (_, _) => { DialogResult = DialogResult.OK; });
        var cancel = Ui.Btn("Annuler", Ui.Kind.Secondary, (_, _) => { DialogResult = DialogResult.Cancel; });
        var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
        row.Controls.Add(cancel);
        row.Controls.Add(ok);
        Padding = new Padding(12);
        Controls.Add(box);
        Controls.Add(lbl);
        Controls.Add(row);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <returns>the text (possibly empty), null when cancelled</returns>
    public static string? Ask(IWin32Window owner, string title, string label)
    {
        using var d = new PromptDialog(title, label);
        return d.ShowDialog(owner) == DialogResult.OK ? d.box.Text.Trim() : null;
    }
}
