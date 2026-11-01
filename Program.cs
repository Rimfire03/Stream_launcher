static class Program
{
    // StreamLauncher.exe                    -> GUI (+ tray icon); a second launch just brings the open window to the front
    // StreamLauncher.exe --chat             -> opens the unified chat window (starts the GUI hidden in the tray if needed)
    // StreamLauncher.exe --tray             -> GUI started hidden in the tray
    // StreamLauncher.exe --list-tray        -> prints the notification-area icon names (stdout, when redirected)
    // StreamLauncher.exe --profile "Nom"    -> toggle, headless (Stream Deck): starts the profile, or closes its windows if it is live; exit code 0 ok, 1 step errors, 2 unknown profile
    // StreamLauncher.exe --start "Nom" / --stop "Nom" -> force one direction
    // Every launch is traced in %LOCALAPPDATA%\StreamLauncher\logs\launcher.log (see RunLog).
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var cfg = Config.Load();
        // « nom - bascule.exe » & co: the launcher's file name carries the action (the stub passes it as --link)
        int link = Array.IndexOf(args, "--link");
        if (link >= 0 && link + 1 < args.Length)
        {
            var linkName = args[link + 1];
            RunLog.Write($"lancement via {linkName}");
            args = Deck.FromExeName(cfg, linkName) ?? args;
        }
        else if (args.Length == 0 && Deck.FromExeName(cfg) is { } fromName)
        {
            args = fromName;
            RunLog.Write($"lancement via {Path.GetFileName(Environment.ProcessPath)}");
        }
        RunLog.Write($"lancement : {(args.Length == 0 ? "(sans argument)" : string.Join(" ", args))}");

        if (args.Contains("--list-tray"))
        {
            foreach (var n in Task.Run(Runner.ListTray).GetAwaiter().GetResult()) Console.WriteLine(n);
            return 0;
        }

        // --profile toggles (first call starts, next call closes the live); --start / --stop force one direction
        foreach (var flag in new[] { "--profile", "--start", "--stop" })
        {
            int i = Array.IndexOf(args, flag);
            if (i < 0 || i + 1 >= args.Length) continue;
            // the name runs up to the next option: a launcher that forgot the quotes still works
            var name = string.Join(" ", args.Skip(i + 1).TakeWhile(x => !x.StartsWith("--")));
            var p = cfg.Profiles.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (p == null)
            {
                RunLog.Write($"profil introuvable : « {name} » (profils : {string.Join(", ", cfg.Profiles.Select(x => x.Name))})");
                return 2;
            }
            bool stop = flag == "--stop" || (flag == "--profile" && Live.IsLive(p.Name));
            using var lease = Live.Enter(p.Name);
            if (lease == null)
            {
                RunLog.Write($"[{p.Name}] déjà en cours d'exécution : appel ignoré");
                return 0;
            }
            try
            {
                if (stop)
                {
                    RunLog.Write($"[{p.Name}] fermeture du live");
                    int n = Task.Run(() => Live.Close(p.Name, RunLog.Write)).GetAwaiter().GetResult();
                    RunLog.Write($"[{p.Name}] {n} fenêtre(s) fermée(s)");
                    return 0;
                }
                RunLog.Write($"[{p.Name}] lancement ({p.Steps.Count} étapes)");
                int errors = Task.Run(() => Live.Run(p, new Runner { Log = RunLog.Write, OpenChat = () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--chat") { UseShellExecute = false }) }, CancellationToken.None)).GetAwaiter().GetResult();
                RunLog.Write($"[{p.Name}] terminé, {errors} erreur(s)");
                return errors == 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                RunLog.Write($"[{p.Name}] ERREUR : {e}");
                return 1;
            }
        }

        using var mutex = new Mutex(true, "TomLine.StreamLauncher.Gui", out var first);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "TomLine.StreamLauncher.Show");
        using var chatEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "TomLine.StreamLauncher.Chat");
        if (!first && args.Contains("--chat"))
        {
            chatEvent.Set(); // the running application opens its chat window
            return 0;
        }
        if (!first)
        {
            // already open: bring that window forward instead of exiting silently
            if (args.Length == 0) RunLog.Write("instance déjà ouverte : fenêtre affichée (aucun argument reçu, voir « Raccourcis Stream Deck » dans l'aide)");
            showEvent.Set();
            return 0;
        }

        var form = new MainForm(cfg, args.Contains("--tray") || args.Contains("--chat")) { OpenChatOnStart = args.Contains("--chat") };
        new Thread(() =>
        {
            while (showEvent.WaitOne())
                if (form.IsHandleCreated && !form.IsDisposed) form.BeginInvoke(form.ShowMain);
        }) { IsBackground = true }.Start();
        new Thread(() =>
        {
            while (chatEvent.WaitOne())
                if (form.IsHandleCreated && !form.IsDisposed) form.BeginInvoke(form.OpenChatWindow);
        }) { IsBackground = true }.Start();
        using var chatCloseEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "TomLine.StreamLauncher.ChatClose");
        new Thread(() =>
        {
            while (chatCloseEvent.WaitOne())
                if (form.IsHandleCreated && !form.IsDisposed) form.BeginInvoke(form.CloseChatWindow);
        }) { IsBackground = true }.Start();
        Application.Run(form);
        return 0;
    }
}

/// <summary>Démarrage avec Windows : valeur « Run » de l'utilisateur, l'application démarre masquée dans la zone de notification (--tray).</summary>
static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "StreamLauncher";

    public static bool Enabled
    {
        get { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(Name) != null; }
    }

    public static void Set(bool on)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\" --tray");
        else k.DeleteValue(Name, false);
    }
}

/// <summary>Journal des lancements en ligne de commande (Stream Deck) : la ligne de commande n'a pas de fenêtre pour afficher les erreurs.</summary>
static class RunLog
{
    static readonly object Gate = new();
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamLauncher", "logs");
    public static string FilePath => Path.Combine(Dir, "launcher.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024) File.Move(FilePath, FilePath + ".old", true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            }
        }
        catch { } // logging must never break a launch
    }
}
