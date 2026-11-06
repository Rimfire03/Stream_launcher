using System.Text.Json;

/// <summary>
/// Export / import of the whole configuration in one file: profiles (steps), screen names, favourites, history and chat settings.
/// Never the accounts (tokens stay in streaming-auth.bin, encrypted for this Windows user), so the file can be moved to another PC.
/// </summary>
static class ConfigBackup
{
    const int Format = 1;

    public sealed class Bundle
    {
        public int FormatVersion { get; set; }
        public string App { get; set; } = "";
        public DateTime ExportedAt { get; set; }
        public Config Config { get; set; } = new();
        public StreamingStore Streaming { get; set; } = new();
        public List<ScreenInfo> Screens { get; set; } = new(); // the PC's screens at export time, to map them on another PC
    }

    public sealed record ScreenInfo(string Device, string Name, int Width, int Height);

    static string Describe(string device, string name, int w, int h) => (name != "" ? $"{name} ({device})" : device) + (w > 0 ? $"  {w}x{h}" : "");

    /// <summary>
    /// When the screens of the file are not the PC's (missing device, other resolution, other count), asks which real screen each screen of the file becomes.
    /// </summary>
    /// <returns>imported device -> real device (empty: nothing to change), or null when cancelled</returns>
    public static Dictionary<string, string>? AskScreens(IWin32Window owner, Bundle b)
    {
        var real = Screen.AllScreens;
        var file = b.Screens.ToDictionary(s => s.Device, s => s, StringComparer.OrdinalIgnoreCase);
        foreach (var dev in b.Config.Profiles.SelectMany(p => p.Steps).Select(s => s.Monitor).Where(m => m != "").Distinct(StringComparer.OrdinalIgnoreCase))
            if (!file.ContainsKey(dev)) file[dev] = new ScreenInfo(dev, b.Config.ScreenNames.GetValueOrDefault(dev, ""), 0, 0);
        if (file.Count == 0) return new();

        bool same = b.Screens.Count == 0 || b.Screens.Count == real.Length;
        foreach (var s in file.Values)
        {
            var r = real.FirstOrDefault(x => x.DeviceName.Equals(s.Device, StringComparison.OrdinalIgnoreCase));
            if (r == null || (s.Width > 0 && (r.Bounds.Width != s.Width || r.Bounds.Height != s.Height))) same = false;
        }
        if (same) return new();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Suggest(ScreenInfo s) // same device and size, else a free screen of the same size, else a free one
        {
            Screen? pick = real.FirstOrDefault(x => x.DeviceName.Equals(s.Device, StringComparison.OrdinalIgnoreCase) && (s.Width == 0 || x.Bounds.Width == s.Width && x.Bounds.Height == s.Height) && !used.Contains(x.DeviceName))
                ?? real.FirstOrDefault(x => s.Width > 0 && x.Bounds.Width == s.Width && x.Bounds.Height == s.Height && !used.Contains(x.DeviceName))
                ?? real.FirstOrDefault(x => !used.Contains(x.DeviceName)) ?? real[0];
            used.Add(pick.DeviceName);
            return pick.DeviceName;
        }

        using var f = new Form
        {
            Text = "Correspondance des écrans", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, BackColor = Ui.Bg, ForeColor = Ui.Text, Font = Ui.Base, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16),
        };
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top };
        var combos = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in file.Values.OrderBy(s => s.Device, StringComparer.OrdinalIgnoreCase))
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, Margin = new Padding(0, 4, 0, 4) };
            foreach (var r in real) cb.Items.Add(new Choice(r.DeviceName, Describe(r.DeviceName, Config.Current.ScreenNames.GetValueOrDefault(r.DeviceName, ""), r.Bounds.Width, r.Bounds.Height) + (r.Primary ? "  (principal)" : "")));
            var wanted = Suggest(s);
            cb.SelectedIndex = Math.Max(0, Array.FindIndex(real, r => r.DeviceName == wanted));
            combos[s.Device] = cb;
            grid.Controls.Add(new Label { Text = Describe(s.Device, s.Name, s.Width, s.Height), AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            grid.Controls.Add(new Label { Text = "→", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            grid.Controls.Add(cb);
        }
        var head = new Label { Text = "Les écrans de la configuration importée ne sont pas ceux de ce PC.\nChoisis quel écran réel remplace chaque écran du fichier :", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 10) };
        var ok = Ui.Btn("Importer", Ui.Kind.Primary, (_, _) => f.DialogResult = DialogResult.OK);
        var cancel = Ui.Btn("Annuler", Ui.Kind.Secondary, (_, _) => f.DialogResult = DialogResult.Cancel);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        f.Controls.Add(buttons);
        f.Controls.Add(grid);
        f.Controls.Add(head);
        f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        return combos.ToDictionary(kv => kv.Key, kv => ((Choice)kv.Value.SelectedItem!).Device, StringComparer.OrdinalIgnoreCase);
    }

    sealed record Choice(string Device, string Label) { public override string ToString() => Label; }

    /// <param name="obsPasswords">false: the OBS WebSocket passwords of the steps are left empty in the file</param>
    public static void Export(Config cfg, string path, bool obsPasswords)
    {
        var profiles = JsonSerializer.Deserialize<List<Profile>>(JsonSerializer.Serialize(cfg.Profiles, Config.JsonOpt), Config.JsonOpt) ?? new(); // deep copy
        if (!obsPasswords) foreach (var s in profiles.SelectMany(p => p.Steps)) s.ObsPassword = "";
        var live = StreamingStore.Current;
        var store = new StreamingStore
        {
            Favorites = live.Favorites, History = live.History, Skipped = live.Skipped,
            ChatOff = live.ChatOff, ChatHidden = live.ChatHidden, ChatShown = live.ChatShown, ChatFontSize = live.ChatFontSize,
            ChatSoundOn = live.ChatSoundOn, ChatSoundDelay = live.ChatSoundDelay, ChatSoundFile = live.ChatSoundFile, ChatExcluded = live.ChatExcluded,
        };
        var bundle = new Bundle
        {
            FormatVersion = Format, App = "Stream Launcher " + Updater.Format(Updater.Current), ExportedAt = DateTime.Now,
            Config = new Config { Profiles = profiles, ScreenNames = new(cfg.ScreenNames) }, Streaming = store,
            Screens = Screen.AllScreens.Select(s => new ScreenInfo(s.DeviceName, cfg.ScreenNames.GetValueOrDefault(s.DeviceName, ""), s.Bounds.Width, s.Bounds.Height)).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(bundle, Config.JsonOpt));
    }

    /// <exception cref="InvalidDataException">not a Stream Launcher configuration file</exception>
    public static Bundle Read(string path)
    {
        Bundle? b;
        try { b = JsonSerializer.Deserialize<Bundle>(File.ReadAllText(path), Config.JsonOpt); }
        catch (JsonException e) { throw new InvalidDataException("fichier illisible : " + e.Message); }
        if (b == null || b.FormatVersion < 1 || b.Config.Profiles.Count == 0 && b.Streaming.Favorites.Count == 0)
            throw new InvalidDataException("ce fichier ne contient pas de configuration Stream Launcher");
        if (b.FormatVersion > Format) throw new InvalidDataException("fichier créé par une version plus récente de Stream Launcher");
        return b;
    }

    /// <summary>Replaces profiles, screen names and the streaming settings; the current profiles.json is kept as profiles.json.bak. Accounts are untouched.</summary>
    /// <param name="screens">file screen -> real screen (see AskScreens); steps and screen names follow it</param>
    public static void Apply(Config cfg, Bundle b, Dictionary<string, string> screens)
    {
        var file = Path.Combine(Deck.AppDir, "profiles.json");
        if (File.Exists(file)) File.Copy(file, file + ".bak", true);

        foreach (var st in b.Config.Profiles.SelectMany(p => p.Steps)) // each step is remapped once from its own value, so swapped screens stay swapped
            if (screens.TryGetValue(st.Monitor, out var real)) st.Monitor = real;
        cfg.Profiles.Clear();
        cfg.Profiles.AddRange(b.Config.Profiles);
        foreach (var (dev, name) in b.Config.ScreenNames) cfg.ScreenNames[screens.GetValueOrDefault(dev, dev)] = name;
        cfg.Save();

        var s = StreamingStore.Current;
        s.Favorites = b.Streaming.Favorites; s.History = b.Streaming.History; s.Skipped = b.Streaming.Skipped;
        s.ChatOff = b.Streaming.ChatOff; s.ChatHidden = b.Streaming.ChatHidden; s.ChatShown = b.Streaming.ChatShown; s.ChatFontSize = b.Streaming.ChatFontSize;
        s.ChatSoundOn = b.Streaming.ChatSoundOn; s.ChatSoundDelay = b.Streaming.ChatSoundDelay; s.ChatSoundFile = b.Streaming.ChatSoundFile; s.ChatExcluded = b.Streaming.ChatExcluded;
        s.Save();
    }
}
