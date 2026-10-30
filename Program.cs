static class Program
{
    // StreamLauncher.exe                    -> GUI (+ tray icon)
    // StreamLauncher.exe --tray             -> GUI started hidden in the tray
    // StreamLauncher.exe --profile "Nom"    -> run the profile headless and exit (Stream Deck); exit code 0 ok, 1 step errors, 2 unknown profile
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var cfg = Config.Load();

        int i = Array.IndexOf(args, "--profile");
        if (i >= 0 && i + 1 < args.Length)
        {
            var p = cfg.Profiles.FirstOrDefault(x => x.Name.Equals(args[i + 1], StringComparison.OrdinalIgnoreCase));
            if (p == null) return 2;
            return Task.Run(() => new Runner().Run(p, CancellationToken.None)).GetAwaiter().GetResult() == 0 ? 0 : 1;
        }

        using var mutex = new Mutex(true, "TomLine.StreamLauncher.Gui", out var first);
        if (!first) return 0;
        Application.Run(new MainForm(cfg, args.Contains("--tray")));
        return 0;
    }
}
