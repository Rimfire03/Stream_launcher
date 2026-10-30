/// <summary>
/// Raccourcis Stream Deck. L'action « Ouvrir » de Stream Deck n'a pas de champ d'arguments, ignore les arguments d'un .lnk
/// et refuse les .cmd : elle n'accepte qu'un exécutable. Pour chaque profil on crée donc, dans « Profils\nom », trois
/// copies d'un lanceur de 4 Ko (assets\LaunchStub.exe) dont le NOM porte l'action : « nom - bascule.exe », « nom - lancer.exe »,
/// « nom - fermer.exe ». Des liens physiques de StreamLauncher.exe ne marchent pas : Stream Deck les résout vers l'exe d'origine
/// et perd le nom. Le lanceur démarre StreamLauncher.exe --link "<son nom>" (voir <see cref="FromExeName"/>).
/// Des .lnk équivalents sont créés pour un usage manuel.
/// </summary>
static class Deck
{
    public static readonly (string Suffix, string Flag)[] Kinds = { (" - bascule", "--profile"), (" - lancer", "--start"), (" - fermer", "--stop") };
    const string Marker = "Stream Launcher :"; // description of our shortcuts: only those are ever deleted

    public static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>Application folder: the exe folder, or two levels up when running as a profile link (Profils\nom\nom - bascule.exe).</summary>
    public static string AppDir
    {
        get
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath)!;
            var parent = Directory.GetParent(dir);
            return parent != null && parent.Name.Equals("Profils", StringComparison.OrdinalIgnoreCase) && parent.Parent != null ? parent.Parent.FullName : dir;
        }
    }

    static string RealExe => Path.Combine(AppDir, "StreamLauncher.exe");

    /// <summary>When this process was started through a profile link, the arguments that link stands for.</summary>
    public static string[]? FromExeName(Config cfg, string? fileName = null)
    {
        var self = Path.GetFileNameWithoutExtension(fileName ?? Environment.ProcessPath);
        if (self == null) return null;
        foreach (var (suffix, flag) in Kinds)
        {
            if (!self.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var safeName = self[..^suffix.Length];
            var p = cfg.Profiles.FirstOrDefault(x => Safe(x.Name).Equals(safeName, StringComparison.OrdinalIgnoreCase));
            return p == null ? null : new[] { flag, p.Name };
        }
        return null;
    }

    /// <summary>"Profils" folder next to the exe; in Documents when the application folder is not writable.</summary>
    public static string Root(out bool inAppDir)
    {
        var dir = Path.Combine(AppDir, "Profils");
        inAppDir = true;
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return dir;
        }
        catch (UnauthorizedAccessException)
        {
            inAppDir = false;
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Stream Launcher", "Profils");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string Root() => Root(out _);

    public static string FolderOf(Profile p) => Path.Combine(Root(), Safe(p.Name));

    /// <summary>Creates or refreshes the shortcuts of every profile, and drops the folders of profiles that no longer exist.</summary>
    public static void Sync(IEnumerable<Profile> profiles)
    {
        var root = Root(out var inAppDir);
        var exe = RealExe;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in profiles)
        {
            var dir = Path.Combine(root, Safe(p.Name));
            wanted.Add(dir);
            Directory.CreateDirectory(dir);
            foreach (var (suffix, flag) in Kinds)
            {
                var baseName = Path.Combine(dir, Safe(p.Name) + suffix);

                dynamic lnk = shell.CreateShortcut(baseName + ".lnk");
                lnk.TargetPath = exe;
                lnk.Arguments = $"{flag} \"{p.Name}\"";
                lnk.WorkingDirectory = Path.GetDirectoryName(exe);
                lnk.IconLocation = exe + ",0";
                lnk.Description = $"{Marker} {p.Name}";
                lnk.Save();

                // the stub finds StreamLauncher.exe two folders up, so only inside the application folder
                if (inAppDir) TryStub(baseName + ".exe");
                if (File.Exists(baseName + ".cmd")) TryDelete(baseName + ".cmd"); // earlier versions made these; Stream Deck refuses them
            }
        }

        // renamed or deleted profiles: remove our own files there, then the folder if it ended up empty
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (wanted.Contains(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.lnk"))
            {
                dynamic lnk = shell.CreateShortcut(f);
                if (((string)lnk.Description).StartsWith(Marker)) File.Delete(f);
            }
            foreach (var f in Directory.GetFiles(dir, "*.cmd"))
                if (File.ReadLines(f).Take(2).Any(l => l.Contains(Marker))) TryDelete(f);
            foreach (var f in Directory.GetFiles(dir, "*.exe"))
                if (Kinds.Any(k => Path.GetFileNameWithoutExtension(f).EndsWith(k.Suffix, StringComparison.OrdinalIgnoreCase)) && IsOurs(f, exe)) TryDelete(f);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
    }

    // a stub copy, or a hard link of the exe made by version 0.1.15
    static bool IsOurs(string f, string exe) => new FileInfo(f).Length is var n && (n == StubBytes().Length || n == new FileInfo(exe).Length);

    static byte[] StubBytes()
    {
        using var s = typeof(Deck).Assembly.GetManifestResourceStream("LaunchStub.exe")!;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    // always re-written; a running launcher (locked) is left as is
    static void TryStub(string path)
    {
        try { File.WriteAllBytes(path, StubBytes()); } catch { }
    }
}
