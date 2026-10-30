class MainForm : Form
{
    readonly Config cfg;
    readonly NotifyIcon tray = new() { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Text = "Stream Launcher" };
    readonly ListBox lbProf = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly ListBox lbSteps = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    readonly PropertyGrid pg = new() { Dock = DockStyle.Fill };
    readonly TextBox log = new() { Dock = DockStyle.Bottom, Height = 110, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly FlowLayoutPanel quick = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
    readonly Button btnRun, btnStop, btnRec;
    bool allowShow, exiting, refreshing;
    CancellationTokenSource? cts;
    Recorder? rec;
    WindowPicker? picker;
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
        Size = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;

        // left: profiles
        var profBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        profBtns.Controls.AddRange(new Control[]
        {
            Btn("Nouveau", (_, _) => NewProfile()),
            Btn("Renommer", (_, _) => RenameProfile()),
            Btn("Supprimer", (_, _) => DeleteProfile()),
        });
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 210, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(lbProf);
        split.Panel1.Controls.Add(profBtns);

        // right: toolbar + steps + properties
        btnRun = Btn("▶ Lancer", (_, _) => { if (Cur != null) RunProfile(Cur); });
        btnStop = Btn("■ Stop", (_, _) => cts?.Cancel());
        btnStop.Enabled = false;
        btnRec = Btn("● Enregistrer les clics", (_, _) => ToggleRec());
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        bar.Controls.AddRange(new Control[]
        {
            btnRun, btnStop, btnRec,
            Btn("Importer fenêtres ouvertes…", (_, _) => ImportWindows()),
            Btn("+ Application…", (_, _) => AddApp()),
            Btn("+ Clic", (_, _) => AddStep(new Step { Type = StepType.Click })),
            Btn("+ Déplacer fenêtre", (_, _) => PickWindow(true)),
            Btn("+ Réduire fenêtre", (_, _) => PickWindow(false)),
            Btn("↑", (_, _) => MoveStep(-1)),
            Btn("↓", (_, _) => MoveStep(1)),
            Btn("Supprimer l'étape", (_, _) => DeleteStep()),
            Btn("Identifier les écrans", (_, _) => IdentifyScreens()),
            Btn("Renommer les écrans…", (_, _) => RenameScreens()),
            Btn("Mises à jour", (_, _) => CheckUpdate(true)),
        });
        var split2 = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 520 };
        split2.Panel1.Controls.Add(lbSteps);
        split2.Panel2.Controls.Add(pg);
        split.Panel2.Controls.Add(split2);
        split.Panel2.Controls.Add(bar);

        Controls.Add(split);
        Controls.Add(log);
        Controls.Add(quick);

        lbProf.SelectedIndexChanged += (_, _) => RefreshSteps();
        lbSteps.SelectedIndexChanged += (_, _) => { if (!refreshing) pg.SelectedObject = CurStep; };
        pg.PropertyValueChanged += (_, _) => { RefreshSteps(); cfg.Save(); };

        foreach (Control c in new Control[] { this, lbSteps, pg })
        {
            c.AllowDrop = true;
            c.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            c.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) DropFiles(files); };
        }

        tray.Visible = true;
        tray.DoubleClick += (_, _) => ShowMain();

        RebuildProfiles();
        if (lbProf.Items.Count > 0) lbProf.SelectedIndex = 0;
        Log("Écrans : " + string.Join(" | ", Screen.AllScreens.Select(s =>
            $"{s.DeviceName} {s.Bounds.Width}x{s.Bounds.Height} @{s.Bounds.X},{s.Bounds.Y}{(s.Primary ? " (principal)" : "")}")));
        HandleCreated += (_, _) => BeginInvoke(() => CheckUpdate(false));
    }

    async void CheckUpdate(bool manual)
    {
        try
        {
            var up = await Updater.CheckAsync();
            if (up is not { } u)
            {
                if (manual) MessageBox.Show("Version à jour : " + Updater.Format(Updater.Current), Text);
                return;
            }
            Log($"Mise à jour disponible : {Updater.Format(u.Ver)}");
            if (!manual && !allowShow) { tray.ShowBalloonTip(5000, "Stream Launcher", $"Mise à jour {Updater.Format(u.Ver)} disponible (ouvrir l'app pour l'installer)", ToolTipIcon.Info); return; }
            if (MessageBox.Show($"Installer {Updater.Format(u.Ver)} (actuelle : {Updater.Format(Updater.Current)}) ?\nL'application va redémarrer.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            await Updater.DownloadAndRestartAsync(u.Url);
            exiting = true;
            Close();
        }
        catch (Exception e) { if (manual) Log("ERREUR mise à jour : " + e.Message); }
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
            quick.Controls.Add(Btn("▶ " + prof.Name, (_, _) => RunProfile(prof)));
            menu.Items.Add("▶ " + prof.Name, null, (_, _) => RunProfile(prof));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Identifier les écrans", null, (_, _) => IdentifyScreens());
        menu.Items.Add("Ouvrir", null, (_, _) => ShowMain());
        menu.Items.Add("Arrêter l'exécution", null, (_, _) => cts?.Cancel());
        menu.Items.Add("Quitter", null, (_, _) => { exiting = true; Close(); });
        tray.ContextMenuStrip = menu;
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
        pg.SelectedObject = CurStep;
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

    async void RunProfile(Profile p)
    {
        if (cts != null) return;
        cts = new CancellationTokenSource();
        SetBusy(true);
        if (Visible) WindowState = FormWindowState.Minimized; // stay out of the way of the clicks
        Log($"=== {p.Name} ===");
        var token = cts.Token;
        try
        {
            int err = await Task.Run(() => new Runner { Log = Log }.Run(p, token));
            Log(err == 0 ? "Terminé" : $"Terminé avec {err} erreur(s)");
        }
        catch (OperationCanceledException) { Log("Annulé"); }
        finally
        {
            cts.Dispose();
            cts = null;
            SetBusy(false);
            if (Visible) WindowState = FormWindowState.Normal;
        }
    }

    void SetBusy(bool busy)
    {
        btnRun.Enabled = !busy;
        btnStop.Enabled = busy;
        btnRec.Enabled = !busy;
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
                var s = new Step { Type = StepType.Window, Process = w.Process, Title = w.Title, Action = move ? WindowAction.Deplacer : WindowAction.Reduire };
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

        pickBar = new Form
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow, TopMost = true, ShowInTaskbar = false, ControlBox = false,
            StartPosition = FormStartPosition.Manual, Text = move ? "Déplacer une fenêtre" : "Réduire une fenêtre", ClientSize = new Size(420, 70),
        };
        var wa = Screen.PrimaryScreen!.WorkingArea;
        pickBar.Location = new Point(wa.X + (wa.Width - pickBar.Width) / 2, wa.Y + 8);
        var label = new Label
        {
            Dock = DockStyle.Top, Height = 38, TextAlign = ContentAlignment.MiddleCenter,
            Text = move ? "Déplacez (ou redimensionnez) maintenant la fenêtre voulue : sa nouvelle position sera enregistrée." : "Cliquez sur la fenêtre à réduire.",
        };
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

        btnRec.Text = "■ Arrêter l'enregistrement";
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
        btnRec.Text = "● Enregistrer les clics";
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
                Type = StepType.Place, DelayMs = 300, Process = w.Process, Title = w.Title, Monitor = scr.DeviceName,
                X = r.Left - scr.Bounds.X, Y = r.Top - scr.Bounds.Y, W = r.Right - r.Left, H = r.Bottom - r.Top,
                Maximize = Native.IsZoomed(w.H),
            });
        }
    }
}
