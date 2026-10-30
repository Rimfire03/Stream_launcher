/// <summary>
/// Raccourcis Stream Deck : l'action « Ouvrir » de Stream Deck n'a pas de champ d'arguments, un .lnk les porte.
/// Un sous-dossier par profil dans le dossier « Profils » de l'application, maintenu automatiquement.
/// </summary>
static class Deck
{
    static readonly (string Suffix, string Flag)[] Kinds = { (" - bascule", "--profile"), (" - lancer", "--start"), (" - fermer", "--stop") };
    const string Marker = "Stream Launcher :"; // description of our shortcuts: only those are ever deleted

    static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>"Profils" folder next to the exe; in Documents when the application folder is not writable.</summary>
    public static string Root()
    {
        var dir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Profils");
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
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Stream Launcher", "Profils");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string FolderOf(Profile p) => Path.Combine(Root(), Safe(p.Name));

    /// <summary>Creates or refreshes the 3 shortcuts of every profile, and drops the folders of profiles that no longer exist.</summary>
    public static void Sync(IEnumerable<Profile> profiles)
    {
        var root = Root();
        var exe = Environment.ProcessPath!;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in profiles)
        {
            var dir = Path.Combine(root, Safe(p.Name));
            wanted.Add(dir);
            Directory.CreateDirectory(dir);
            foreach (var (suffix, flag) in Kinds)
            {
                dynamic lnk = shell.CreateShortcut(Path.Combine(dir, Safe(p.Name) + suffix + ".lnk"));
                lnk.TargetPath = exe;
                lnk.Arguments = $"{flag} \"{p.Name}\"";
                lnk.WorkingDirectory = Path.GetDirectoryName(exe);
                lnk.IconLocation = exe + ",0";
                lnk.Description = $"{Marker} {p.Name}";
                lnk.Save();
            }
        }

        // renamed or deleted profiles: remove our own shortcuts there, then the folder if it ended up empty
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (wanted.Contains(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.lnk"))
            {
                dynamic lnk = shell.CreateShortcut(f);
                if (((string)lnk.Description).StartsWith(Marker)) File.Delete(f);
            }
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
    }
}
