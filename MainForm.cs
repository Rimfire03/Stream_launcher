class MainForm : Form
{
    readonly Config cfg;
    readonly NotifyIcon tray = new() { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Text = "Stream Launcher" };
    readonly ListBox lbProf = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 42, BorderStyle = BorderStyle.None, BackColor = Ui.Card };
    readonly ListBox lbSteps = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36, BorderStyle = BorderStyle.None, BackColor = Ui.Card };
    readonly PropertyGrid pg = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true, PropertySort = PropertySort.Categorized, ViewBackColor = Ui.Card, LineColor = Ui.Border, CategoryForeColor = Ui.Primary };
    readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = Ui.Mono, BorderStyle = BorderStyle.None, BackColor = Ui.Card };
    readonly FlowLayoutPanel quick = new() { Dock = DockStyle.Fill, AutoSize = false, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
    readonly Label lblProfile = new() { Font = Ui.Title, ForeColor = Ui.Text, AutoSize = true };
    readonly Label lblCount = new() { Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true };
    readonly Label lblEmpty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Muted, Font = Ui.Base, BackColor = Ui.Card, Padding = new Padding(30) };
    readonly Label lblProps = new() { Dock = DockStyle.Top, Height = 34, Font = Ui.Bold, ForeColor = Ui.Text, BackColor = Ui.Card, Padding = new Padding(12, 9, 0, 0), Text = "Réglages de l'étape" };
    readonly Label lblFootLeft = new() { Dock = DockStyle.Fill, Font = Ui.Small, ForeColor = ColorTranslator.FromHtml("#64748B"), TextAlign = ContentAlignment.MiddleLeft, Text = "Vérification des mises à jour…" };
    readonly Button btnRun, btnStop, btnRec;
    readonly Button btnSide = new() { Text = "◀", FlatStyle = FlatStyle.Flat, Size = new Size(28, 22), Cursor = Cursors.Hand, TabStop = false, Font = Ui.Small, BackColor = Ui.Card, ForeColor = Ui.Muted };
    readonly ToolTip sideTip = new();
    Panel sidebar = null!;
    Label sideTitle = null!;
    TableLayoutPanel profBtns = null!;
    bool allowShow, exiting, refreshing;
    CancellationTokenSource? cts;
    Recorder? rec;
    IDisposable? picker;
    Form? pickBar;
    Form? recBar;

    Profile? Cur => lbProf.SelectedItem as Profile;
    Step? CurStep => lbSteps.SelectedItem as Step;

    public MainForm(Config cfg, bool startHidden)
    {
        this.cfg = cfg;
        allowShow = !startHidden;
        Text = "Stream Launcher " + Updater.Format(Updater.Current);
        Icon = tray.Icon;
        Font = Ui.Base;
        BackColor = Ui.Bg;
        ForeColor = Ui.Text;
        Size = new Size(1180, 760);
        MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterScreen;

        // ---- header band: brand, quick-launch pills, help menu ----
        var header = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Ui.Header, Padding = new Padding(250, 0, 0, 0) };
        var logo = new PictureBox { Image = new Icon(tray.Icon!, 32, 32).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(28, 28), Location = new Point(14, 8) };
        var brand = new Label { Text = "Stream Launcher", Font = Ui.Title, ForeColor = Color.White, AutoSize = true, Location = new Point(48, 7) };
        var ver = new Label { Text = Updater.Format(Updater.Current), Font = Ui.Small, ForeColor = Color.FromArgb(170, 165, 195), AutoSize = true, Location = new Point(48 + TextRenderer.MeasureText("Stream Launcher", Ui.Title).Width, 16) };
        quick.Padding = new Padding(0, 6, 0, 0);
        quick.WrapContents = false;
        header.Controls.Add(quick);
        header.Controls.AddRange(new Control[] { logo, brand, ver });

        // ---- profiles sidebar ----
        sideTitle = new Label { Text = "PROFILS", Font = Ui.Small, ForeColor = Ui.Muted, Dock = DockStyle.Top, Height = 28, Padding = new Padding(14, 10, 0, 0), BackColor = Ui.Card };
        profBtns = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(10, 8, 10, 10), BackColor = Ui.Card, ColumnCount = 2, RowCount = 2 };
        profBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profBtns.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        profBtns.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var newBtn = Ui.Btn("+  Nouveau profil", Ui.Kind.Secondary, (_, _) => NewProfile());
        var renBtn = Ui.Btn("Renommer", Ui.Kind.Ghost, (_, _) => RenameProfile());
        var delBtn = Ui.Btn("Supprimer", Ui.Kind.Ghost, (_, _) => DeleteProfile());
        foreach (var btn in new[] { newBtn, renBtn, delBtn }) { btn.AutoSize = false; btn.Dock = DockStyle.Fill; btn.Margin = new Padding(2); btn.Padding = new Padding(0); }
        profBtns.Controls.Add(newBtn, 0, 0);
        profBtns.SetColumnSpan(newBtn, 2);
        profBtns.Controls.Add(renBtn, 0, 1);
        profBtns.Controls.Add(delBtn, 1, 1);
        sidebar = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Ui.Card };
        btnSide.FlatAppearance.BorderSize = 0;
        btnSide.Click += (_, _) => SetSidebar(!cfg.SidebarCollapsed);
        sideTitle.Controls.Add(btnSide);
        sidebar.Controls.Add(lbProf);
        sidebar.Controls.Add(sideTitle);
        sidebar.Controls.Add(profBtns);

        // ---- profile bar (title + run buttons) and toolbar ----
        btnRun = Ui.Btn("▶  Lancer le live", Ui.Kind.Primary, (_, _) => { if (Cur != null) RunProfile(Cur); });
        btnRun.Font = Ui.Bold;
        btnStop = Ui.Btn("■  Stop", Ui.Kind.Secondary, (_, _) => cts?.Cancel());
        btnStop.Enabled = false;
        btnRec = Ui.Btn("●  Enregistrer les clics", Ui.Kind.Secondary, (_, _) => ToggleRec());

        var titleBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Left, WrapContents = false, Margin = new Padding(0) };
        titleBox.Controls.AddRange(new Control[] { lblProfile, lblCount });
        var runBtns = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        runBtns.Controls.AddRange(new Control[] { btnRun, btnStop, btnRec });
        var profileBar = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Ui.Card, Padding = new Padding(18, 5, 14, 3) };
        profileBar.Controls.Add(runBtns);
        profileBar.Controls.Add(titleBox);

        var addMenu = new ContextMenuStrip { Font = Ui.Base };
        void Group(string name) => addMenu.Items.Add(new ToolStripLabel(name) { Font = Ui.Small, ForeColor = Ui.Muted, Enabled = false });
        void Item(string text, Action a) => addMenu.Items.Add(text, null, (_, _) => a());
        Group("APPLICATIONS");
        Item("Application ou fichier…", AddApp);
        Item("Importer les fenêtres ouvertes (position + écran)…", ImportWindows);
        Item("Site web dans un navigateur", () => AddStep(new Step { Type = StepType.Browser, Browser = BrowserKind.Chrome }));
        addMenu.Items.Add(new ToolStripSeparator());
        Group("FENÊTRES");
        Item("Placer une fenêtre (écran, Multitâche)", () => AddStep(new Step { Type = StepType.Place, Zone = Zone.Gauche }));
        Item("Déplacer une fenêtre (je la déplace moi-même)", () => PickWindow(true));
        Item("Réduire une fenêtre (je clique dessus)", () => PickWindow(false));
        addMenu.Items.Add(new ToolStripSeparator());
        Group("INTERACTIONS");
        Item("Clic (réglé à la main)", () => AddStep(new Step { Type = StepType.Click }));
        Item("Icône de la zone de notification (je clique dessus)", PickTray);
        var addBtn = Ui.Btn("＋  Ajouter une étape  ▾", Ui.Kind.Primary, (s, _) => addMenu.Show((Control)s!, new Point(0, ((Control)s!).Height)));

        var toolsMenu = new ContextMenuStrip { Font = Ui.Base };
        toolsMenu.Items.Add("Identifier les écrans", null, (_, _) => IdentifyScreens());
        toolsMenu.Items.Add("Renommer les écrans…", null, (_, _) => RenameScreens());
        toolsMenu.Items.Add("Lister les icônes de la zone de notification", null, (_, _) => ListTray());
        toolsMenu.Items.Add(new ToolStripSeparator());
        toolsMenu.Items.Add("Vérifier les mises à jour…", null, (_, _) => CheckUpdate(true));
        var toolsBtn = Ui.Btn("Outils  ▾", Ui.Kind.Secondary, (s, _) => toolsMenu.Show((Control)s!, new Point(0, ((Control)s!).Height)));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Ui.Bg, Padding = new Padding(18, 6, 14, 0), WrapContents = false };
        toolbar.Controls.AddRange(new Control[]
        {
            addBtn, toolsBtn,
            Ui.Btn("↑", Ui.Kind.Secondary, (_, _) => MoveStep(-1)),
            Ui.Btn("↓", Ui.Kind.Secondary, (_, _) => MoveStep(1)),
            Ui.Btn("Dupliquer", Ui.Kind.Secondary, (_, _) => DuplicateStep()),
            Ui.Btn("Supprimer", Ui.Kind.Secondary, (_, _) => DeleteStep()),
        });

        // ---- steps | settings ----
        var stepsHolder = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card };
        stepsHolder.Controls.Add(lblEmpty);
        stepsHolder.Controls.Add(lbSteps);
        lblEmpty.BringToFront();
        var propsHolder = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card };
        propsHolder.Controls.Add(pg);
        propsHolder.Controls.Add(lblProps);
        var split2 = new SplitContainer { Dock = DockStyle.Fill, BackColor = Ui.Border, SplitterWidth = 1 };
        split2.SizeChanged += (_, _) => { if (split2.Width > 700) split2.SplitterDistance = (int)(split2.Width * 0.52); };
        split2.Panel1.Controls.Add(stepsHolder);
        split2.Panel2.Controls.Add(propsHolder);
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(0, 0, 0, 0) };
        var cardFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 0, 14, 6), BackColor = Ui.Bg };
        cardFrame.Controls.Add(split2);
        content.Controls.Add(cardFrame);
        content.Controls.Add(toolbar);
        content.Controls.Add(profileBar);

        // ---- journal ----
        var logHolder = new Panel { Dock = DockStyle.Bottom, Height = 84, BackColor = Ui.Bg, Padding = new Padding(16, 0, 14, 6) };
        var logTitle = new Label { Text = "JOURNAL", Font = Ui.Small, ForeColor = Ui.Muted, Dock = DockStyle.Top, Height = 16 };
        var logCard = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card, Padding = new Padding(8, 4, 8, 4) };
        logCard.Controls.Add(log);
        logHolder.Controls.Add(logCard);
        logHolder.Controls.Add(logTitle);

        // dock order: fill first, then edges (later controls dock outermost)
        var main = new Panel { Dock = DockStyle.Fill };
        main.Controls.Add(content);
        main.Controls.Add(sidebar);
        // footer, same look as FiltresApp: thin top border, copyright centred, version on the right, status on the left
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 26, BackColor = Ui.Card, Padding = new Padding(16, 0, 16, 0), ColumnCount = 3, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        var footColor = ColorTranslator.FromHtml("#64748B");
        footer.Controls.Add(lblFootLeft, 0, 0);
        footer.Controls.Add(new Label { Dock = DockStyle.Fill, Font = Ui.Small, ForeColor = footColor, TextAlign = ContentAlignment.MiddleCenter, Text = "© TomLine prod&&co by Thomas Guidez  ·  Préparation de live Twitch" }, 1, 0);
        footer.Controls.Add(new Label { Dock = DockStyle.Fill, Font = Ui.Small, ForeColor = footColor, TextAlign = ContentAlignment.MiddleRight, Text = $"Stream Launcher {Updater.Format(Updater.Current)}" }, 2, 0);
        footer.Paint += (_, e) => { using var pen = new Pen(Ui.Border); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };

        Controls.Add(main);
        Controls.Add(logHolder);
        Controls.Add(footer);
        Controls.Add(header);

        var help = new ToolStripMenuItem("Aide");
        help.DropDownItems.Add(new ToolStripMenuItem("Guide d'utilisation", null, (_, _) => ShowHelp(0)) { ShortcutKeys = Keys.F1 });
        var topics = new ToolStripMenuItem("Rubriques");
        for (int t = 0; t < HelpTopics.All.Length; t++)
        {
            int topic = t;
            topics.DropDownItems.Add(HelpTopics.All[t].Title, null, (_, _) => ShowHelp(topic));
        }
        help.DropDownItems.Add(topics);
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add("Vérifier les mises à jour…", null, (_, _) => CheckUpdate(true));
        help.DropDownItems.Add("À propos…", null, (_, _) => About());
        help.ForeColor = Color.White;
        void DarkText(ToolStripItemCollection items)
        {
            foreach (ToolStripItem i in items) { i.ForeColor = Ui.Text; if (i is ToolStripMenuItem m) DarkText(m.DropDownItems); }
        }
        DarkText(help.DropDownItems);
        var menu = new MenuStrip { Dock = DockStyle.Right, LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow, AutoSize = false, Width = 70, BackColor = Ui.Header, Font = Ui.Base, GripStyle = ToolStripGripStyle.Hidden };
        menu.Items.Add(help);
        MainMenuStrip = menu;
        header.Controls.Add(menu);

        // ---- step list: right-click menu and drawing ----
        var stepMenu = new ContextMenuStrip { Font = Ui.Base };
        stepMenu.Items.Add("▶  Tester cette étape", null, (_, _) => TestStep(false));
        stepMenu.Items.Add("▶  Tester à partir de cette étape", null, (_, _) => TestStep(true));
        stepMenu.Items.Add(new ToolStripSeparator());
        stepMenu.Items.Add("Monter", null, (_, _) => MoveStep(-1));
        stepMenu.Items.Add("Descendre", null, (_, _) => MoveStep(1));
        stepMenu.Items.Add("Dupliquer", null, (_, _) => DuplicateStep());
        stepMenu.Items.Add("Supprimer", null, (_, _) => DeleteStep());
        stepMenu.Opening += (_, e) => e.Cancel = CurStep == null || cts != null;
        lbSteps.ContextMenuStrip = stepMenu;
        lbSteps.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int i = lbSteps.IndexFromPoint(e.Location);
            if (i >= 0) lbSteps.SelectedIndex = i;
        };
        // rows are one line high: the full description of the hovered step is in a tooltip
        var stepTip = new ToolTip { InitialDelay = 400 };
        int tipIndex = -1;
        lbSteps.MouseMove += (_, e) =>
        {
            int i = lbSteps.IndexFromPoint(e.Location);
            if (i == tipIndex) return;
            tipIndex = i;
            var (m, sub) = i >= 0 ? Ui.Describe((Step)lbSteps.Items[i]) : ("", "");
            stepTip.SetToolTip(lbSteps, i >= 0 ? m + "
" + sub : "");
        };
        lbSteps.DrawItem += DrawStep;
        lbProf.DrawItem += DrawProfile;

        lbProf.SelectedIndexChanged += (_, _) => { RefreshSteps(); UpdateRunLabel(); };
        lbSteps.SelectedIndexChanged += (_, _) => { if (!refreshing) ShowProps(); };
        pg.PropertyValueChanged += (_, e) =>
        {
            var changed = e.ChangedItem?.PropertyDescriptor?.Name;
            RefreshSteps();
            cfg.Save();
            if (changed is "Zone" or "Action" or "Mode" or "PlaceWindow") ShowProps(); // the visible settings depend on these
        };

        foreach (Control c in new Control[] { this, lbSteps, pg, lblEmpty })
        {
            c.AllowDrop = true;
            c.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            c.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) DropFiles(files); };
        }

        tray.Visible = true;
        tray.DoubleClick += (_, _) => ShowMain();

        RebuildProfiles();
        if (lbProf.Items.Count > 0) lbProf.SelectedIndex = 0;
        SetSidebar(cfg.SidebarCollapsed, false);
        UpdateHeader();
        Log("Écrans : " + string.Join(" | ", Screen.AllScreens.Select(s =>
            $"{Config.Label(s.DeviceName)} {s.Bounds.Width}x{s.Bounds.Height}{(s.Primary ? " (principal)" : "")}")));
        HandleCreated += (_, _) => BeginInvoke(() => CheckUpdate(false));
    }

    // folds the profile list to a thin strip (the quick-launch pills in the header still start every profile)
    void SetSidebar(bool collapsed, bool save = true)
    {
        cfg.SidebarCollapsed = collapsed;
        sidebar.Width = collapsed ? 40 : 250;
        lbProf.Visible = profBtns.Visible = !collapsed;
        sideTitle.Text = collapsed ? "" : "PROFILS";
        btnSide.Text = collapsed ? "▶" : "◀";
        btnSide.Anchor = collapsed ? AnchorStyles.Top | AnchorStyles.Left : AnchorStyles.Top | AnchorStyles.Right;
        btnSide.Location = new Point(collapsed ? 6 : sidebar.Width - btnSide.Width - 8, 4);
        sideTip.SetToolTip(btnSide, collapsed ? "Afficher la liste des profils" : "Masquer la liste des profils");
        if (save) cfg.Save();
    }

    void ShowProps() => pg.SelectedObject = CurStep == null ? null : new StepView(CurStep);

    void UpdateHeader()
    {
        lblProfile.Text = Cur?.Name ?? "Aucun profil";
        int n = Cur?.Steps.Count ?? 0;
        lblCount.Text = Cur == null ? "Crée un profil pour commencer" : n == 0 ? "Aucune étape" : $"{n} étape{(n > 1 ? "s" : "")} · exécutées de haut en bas";
        lblEmpty.Visible = lbSteps.Items.Count == 0;
        lblEmpty.Text = Cur == null
            ? "Aucun profil.\n\nClique sur « + Nouveau » à gauche pour créer ton premier profil (par exemple « Live Twitch »)."
            : "Ce profil n'a encore aucune étape.\n\nClique sur « Ajouter une étape », ou glisse ici des raccourcis, exécutables ou documents.\nAstuce : ouvre tes applications, place-les, puis « Importer les fenêtres ouvertes ».";
        lblProps.Text = CurStep == null ? "Réglages de l'étape" : "Réglages : " + Ui.Badge(CurStep.Type).Label.ToLowerInvariant();
        var live = Cur != null && Live.IsLive(Cur.Name);
        btnRec.Visible = !live;
    }

    void DrawStep(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= lbSteps.Items.Count) return;
        var s = (Step)lbSteps.Items[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel ? Ui.Selected : Ui.Card)) g.FillRectangle(bg, e.Bounds);
        if (sel) using (var bar = new SolidBrush(Ui.Primary)) g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);
        using (var line = new Pen(Ui.Border)) g.DrawLine(line, e.Bounds.X + 14, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        TextRenderer.DrawText(g, (e.Index + 1).ToString(), Ui.Bold, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, 26, e.Bounds.Height), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

        var (label, color) = Ui.Badge(s.Type);
        var badge = new Rectangle(e.Bounds.X + 44, e.Bounds.Y + 9, 76, 18);
        using (var path = Ui.Round(badge, 6)) using (var br = new SolidBrush(color)) g.FillPath(br, path);
        TextRenderer.DrawText(g, label, new Font("Segoe UI", 8f, FontStyle.Bold), badge, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

        // one line per step: bold summary, then the details in grey, then the delay on the right
        var (main, sub) = Ui.Describe(s);
        var delay = $"+{s.DelayMs} ms";
        int right = e.Bounds.Right - 14;
        var delaySize = TextRenderer.MeasureText(delay, Ui.Small);
        TextRenderer.DrawText(g, delay, Ui.Small, new Rectangle(right - delaySize.Width, e.Bounds.Y, delaySize.Width, e.Bounds.Height), Ui.Muted, TextFormatFlags.VerticalCenter);
        int textLeft = badge.Right + 12, textWidth = right - delaySize.Width - 12 - textLeft;
        var fl = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
        int mainWidth = Math.Min(TextRenderer.MeasureText(main, Ui.Bold).Width + 4, textWidth);
        TextRenderer.DrawText(g, main, Ui.Bold, new Rectangle(textLeft, e.Bounds.Y, mainWidth, e.Bounds.Height), Ui.Text, fl);
        int subLeft = textLeft + mainWidth + 10;
        if (textWidth - mainWidth - 10 > 40)
            TextRenderer.DrawText(g, sub, Ui.Small, new Rectangle(subLeft, e.Bounds.Y, textLeft + textWidth - subLeft, e.Bounds.Height), Ui.Muted, fl);
    }

    void DrawProfile(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= lbProf.Items.Count) return;
        var p = (Profile)lbProf.Items[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel ? Ui.Selected : Ui.Card)) g.FillRectangle(bg, e.Bounds);
        if (sel) using (var bar = new SolidBrush(Ui.Primary)) g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);
        bool live = Live.IsLive(p.Name);
        var fl = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
        TextRenderer.DrawText(g, p.Name, Ui.Bold, new Rectangle(e.Bounds.X + 16, e.Bounds.Y + 6, e.Bounds.Width - 50, 20), Ui.Text, fl);
        TextRenderer.DrawText(g, live ? "en direct" : $"{p.Steps.Count} étape{(p.Steps.Count > 1 ? "s" : "")}", Ui.Small,
            new Rectangle(e.Bounds.X + 16, e.Bounds.Y + 25, e.Bounds.Width - 50, 18), live ? Ui.LiveGreen : Ui.Muted, fl);
        if (live) using (var br = new SolidBrush(Ui.LiveGreen)) g.FillEllipse(br, e.Bounds.Right - 28, e.Bounds.Y + 17, 11, 11);
    }

    void DuplicateStep()
    {
        if (Cur is not { } p || CurStep is not { } s) return;
        int i = lbSteps.SelectedIndex;
        p.Steps.Insert(i + 1, s.Clone());
        cfg.Save();
        RefreshSteps(i + 1);
    }

    async void CheckUpdate(bool manual)
    {
        try
        {
            var up = await Updater.CheckAsync();
            if (up is not { } u)
            {
                lblFootLeft.Text = "Application à jour";
                if (manual) MessageBox.Show("Version à jour : " + Updater.Format(Updater.Current), Text);
                return;
            }
            Log($"Mise à jour disponible : {Updater.Format(u.Ver)}");
            lblFootLeft.Text = $"Mise à jour {Updater.Format(u.Ver)} disponible";
            lblFootLeft.ForeColor = Ui.Primary;
            if (!manual && !allowShow) { tray.ShowBalloonTip(5000, "Stream Launcher", $"Mise à jour {Updater.Format(u.Ver)} disponible (ouvrir l'app pour l'installer)", ToolTipIcon.Info); return; }
            if (MessageBox.Show($"Installer {Updater.Format(u.Ver)} (actuelle : {Updater.Format(Updater.Current)}) ?\nL'application va redémarrer.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            await Updater.DownloadAndRestartAsync(u.Url);
            exiting = true;
            Close();
        }
        catch (Exception e)
        {
            lblFootLeft.Text = "Mises à jour : vérification impossible";
            if (manual) Log("ERREUR mise à jour : " + e.Message);
        }
    }

    // keeps the form hidden at startup when launched with --tray
    protected override void SetVisibleCore(bool value)
    {
        if (!allowShow && !IsHandleCreated) CreateHandle();
        base.SetVisibleCore(allowShow && value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        StopRec();
        StopPick();
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosing(e);
    }

    void ShowMain()
    {
        allowShow = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    static Button Btn(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += onClick;
        return b;
    }

    void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg)); return; }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
    }

    // full-screen number overlay on each monitor (the DISPLAYn names used by the Monitor field); click or 5 s to close
    void IdentifyScreens()
    {
        foreach (var s in Screen.AllScreens)
        {
            var num = new string(s.DeviceName.Where(char.IsDigit).ToArray());
            var f = new Form
            {
                FormBorderStyle = FormBorderStyle.None, TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Bounds = s.Bounds, BackColor = Color.Black, Opacity = 0.85,
            };
            f.Paint += (_, e) =>
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using var big = new Font("Segoe UI", f.Height / 3f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var small = new Font("Segoe UI", f.Height / 25f, GraphicsUnit.Pixel);
                e.Graphics.DrawString(num, big, Brushes.White, f.ClientRectangle, sf);
                e.Graphics.DrawString($"{Config.Label(s.DeviceName)}  {s.Bounds.Width}x{s.Bounds.Height}{(s.Primary ? "  (principal)" : "")}", small, Brushes.LightGray,
                    new Rectangle(0, f.Height * 3 / 4, f.Width, f.Height / 5), sf);
            };
            f.Click += (_, _) => f.Close();
            f.KeyDown += (_, _) => f.Close();
            var t = new System.Windows.Forms.Timer { Interval = 5000 };
            t.Tick += (_, _) => { t.Dispose(); f.Close(); };
            f.FormClosed += (_, _) => t.Dispose();
            f.Show();
            t.Start();
        }
        Log("Écrans : " + string.Join(" | ", Screen.AllScreens.Select(s => $"{Config.Label(s.DeviceName)}{(s.Primary ? " (principal)" : "")}")));
    }

    void RenameScreens()
    {
        var screens = Screen.AllScreens;
        using var f = new Form { Text = "Renommer les écrans", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(480, 20 + screens.Length * 32 + 50) };
        var boxes = new List<TextBox>();
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            f.Controls.Add(new Label { Text = $"{s.DeviceName} {s.Bounds.Width}x{s.Bounds.Height}{(s.Primary ? " *" : "")}", Left = 10, Top = 16 + i * 32, Width = 220 });
            var tb = new TextBox { Left = 235, Top = 12 + i * 32, Width = 235, Text = cfg.ScreenNames.GetValueOrDefault(s.DeviceName, "") };
            boxes.Add(tb);
            f.Controls.Add(tb);
        }
        var ok = new Button { Text = "OK", Left = 310, Top = 20 + screens.Length * 32, DialogResult = DialogResult.OK };
        var show = new Button { Text = "Identifier", Left = 10, Top = 20 + screens.Length * 32, AutoSize = true };
        show.Click += (_, _) => IdentifyScreens();
        var cancel = new Button { Text = "Annuler", Left = 395, Top = 20 + screens.Length * 32, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { ok, cancel, show });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        for (int i = 0; i < screens.Length; i++)
        {
            var name = boxes[i].Text.Trim();
            if (name == "") cfg.ScreenNames.Remove(screens[i].DeviceName);
            else cfg.ScreenNames[screens[i].DeviceName] = name;
        }
        cfg.Save();
        RefreshSteps();
    }

    void ShowHelp(int topic)
    {
        using var d = new HelpDialog(topic);
        d.ShowDialog(this);
    }

    void About() => MessageBox.Show(
        $"Stream Launcher {Updater.Format(Updater.Current)}\nTomLine prod&co\n\nPréparation du lancement d'un live : lance des applications, les place sur les écrans et clique dans leurs fenêtres.\n\nProfils : {Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "profiles.json")}\nhttps://github.com/Rimfire03/Stream_launcher",
        "À propos", MessageBoxButtons.OK, MessageBoxIcon.Information);

    // ---- profiles ----

    void RebuildProfiles()
    {
        int sel = lbProf.SelectedIndex;
        lbProf.Items.Clear();
        lbProf.Items.AddRange(cfg.Profiles.ToArray<object>());
        if (sel >= 0 && sel < lbProf.Items.Count) lbProf.SelectedIndex = sel;

        quick.Controls.Clear();
        var menu = new ContextMenuStrip();
        foreach (var p in cfg.Profiles)
        {
            var prof = p;
            var label = (Live.IsLive(prof.Name) ? "■ Fermer " : "▶ ") + prof.Name;
            var pill = Ui.Btn(label, Live.IsLive(prof.Name) ? Ui.Kind.Danger : Ui.Kind.Secondary, (_, _) => RunProfile(prof));
            pill.MinimumSize = new Size(0, 28);
            pill.Margin = new Padding(0, 0, 6, 0);
            quick.Controls.Add(pill);
            menu.Items.Add(label, null, (_, _) => RunProfile(prof));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Identifier les écrans", null, (_, _) => IdentifyScreens());
        menu.Items.Add("Ouvrir", null, (_, _) => ShowMain());
        menu.Items.Add("Arrêter l'exécution", null, (_, _) => cts?.Cancel());
        menu.Items.Add("Quitter", null, (_, _) => { exiting = true; Close(); });
        tray.ContextMenuStrip = menu;
        UpdateRunLabel();
    }

    void NewProfile()
    {
        var name = Prompt("Nom du profil", "Nouveau profil");
        if (name == null) return;
        cfg.Profiles.Add(new Profile { Name = name });
        cfg.Save();
        RebuildProfiles();
        lbProf.SelectedIndex = lbProf.Items.Count - 1;
    }

    void RenameProfile()
    {
        if (Cur is not { } p) return;
        var name = Prompt("Nouveau nom", p.Name);
        if (name == null) return;
        p.Name = name;
        cfg.Save();
        RebuildProfiles();
    }

    void DeleteProfile()
    {
        if (Cur is not { } p) return;
        if (MessageBox.Show($"Supprimer le profil « {p.Name} » ?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        cfg.Profiles.Remove(p);
        cfg.Save();
        RebuildProfiles();
        RefreshSteps();
    }

    static string? Prompt(string title, string def)
    {
        using var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(320, 80), MaximizeBox = false, MinimizeBox = false };
        var tb = new TextBox { Text = def, Left = 10, Top = 10, Width = 300 };
        var ok = new Button { Text = "OK", Left = 150, Top = 42, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Annuler", Left = 235, Top = 42, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { tb, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK && tb.Text.Trim() != "" ? tb.Text.Trim() : null;
    }

    // ---- steps ----

    void RefreshSteps(int select = -2)
    {
        refreshing = true;
        int sel = select == -2 ? lbSteps.SelectedIndex : select;
        lbSteps.Items.Clear();
        if (Cur != null) lbSteps.Items.AddRange(Cur.Steps.ToArray<object>());
        if (sel >= lbSteps.Items.Count) sel = lbSteps.Items.Count - 1;
        if (sel >= 0) lbSteps.SelectedIndex = sel;
        refreshing = false;
        ShowProps();
        UpdateHeader();
    }

    void AddStep(Step s)
    {
        if (Cur is not { } p) return;
        p.Steps.Add(s);
        cfg.Save();
        RefreshSteps(p.Steps.Count - 1);
    }

    void AddApp()
    {
        using var d = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe|Tous les fichiers|*.*" };
        if (d.ShowDialog() == DialogResult.OK) AddStep(new Step { Type = StepType.Launch, Exe = d.FileName });
    }

    // drag & drop of shortcuts (.lnk), executables, documents: one Launch step each
    void DropFiles(string[] files)
    {
        if (Cur == null) { Log("Crée ou sélectionne un profil avant de déposer des fichiers"); return; }
        foreach (var f in files)
        {
            string exe = f, args = "";
            if (f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    dynamic sc = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                    dynamic lnk = sc.CreateShortcut(f);
                    string target = lnk.TargetPath;
                    if (target != "") { exe = target; args = lnk.Arguments; }
                }
                catch { }
            }
            // Store apps have no target path: keep the .lnk itself, and its name says nothing about the process
            bool resolved = !exe.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
            Cur.Steps.Add(new Step
            {
                Type = StepType.Launch, Exe = exe, Args = args, SkipIfRunning = resolved,
                Process = resolved ? Path.GetFileNameWithoutExtension(exe) : "",
            });
        }
        cfg.Save();
        RefreshSteps(Cur.Steps.Count - 1);
    }

    void MoveStep(int dir)
    {
        if (Cur is not { } p || lbSteps.SelectedIndex < 0) return;
        int i = lbSteps.SelectedIndex, j = i + dir;
        if (j < 0 || j >= p.Steps.Count) return;
        (p.Steps[i], p.Steps[j]) = (p.Steps[j], p.Steps[i]);
        cfg.Save();
        RefreshSteps(j);
    }

    void DeleteStep()
    {
        if (Cur is not { } p || CurStep is not { } s) return;
        int i = lbSteps.SelectedIndex;
        p.Steps.Remove(s);
        cfg.Save();
        RefreshSteps(i);
    }

    void ImportWindows()
    {
        if (Cur is not { } p) return;
        using var d = new ImportDialog();
        if (d.ShowDialog(this) != DialogResult.OK) return;
        p.Steps.AddRange(d.Result);
        cfg.Save();
        RefreshSteps(p.Steps.Count - 1);
    }

    // ---- run ----

    void TestStep(bool fromHere)
    {
        if (Cur is not { } p || CurStep is not { } s) return;
        int i = lbSteps.SelectedIndex;
        RunProfile(new Profile { Name = "Test : " + s, Steps = fromHere ? p.Steps.Skip(i).ToList() : new List<Step> { s } }, live: false);
    }


    // Toggle: first press runs the profile (and remembers the live), next press closes the windows it opened.
    async void RunProfile(Profile p, bool live = true)
    {
        if (cts != null) return;
        if (live && Live.IsLive(p.Name)) { CloseLive(p); return; }
        cts = new CancellationTokenSource();
        SetBusy(true);
        if (Visible) WindowState = FormWindowState.Minimized; // stay out of the way of the clicks
        Log($"=== {p.Name} ===");
        var token = cts.Token;
        try
        {
            int err = await Task.Run(() => live ? Live.Run(p, new Runner { Log = Log }, token) : new Runner { Log = Log }.Run(p, token));
            Log(err == 0 ? "Terminé" : $"Terminé avec {err} erreur(s)");
        }
        catch (OperationCanceledException) { Log("Annulé"); }
        finally
        {
            cts.Dispose();
            cts = null;
            SetBusy(false);
            if (Visible) WindowState = FormWindowState.Normal;
            if (live) RebuildProfiles();
        }
    }

    async void CloseLive(Profile p)
    {
        Log($"=== Fermeture du live : {p.Name} ===");
        int n = await Task.Run(() => Live.Close(p.Name, Log));
        Log($"{n} fenêtre(s) fermée(s)");
        RebuildProfiles();
    }

    void UpdateRunLabel()
    {
        bool live = Cur != null && Live.IsLive(Cur.Name);
        btnRun.Text = live ? "■  Fermer le live" : "▶  Lancer le live";
        Ui.Style(btnRun, live ? Ui.Kind.Danger : Ui.Kind.Primary);
        lbProf.Invalidate();
        UpdateHeader();
    }

    void SetBusy(bool busy)
    {
        btnRun.Enabled = !busy;
        btnStop.Enabled = busy;
        btnRec.Enabled = !busy;
    }

    async void ListTray()
    {
        Log("Icônes de la zone de notification (lecture en cours)…");
        try { Log("Icônes : " + string.Join(" | ", await Task.Run(Runner.ListTray))); }
        catch (Exception e) { Log("ERREUR : " + e.Message); }
    }

    // ---- window capture (move / minimize) ----

    // Move: the user drags the window, the new position is recorded. Minimize: the user clicks the window to minimize.
    void PickWindow(bool move)
    {
        if (Cur is not { } p || picker != null || rec != null) return;
        try
        {
            picker = new WindowPicker(this, move, h =>
            {
                StopPick();
                var w = Native.Describe(h);
                var s = new Step { Type = StepType.Window, Process = w.Process, Title = Native.StableTitle(w.Title, w.Process), WindowClass = w.Class, Action = move ? WindowAction.Deplacer : WindowAction.Reduire };
                if (move)
                {
                    Native.GetWindowRect(h, out var r);
                    var scr = Screen.FromHandle(h);
                    s.Zone = Zone.Manuel;
                    s.Monitor = scr.DeviceName;
                    s.X = r.Left - scr.Bounds.X;
                    s.Y = r.Top - scr.Bounds.Y;
                    s.W = r.Right - r.Left;
                    s.H = r.Bottom - r.Top;
                    s.Maximize = Native.IsZoomed(h);
                }
                AddStep(s);
                Log("Capturé : " + s);
            });
        }
        catch (Exception e) { Log("ERREUR : " + e.Message); return; }

        ShowPickBar(move ? "Déplacer une fenêtre" : "Réduire une fenêtre",
            move ? "Déplacez (ou redimensionnez) maintenant la fenêtre voulue : sa nouvelle position sera enregistrée." : "Cliquez sur la fenêtre à réduire.");
    }

    // Tray: the user clicks the icon (open the hidden-icons flyout first if needed); the click goes through and is recorded.
    void PickTray()
    {
        if (Cur == null || picker != null || rec != null) return;
        try
        {
            picker = new TrayPicker(this, (name, right) =>
            {
                StopPick();
                var s = new Step { Type = StepType.Tray, TrayName = name, Button = right ? MouseBtn.Right : MouseBtn.Left };
                AddStep(s);
                Log("Capturé : " + s);
            });
        }
        catch (Exception e) { Log("ERREUR : " + e.Message); return; }
        ShowPickBar("Icône de la zone de notification", "Cliquez sur l'icône de l'application dans la zone de notification (ouvrez d'abord les icônes cachées si besoin).");
    }

    void ShowPickBar(string title, string text)
    {
        pickBar = new Form
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow, TopMost = true, ShowInTaskbar = false, ControlBox = false,
            StartPosition = FormStartPosition.Manual, Text = title, ClientSize = new Size(420, 70),
        };
        var wa = Screen.PrimaryScreen!.WorkingArea;
        pickBar.Location = new Point(wa.X + (wa.Width - pickBar.Width) / 2, wa.Y + 8);
        var label = new Label { Dock = DockStyle.Top, Height = 38, TextAlign = ContentAlignment.MiddleCenter, Text = text };
        var cancel = new Button { Text = "Annuler", Dock = DockStyle.Fill };
        cancel.Click += (_, _) => StopPick();
        pickBar.Controls.Add(cancel);
        pickBar.Controls.Add(label);
        pickBar.Show();
        WindowState = FormWindowState.Minimized;
    }

    void StopPick()
    {
        if (picker == null) return;
        picker.Dispose();
        picker = null;
        pickBar?.Close();
        pickBar = null;
        if (!IsDisposed && Visible) { WindowState = FormWindowState.Normal; Activate(); }
    }

    // ---- recording ----

    void ToggleRec()
    {
        if (rec != null) { StopRec(); return; }
        if (Cur is not { } p) return;
        try { rec = new Recorder(p.Steps, () => BeginInvoke(() => { RefreshSteps(p.Steps.Count - 1); cfg.Save(); })); }
        catch (Exception e) { Log("ERREUR : " + e.Message); return; }

        btnRec.Text = "■  Arrêter l'enregistrement";
        recBar = new Form
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow, TopMost = true, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Text = "Enregistrement des clics", ClientSize = new Size(260, 44),
        };
        var wa = Screen.PrimaryScreen!.WorkingArea;
        recBar.Location = new Point(wa.X + (wa.Width - recBar.Width) / 2, wa.Y + 8);
        var stop = new Button { Text = "■ Arrêter", Dock = DockStyle.Fill };
        stop.Click += (_, _) => StopRec();
        recBar.ControlBox = false;
        recBar.Controls.Add(stop);
        recBar.Show();
        WindowState = FormWindowState.Minimized;
    }

    void StopRec()
    {
        if (rec == null) return;
        rec.Dispose();
        rec = null;
        recBar?.Close();
        recBar = null;
        btnRec.Text = "●  Enregistrer les clics";
        if (!IsDisposed && Visible) { WindowState = FormWindowState.Normal; Activate(); }
    }
}

class ImportDialog : Form
{
    public List<Step> Result { get; } = new();
    readonly CheckedListBox clb = new() { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    readonly CheckBox chkLaunch = new() { Text = "Inclure le lancement de l'application", Checked = true, AutoSize = true };
    readonly List<Native.Win> wins;

    public ImportDialog()
    {
        Text = "Importer les fenêtres ouvertes (position + écran)";
        Size = new Size(720, 460);
        StartPosition = FormStartPosition.CenterParent;
        wins = Native.TopWindows().Where(w => w.Pid != Environment.ProcessId && w.Process != "").ToList();
        foreach (var w in wins) clb.Items.Add($"{w.Process}  —  {w.Title}");

        var ok = new Button { Text = "Importer", AutoSize = true, DialogResult = DialogResult.OK };
        ok.Click += (_, _) => Build();
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        bottom.Controls.AddRange(new Control[] { chkLaunch, ok });
        Controls.Add(clb);
        Controls.Add(bottom);
        AcceptButton = ok;
    }

    void Build()
    {
        foreach (int i in clb.CheckedIndices)
        {
            var w = wins[i];
            Native.GetWindowRect(w.H, out var r);
            var scr = Screen.FromHandle(w.H);
            if (chkLaunch.Checked && w.Path != "")
                Result.Add(new Step { Type = StepType.Launch, Exe = w.Path, DelayMs = 500 });
            Result.Add(new Step
            {
                Type = StepType.Place, DelayMs = 300, Process = w.Process, Title = Native.StableTitle(w.Title, w.Process), WindowClass = w.Class, Monitor = scr.DeviceName,
                X = r.Left - scr.Bounds.X, Y = r.Top - scr.Bounds.Y, W = r.Right - r.Left, H = r.Bottom - r.Top,
                Maximize = Native.IsZoomed(w.H),
            });
        }
    }
}
