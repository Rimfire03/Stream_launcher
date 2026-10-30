using System.Text.Json;

/// <summary>
/// "Live" state of a profile, persisted on disk so the GUI, the tray menu and the command line (Stream Deck) share it.
/// A first launch runs the profile and records which windows existed before; the next press closes every window
/// that was opened since (plus the ones the profile placed or clicked), then forgets the state.
/// </summary>
static class Live
{
    record WinRef(long H, uint Pid);
    record State(DateTime Started, long[] Before, WinRef[] Touched, string[]? Keep = null); // Keep: process names left open at stop

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamLauncher", "live");

    static string FileOf(string profile) =>
        Path.Combine(Dir, string.Concat(profile.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");

    public static bool IsLive(string profile) => File.Exists(FileOf(profile));

    /// <summary>Runs the profile and records the live state (even when cancelled midway, so it can still be closed).</summary>
    public static int Run(Profile p, Runner runner, CancellationToken ct)
    {
        var before = Native.TopWindows().Select(w => (long)w.H).ToArray();
        try { return runner.Run(p, ct); }
        finally
        {
            Directory.CreateDirectory(Dir);
            var keep = p.Steps.Where(s => s.Type == StepType.Launch && s.KeepOpen && !string.IsNullOrWhiteSpace(s.Exe))
                .Select(s => Path.GetFileNameWithoutExtension(s.Exe)).ToArray();
            var state = new State(DateTime.Now, before, runner.Touched.Select(t => new WinRef((long)t.H, t.Pid)).ToArray(), keep);
            File.WriteAllText(FileOf(p.Name), JsonSerializer.Serialize(state));
        }
    }

    /// <summary>Asks every window opened since the profile started to close (WM_CLOSE, so unsaved work still gets its prompt).</summary>
    /// <returns>number of windows asked to close</returns>
    public static int Close(string profile, Action<string> log)
    {
        var file = FileOf(profile);
        if (!File.Exists(file)) return 0;
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(file)) ?? new State(DateTime.Now, Array.Empty<long>(), Array.Empty<WinRef>());
        var before = state.Before.ToHashSet();
        var own = (uint)Environment.ProcessId;

        var targets = new Dictionary<IntPtr, Native.Win>();
        foreach (var w in Native.TopWindows())
            if (w.Pid != own && !before.Contains((long)w.H) && !w.Path.StartsWith(Deck.AppDir, StringComparison.OrdinalIgnoreCase)) targets[w.H] = w; // never our own app, whichever instance
        foreach (var t in state.Touched)
        {
            var h = (IntPtr)t.H;
            if (!Native.IsWindow(h)) continue;
            Native.GetWindowThreadProcessId(h, out var pid);
            if (pid == t.Pid && pid != own) targets[h] = Native.Describe(h); // same pid: guards against a reused handle
        }

        var keep = (state.Keep ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var w in targets.Values.Where(w => keep.Contains(w.Process)).ToList())
        {
            log($"  gardée ouverte : {w.Process} '{w.Title}'");
            targets.Remove(w.H);
        }
        foreach (var w in targets.Values)
        {
            log($"  fermeture : {w.Process} '{w.Title}'");
            Native.PostMessage(w.H, 0x10, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
        }
        File.Delete(file);
        return targets.Count;
    }
}
