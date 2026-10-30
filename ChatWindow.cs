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
    DateTime lastActivity = DateTime.MinValue; // last message that counted (for the "quiet moment" sound)

    public ChatWindow()
    {
        Text = "Chat unifié - Stream Launcher";
        Size = new Size(520, 720);
        MinimumSize = new Size(360, 360);
        StartPosition = FormStartPosition.CenterParent;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        // top bar: platform logos (click = switch off / on), settings and "always on top"
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, BackColor = Ui.Header, Padding = new Padding(12, 9, 8, 0), WrapContents = false };
        foreach (var s in sources)
        {
            var t = new ChatToggle(s.PlatformId) { On = !store.ChatOff.Contains(s.PlatformId), Margin = new Padding(0, 0, 10, 0) };
            var src = s;
            t.Click += (_, _) => SwitchPlatform(src, t);
            toggles.Add(t);
            top.Controls.Add(t);
        }
        var settings = Ui.Btn("⚙ Réglages", Ui.Kind.Secondary, (_, _) => ShowSettings());
        settings.Margin = new Padding(8, 2, 8, 0);
        top.Controls.Add(settings);
        var pin = new CheckBox { Text = "Au premier plan", AutoSize = true, ForeColor = Color.White, Font = Ui.Small, Margin = new Padding(4, 12, 0, 0) };
        pin.CheckedChanged += (_, _) => TopMost = pin.Checked;
        top.Controls.Add(pin);

        // bottom bar: the message and "Envoyer"
        send = Ui.Btn("Envoyer", Ui.Kind.Primary, async (_, _) => await SendAll());
        send.Dock = DockStyle.Right;
        send.Margin = new Padding(0);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Ui.Bg, Padding = new Padding(10, 7, 10, 7) };
        bottom.Controls.Add(input);
        bottom.Controls.Add(counter);
        bottom.Controls.Add(send);

        Controls.Add(view);
        Controls.Add(bottom);
        Controls.Add(top);

        input.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendAll(); } };
        input.TextChanged += (_, _) => counter.Text = input.TextLength == 0 ? "" : $"{input.TextLength}/500";

        view.ShowBadges = ChatKinds.Shown("twitch", "badges");
        foreach (var s in sources)
        {
            s.Message += m => OnUi(() => Append(m));
            s.StatusChanged += () => OnUi(RefreshToggles);
            if (!store.ChatOff.Contains(s.PlatformId)) s.Start();
        }
        RefreshToggles();
        LoadBadges();
        Shown += (_, _) => input.Focus();
        FormClosed += (_, _) => { foreach (var s in sources) s.Dispose(); };
        AddSystem("Chat unifié : écris ici, le message part sur toutes les plateformes allumées. Clique sur un logo pour l'éteindre ; ⚙ Réglages : son, utilisateurs exclus, ce qui s'affiche.");
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
            if (!m.Mine && !m.System) MaybePlaySound(m);
        }
        catch (Exception e) { RunLog.Write("chat : affichage impossible : " + e); }
    }

    void AddSystem(string text) => view.AddMessage(new ChatMessage(DateTime.Now, "system", "Stream Launcher", text));

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
        var targets = sources.Where(s => !store.ChatOff.Contains(s.PlatformId)).ToList();
        if (targets.Count == 0) { AddSystem("Aucune plateforme allumée : clique sur un logo pour en allumer une."); return; }
        send.Enabled = false;
        input.Enabled = false;
        try
        {
            var results = await Task.WhenAll(targets.Select(async s =>
            {
                try
                {
                    await s.SendAsync(text, CancellationToken.None);
                    return (s, (string?)null);
                }
                catch (Exception e) { return (s, e.Message); }
            }));
            var ok = results.Where(r => r.Item2 == null).ToList();
            if (ok.Count > 0)
            {
                var names = ok.Select(r => Streaming.Platforms.First(p => p.Id == r.s.PlatformId).Name);
                Append(new ChatMessage(DateTime.Now, ok[0].s.PlatformId, "Moi (" + string.Join(" + ", names) + ")", text, null, Mine: true));
                input.Clear();
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
            list.Controls.Add(new Label { Text = "Ce qui s'affiche en plus des messages :", AutoSize = true, Font = Ui.Bold, Margin = new Padding(0, 0, 0, 6) });
            foreach (var (kind, label) in ChatKinds.ByPlatform[p.Id])
            {
                var cb = new CheckBox { Text = label, AutoSize = true, Checked = !store.ChatHidden.Contains(p.Id + ":" + kind), Margin = new Padding(0, 2, 0, 2) };
                kinds[p.Id + ":" + kind] = cb;
                list.Controls.Add(cb);
            }
            list.Controls.Add(new Label
            {
                Text = p.Id switch
                {
                    "twitch" => "Les récompenses de points de chaîne sans message ne sont pas disponibles (elles demandent un droit supplémentaire).",
                    "kick" => "Les événements Kick passent par le flux non officiel de kick.com : ils peuvent cesser de fonctionner.",
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
        store.ChatHidden = kinds.Where(k => !k.Value.Checked).Select(k => k.Key).ToList();
        store.Save();
    }
}

/// <summary>Platform logo used as an on/off button: coloured badge with a status dot; greyed out when off.</summary>
sealed class ChatToggle : Control
{
    readonly string id;
    public bool On { get; set; } = true;
    public Color Dot { get; set; } = Color.Gray;

    public ChatToggle(string id)
    {
        this.id = id;
        Size = new Size(40, 36);
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
    }
}
