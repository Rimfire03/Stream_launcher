/// <summary>Edit the title, game and tags of the live on Twitch and Kick, with favourites, history and the game dictionary.</summary>
sealed class StreamingDialog : Form
{
    readonly StreamingStore store = StreamingStore.Current;
    readonly TextBox tbTitle = new() { Dock = DockStyle.Top, Font = Ui.Base, MaxLength = 140 };
    readonly TextBox tbGame = new() { Dock = DockStyle.Top, Font = Ui.Base, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource };
    readonly TextBox tbTags = new() { Dock = DockStyle.Top, Font = Ui.Base };
    readonly Label lblCount = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted };
    readonly Label lblGameInfo = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted };
    readonly Label lblStatus = new() { AutoSize = true, Font = Ui.Base, MaximumSize = new Size(540, 0), Padding = new Padding(0, 6, 0, 0) };
    readonly Label lblDict = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Padding = new Padding(0, 8, 0, 0) };
    readonly FlowLayoutPanel accounts = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top };
    readonly ListView lvFav = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Font = Ui.Base };
    readonly ListView lvHistory = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Font = Ui.Base };
    readonly System.Windows.Forms.Timer searchTimer = new() { Interval = 400 };
    CancellationTokenSource? searchCts;
    bool busy;

    public StreamingDialog()
    {
        Text = "Live : titre, jeu et tags - Stream Launcher";
        Size = new Size(1260, 740);
        MinimumSize = new Size(1180, 660);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Bg;
        Font = Ui.Base;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 590));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildLeft(), 0, 0);
        root.Controls.Add(BuildRight(), 1, 0);
        Controls.Add(root);

        tbTitle.TextChanged += (_, _) => lblCount.Text = $"{tbTitle.Text.Length}/140";
        tbGame.TextChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); ShowGameInfo(); };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); await SearchGames(); };
        Action onState = () => { if (IsHandleCreated && !IsDisposed) BeginInvoke(RebuildAccounts); };
        foreach (var p in Streaming.Platforms) p.StateChanged += onState;
        FormClosed += (_, _) =>
        {
            foreach (var p in Streaming.Platforms) p.StateChanged -= onState;
            searchCts?.Cancel();
            searchTimer.Dispose();
        };
        Shown += async (_, _) => await OnShown();

        lblCount.Text = "0/140";
        RebuildAccounts();
        FillFavs();
        FillHistory();
        ShowDict();
    }

    // ---- layout ----

    Control BuildLeft()
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 0, 14, 0) };
        void Add(Control c) { c.Width = 550; c.Margin = new Padding(0, 0, 0, 6); p.Controls.Add(c); }

        Add(Ui.Heading("COMPTES"));
        Add(accounts);
        Add(Ui.Heading("INFORMATIONS DU LIVE"));
        Add(new Label { Text = "Mêmes titre, jeu et tags sur toutes les plateformes connectées.", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true });
        Add(new Label { Text = "Titre", Font = Ui.Bold, AutoSize = true });
        Add(tbTitle);
        Add(lblCount);
        Add(new Label { Text = "Jeu (le dictionnaire se complète tout seul pendant la saisie)", Font = Ui.Bold, AutoSize = true });
        Add(tbGame);
        Add(lblGameInfo);
        Add(new Label { Text = "Tags (séparés par des virgules ; 10 au plus, lettres et chiffres)", Font = Ui.Bold, AutoSize = true });
        Add(tbTags);

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
        actions.Controls.Add(Ui.Btn("Appliquer sur les plateformes", Ui.Kind.Primary, async (_, _) => await ApplyForm()));
        actions.Controls.Add(Ui.Btn("Charger depuis le live", Ui.Kind.Secondary, async (_, _) => await LoadFromLive()));
        Add(actions);
        var stars = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        stars.Controls.Add(Ui.Btn("★ Ajouter aux favoris (titre + jeu + tags)", Ui.Kind.Secondary, (_, _) => AddFavorite()));
        Add(stars);
        Add(lblStatus);
        Add(lblDict);
        return p;
    }

    Control BuildRight()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = Ui.Base };

        var fav = new TabPage("Favoris (titre + jeu + tags)") { BackColor = Ui.Card, Padding = new Padding(10) };
        lvFav.Columns.Add("Titre", 280);
        lvFav.Columns.Add("Jeu", 150);
        lvFav.Columns.Add("Tags", 170);
        lvFav.DoubleClick += async (_, _) => await ApplyFavorite();
        var fb = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        fb.Controls.Add(Ui.Btn("Appliquer en 1 clic", Ui.Kind.Primary, async (_, _) => await ApplyFavorite()));
        fb.Controls.Add(Ui.Btn("Modifier…", Ui.Kind.Secondary, (_, _) => EditFavorite(false)));
        fb.Controls.Add(Ui.Btn("Nouveau…", Ui.Kind.Secondary, (_, _) => EditFavorite(true)));
        fb.Controls.Add(Ui.Btn("Dans le formulaire", Ui.Kind.Secondary, (_, _) => FavoriteToForm()));
        fb.Controls.Add(Ui.Btn("Supprimer", Ui.Kind.Secondary, (_, _) => RemoveFavorite()));
        fav.Controls.Add(lvFav);
        fav.Controls.Add(fb);

        var hist = new TabPage("Historique (15 derniers lives)") { BackColor = Ui.Card, Padding = new Padding(10) };
        lvHistory.Columns.Add("Date", 120);
        lvHistory.Columns.Add("Titre", 250);
        lvHistory.Columns.Add("Jeu", 130);
        lvHistory.Columns.Add("Tags", 130);
        lvHistory.Columns.Add("Origine", 90);
        lvHistory.DoubleClick += async (_, _) => await ApplyHistory();
        var hb = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        hb.Controls.Add(Ui.Btn("Appliquer", Ui.Kind.Primary, async (_, _) => await ApplyHistory()));
        hb.Controls.Add(Ui.Btn("★ Mettre en favoris", Ui.Kind.Secondary, (_, _) => FavHistory()));
        hb.Controls.Add(Ui.Btn("Importer mes lives Twitch (VOD)", Ui.Kind.Secondary, async (_, _) => await ImportVods()));
        hist.Controls.Add(lvHistory);
        hist.Controls.Add(hb);

        tabs.TabPages.Add(fav);
        tabs.TabPages.Add(hist);
        return tabs;
    }

    // ---- accounts ----

    void RebuildAccounts()
    {
        accounts.Controls.Clear();
        foreach (var p in Streaming.Platforms)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(new Label
            {
                Text = $"{p.Name} : " + (p.Connected ? $"connecté ({p.UserName})" : p.Configured ? "non connecté" : "indisponible (version de développement)")
                    + (p.Connected ? p.State switch { Health.Ok => "  ✔ opérationnel", Health.Error => "  ✖ erreur", Health.Checking => "  …", _ => "" } : ""),
                Font = Ui.Bold, AutoSize = false, Width = 285, Height = 24, Margin = new Padding(0, 8, 8, 0),
                ForeColor = StatusPill.ColorOf(p.State),
            });
            var plat = p;
            row.Controls.Add(p.Connected
                ? Ui.Btn("Déconnecter", Ui.Kind.Secondary, (_, _) => { plat.Disconnect(); RebuildAccounts(); ShowGameInfo(); })
                : Ui.Btn("Connecter", Ui.Kind.Primary, async (_, _) => await Connect(plat)));
            if (p.Connected) row.Controls.Add(Ui.Btn("Tester", Ui.Kind.Ghost, async (_, _) => { await plat.CheckAsync(); Say(plat.StateText, plat.State == Health.Error); }));
            accounts.Controls.Add(row);
        }
    }

    async Task Connect(Platform p)
    {
        if (!p.Configured)
        {
            Say($"{p.Name} n'est pas disponible dans cette version : elle ne contient pas les identifiants de l'application (version de développement). Utilise la version publiée.", true);
            return;
        }
        using var login = new LoginDialog(p);
        login.ShowDialog(this);
        RebuildAccounts();
        _ = p.CheckAsync();
        if (p.Connected)
        {
            Say($"{p.Name} connecté : {p.UserName}", false);
            await RefreshDict(); // new account: its categories join the dictionary
        }
    }

    async Task OnShown()
    {
        if (Streaming.Connected.Any() && tbTitle.Text == "") await LoadFromLive();
    }

    // ---- status helpers ----

    void Say(string text, bool error)
    {
        lblStatus.ForeColor = error ? Ui.Danger : Ui.Text;
        lblStatus.Text = text;
    }

    async Task Guard(Func<Task> work)
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
        try { await work(); }
        catch (Exception e) { Say("Erreur : " + e.Message, true); }
        finally { busy = false; UseWaitCursor = false; }
    }

    bool NeedConnection()
    {
        if (Streaming.Connected.Any()) return false;
        Say("Connecte d'abord un compte Twitch ou Kick (bouton « Connecter »).", true);
        return true;
    }

    void Report(List<(Platform P, string? Warning, Exception? Error)> results)
    {
        Say(string.Join("\n", results.Select(r => r.Error != null ? $"✖ {r.P.Name} : {r.Error.Message}" : r.Warning != null ? $"⚠ {r.P.Name} : appliqué, mais {r.Warning}" : $"✔ {r.P.Name} : appliqué")),
            results.Any(r => r.Error != null));
    }

    // ---- apply ----

    Task ApplyForm() => Guard(async () =>
    {
        if (NeedConnection()) return;
        var title = tbTitle.Text.Trim();
        var game = tbGame.Text.Trim();
        var tags = Streaming.NormalizeTags(tbTags.Text);
        tbTags.Text = string.Join(", ", tags);
        Say("Envoi…", false);
        var res = await Streaming.ApplyAsync(title == "" ? null : title, game == "" ? null : game, tags);
        Report(res);
        var ok = res.Where(r => r.Error == null).Select(r => r.P.Name).ToList();
        if (ok.Count > 0 && title != "")
        {
            store.AddHistory(new HistoryEntry { Title = title, GameName = game, Tags = tags, Source = string.Join(" + ", ok) });
            FillHistory();
        }
        FillGameItemsFromDict();
    });

    Task LoadFromLive() => Guard(async () =>
    {
        var p = Streaming.Connected.FirstOrDefault();
        if (p == null) { NeedConnection(); return; }
        var info = await p.GetLiveAsync(CancellationToken.None);
        tbTitle.Text = info.Title;
        tbGame.Text = info.GameName;
        tbTags.Text = string.Join(", ", info.Tags);
        Say($"Valeurs actuelles de {p.Name} chargées.", false);
    });

    // ---- favourites: a title, a game and tags that go together ----

    LiveInfo FormInfo() => new() { Title = tbTitle.Text.Trim(), GameName = tbGame.Text.Trim(), Tags = Streaming.NormalizeTags(tbTags.Text) };

    void AddFavorite()
    {
        var info = FormInfo();
        if (info.Title == "") { Say("Saisis au moins un titre pour l'ajouter aux favoris.", true); return; }
        Say(store.AddFavorite(info) ? "Ajouté aux favoris." : "Ce favori existe déjà.", false);
        FillFavs();
    }

    LiveInfo? SelectedFavorite => lvFav.SelectedItems.Count > 0 ? lvFav.SelectedItems[0].Tag as LiveInfo : null;

    void FavoriteToForm()
    {
        if (SelectedFavorite is not { } f) return;
        tbTitle.Text = f.Title;
        tbGame.Text = f.GameName;
        tbTags.Text = string.Join(", ", f.Tags);
    }

    /// <summary>1 click: fills the form with the favourite and sends the three values to every connected platform</summary>
    Task ApplyFavorite()
    {
        if (SelectedFavorite == null) return Task.CompletedTask;
        FavoriteToForm();
        return ApplyForm();
    }

    /// <summary>edits the selected favourite, or creates a new one, in a small form</summary>
    void EditFavorite(bool create)
    {
        if (!create && SelectedFavorite == null) return;
        int index = create ? -1 : lvFav.SelectedIndices[0];
        using var d = new FavoriteDialog(create ? new LiveInfo() : store.Favorites[index], create);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        if (create) store.Favorites.Add(d.Result); else store.Favorites[index] = d.Result;
        store.Save();
        FillFavs();
        int sel = create ? store.Favorites.Count - 1 : index;
        lvFav.Items[sel].Selected = true;
        Say(create ? "Favori ajouté." : "Favori modifié.", false);
    }

    void RemoveFavorite()
    {
        if (lvFav.SelectedItems.Count == 0) return;
        store.Favorites.RemoveAt(lvFav.SelectedIndices[0]);
        store.Save();
        FillFavs();
    }

    void FillFavs()
    {
        lvFav.Items.Clear();
        foreach (var f in store.Favorites)
            lvFav.Items.Add(new ListViewItem(new[] { f.Title, f.GameName, string.Join(", ", f.Tags) }) { Tag = f });
    }

    // ---- history ----

    void FillHistory()
    {
        lvHistory.Items.Clear();
        foreach (var h in store.History)
            lvHistory.Items.Add(new ListViewItem(new[] { h.Date.ToString("dd/MM/yyyy HH:mm"), h.Title, h.GameName, string.Join(", ", h.Tags), h.Source }) { Tag = h });
    }

    HistoryEntry? SelectedHistory => lvHistory.SelectedItems.Count > 0 ? lvHistory.SelectedItems[0].Tag as HistoryEntry : null;

    Task ApplyHistory()
    {
        if (SelectedHistory is not { } h) return Task.CompletedTask;
        tbTitle.Text = h.Title;
        tbGame.Text = h.GameName;
        tbTags.Text = string.Join(", ", h.Tags);
        return ApplyForm();
    }

    void FavHistory()
    {
        if (SelectedHistory is not { } h) return;
        Say(store.AddFavorite(h) ? "Ce live a été ajouté aux favoris." : "Ce favori existe déjà.", false);
        FillFavs();
    }

    Task ImportVods() => Guard(async () =>
    {
        var twitch = Streaming.Platforms.First(p => p.Id == "twitch");
        if (!twitch.Connected) { Say("Connecte d'abord ton compte Twitch.", true); return; }
        var vods = await twitch.RecentLivesAsync(CancellationToken.None);
        int before = store.History.Count;
        foreach (var v in vods.Where(v => !store.History.Any(h => h.Title == v.Title && Math.Abs((h.Date - v.Date).TotalHours) < 24))) store.History.Add(v);
        store.History = store.History.OrderByDescending(h => h.Date).Take(StreamingStore.HistorySize).ToList();
        store.Save();
        FillHistory();
        Say($"{Math.Max(0, store.History.Count - before)} live(s) importé(s) (Twitch ne donne que le titre et la date).", false);
    });

    // ---- game dictionary ----

    void ShowDict()
    {
        var d = GameDict.Current;
        lblDict.Text = d.Games.Count == 0 ? "Dictionnaire des jeux : vide (se remplit à la connexion et à chaque lancement)"
            : $"Dictionnaire des jeux : {d.Games.Count} jeux, mis à jour le {d.Updated:dd/MM/yyyy HH:mm} (à chaque lancement)";
    }

    Task RefreshDict() => Guard(async () =>
    {
        if (NeedConnection()) return;
        Say("Mise à jour du dictionnaire des jeux…", false);
        int n = await Streaming.RefreshGamesAsync();
        FillGameItemsFromDict();
        ShowDict();
        Say($"Dictionnaire à jour : {n} jeux.", false);
    });

    async Task SearchGames()
    {
        var text = tbGame.Text;
        if (text.Trim().Length < 2) return;
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        try
        {
            var found = await Streaming.SearchGamesAsync(text, searchCts.Token);
            SetSuggestions(found);
            ShowDict();
        }
        catch (OperationCanceledException) { }
    }

    void FillGameItemsFromDict() => SetSuggestions(GameDict.Current.Find(tbGame.Text, 200));

    void SetSuggestions(List<GameEntry> games)
    {
        var src = new AutoCompleteStringCollection();
        src.AddRange(games.Select(g => g.Name).ToArray());
        tbGame.AutoCompleteCustomSource = src;
    }

    void ShowGameInfo()
    {
        var g = GameDict.Current.Get(tbGame.Text);
        if (g == null) { lblGameInfo.Text = tbGame.Text.Trim() == "" ? "" : "Jeu inconnu du dictionnaire (il sera cherché sur la plateforme à l'envoi)"; return; }
        lblGameInfo.Text = "Pris en charge : " + string.Join("  ·  ", Streaming.Platforms.Select(p => $"{p.Name} {(HasId(g, p) ? "✔" : "?")}"));
    }

    static bool HasId(GameEntry g, Platform p) => p.Id == "twitch" ? g.TwitchId != null : g.KickId != null;
}

/// <summary>Edit one favourite: title, game and tags.</summary>
sealed class FavoriteDialog : Form
{
    public LiveInfo Result { get; private set; } = new();

    public FavoriteDialog(LiveInfo f, bool create)
    {
        Text = create ? "Nouveau favori" : "Modifier le favori";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 270);
        Font = Ui.Base;
        var title = new TextBox { Left = 16, Top = 36, Width = 488, MaxLength = 140, Text = f.Title };
        var game = new TextBox { Left = 16, Top = 92, Width = 488, Text = f.GameName, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource };
        var names = new AutoCompleteStringCollection();
        names.AddRange(GameDict.Current.Find("", 2000).Select(g => g.Name).ToArray());
        game.AutoCompleteCustomSource = names;
        var tags = new TextBox { Left = 16, Top = 148, Width = 488, Text = string.Join(", ", f.Tags) };
        Controls.AddRange(new Control[]
        {
            new Label { Left = 16, Top = 14, AutoSize = true, Text = "Titre", Font = Ui.Bold }, title,
            new Label { Left = 16, Top = 70, AutoSize = true, Text = "Jeu", Font = Ui.Bold }, game,
            new Label { Left = 16, Top = 126, AutoSize = true, Text = "Tags (séparés par des virgules ; 10 au plus, lettres et chiffres)", Font = Ui.Bold }, tags,
        });
        var ok = Ui.Btn("Enregistrer", Ui.Kind.Primary, (_, _) =>
        {
            if (title.Text.Trim() == "") { MessageBox.Show("Le titre est obligatoire.", Text); return; }
            Result = new LiveInfo { Title = title.Text.Trim(), GameName = game.Text.Trim(), Tags = Streaming.NormalizeTags(tags.Text) };
            DialogResult = DialogResult.OK;
        });
        ok.Left = 16; ok.Top = 214;
        var cancel = Ui.Btn("Annuler", Ui.Kind.Secondary, (_, _) => DialogResult = DialogResult.Cancel);
        cancel.Left = 140; cancel.Top = 214;
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

/// <summary>Shows what the user has to do (code to enter / page to authorize) while the platform sign-in runs.</summary>
sealed class LoginDialog : Form
{
    readonly CancellationTokenSource cts = new();
    readonly Label info = new() { Left = 16, Top = 16, Width = 388, Height = 150, AutoSize = false, Font = new Font("Segoe UI", 11f) };
    readonly Button close;

    public LoginDialog(Platform p)
    {
        Text = $"Connexion à {p.Name}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 230);
        info.Text = "Connexion en cours…";
        close = Ui.Btn("Annuler", Ui.Kind.Secondary, (_, _) => { cts.Cancel(); Close(); });
        close.Left = 16; close.Top = 180;
        Controls.Add(info);
        Controls.Add(close);
        FormClosed += (_, _) => cts.Cancel();
        Shown += async (_, _) =>
        {
            try
            {
                await p.ConnectAsync(msg => { if (IsHandleCreated) BeginInvoke(() => info.Text = msg); }, cts.Token);
                DialogResult = DialogResult.OK;
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                info.ForeColor = Ui.Danger;
                info.Text = "Échec de la connexion :\r\n\r\n" + e.Message;
                close.Text = "Fermer";
            }
        };
    }
}

/// <summary>Header indicator of a platform: coloured dot (grey off, orange checking, green working, red failing) and its name.</summary>
sealed class StatusPill : Control
{
    readonly Platform p;
    readonly ToolTip tip;

    public static Color ColorOf(Health h) => h switch
    {
        Health.Ok => ColorTranslator.FromHtml("#2E9D5B"),
        Health.Checking => ColorTranslator.FromHtml("#E0A030"),
        Health.Error => ColorTranslator.FromHtml("#D64535"),
        _ => ColorTranslator.FromHtml("#8A8799"),
    };

    public StatusPill(Platform p, ToolTip tip)
    {
        this.p = p;
        this.tip = tip;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Size = new Size(TextRenderer.MeasureText(p.Name, Ui.Small).Width + 34, 26);
        Margin = new Padding(0, 0, 4, 0);
        p.StateChanged += OnState;
        Disposed += (_, _) => p.StateChanged -= OnState;
        Refresh2();
    }

    void OnState()
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(Refresh2);
    }

    void Refresh2()
    {
        tip.SetToolTip(this, p.StateText + (p.State == Health.Off ? "\n(clic : connecter le compte)" : ""));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var br = new SolidBrush(ColorOf(p.State))) g.FillEllipse(br, 8, Height / 2 - 5, 10, 10);
        TextRenderer.DrawText(g, p.Name, Ui.Small, new Rectangle(24, 0, Width - 24, Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}
