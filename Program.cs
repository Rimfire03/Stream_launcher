static class Program
{
    // StreamLauncher.exe                    -> GUI (+ tray icon)
    // StreamLauncher.exe --tray             -> GUI started hidden in the tray
    // StreamLauncher.exe --list-tray       -> prints the notification-area icon names (stdout, when redirected)
    // StreamLauncher.exe --profile "Nom"    -> toggle, headless (Stream Deck): starts the profile, or closes its windows if it is live; exit code 0 ok, 1 step errors, 2 unknown profile
    // StreamLauncher.exe --start "Nom" / --stop "Nom" -> force one direction
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var cfg = Config.Load();

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
            var p = cfg.Profiles.FirstOrDefault(x => x.Name.Equals(args[i + 1], StringComparison.OrdinalIgnoreCase));
            if (p == null) return 2;
            bool stop = flag == "--stop" || (flag == "--profile" && Live.IsLive(p.Name));
            if (stop) return Task.Run(() => Live.Close(p.Name, _ => { })).GetAwaiter().GetResult() >= 0 ? 0 : 1;
            return Task.Run(() => Live.Run(p, new Runner(), CancellationToken.None)).GetAwaiter().GetResult() == 0 ? 0 : 1;
        }

        using var mutex = new Mutex(true, "TomLine.StreamLauncher.Gui", out var first);
        if (!first) return 0;
        Application.Run(new MainForm(cfg, args.Contains("--tray")));
        return 0;
    }
}
