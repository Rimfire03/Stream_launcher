using System.Text.Json;

/// <summary>
/// "Live" state of a profile, persisted on disk so the GUI, the tray menu and the command line (Stream Deck) share it.
/// A first launch runs the profile and records which windows existed before; the next press closes every window
/// that was opened since (plus the ones the profile placed or clicked), then forgets the state.
/// </summary>
static class Live
{
    record WinRef(long H, uint Pid);
    record State(DateTime Started, long[] Before, WinRef[] Touched, string[]? Keep = null, long[]? KeepWindows = null, ObsStop[]? ObsStops = null, string[]? Owned = null, bool CloseChat = false, Dictionary<uint, long>? OwnedPids = null); // OwnedPids: pid -> start time ticks, so a reused pid is not mistaken for the original process

    static long StartOf(uint pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); return p.StartTime.Ticks; }
        catch { return 0; } // exited or not readable
    } // Keep: process names, KeepWindows: window handles, both left open at stop; Owned: processes launched by the profile (only these, plus Touched, are closed)

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamLauncher", "live");

    static string FileOf(string profile) =>
        Path.Combine(Dir, string.Concat(profile.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");

    public static bool IsLive(string profile) => File.Exists(FileOf(profile));

    /// <summary>One start or stop of a profile at a time, across the GUI and the command line (Stream Deck pressed twice).</summary>
    /// <returns>a lease to dispose when finished, or null when that profile is already being started or stopped</returns>
    public static IDisposable? Enter(string profile)
    {
        var sem = new Semaphore(1, 1, @"Local\TomLine.StreamLauncher.Run." + Deck.Safe(profile));
        if (sem.WaitOne(0)) return new Lease(sem);
        sem.Dispose();
        return null;
    }

    sealed class Lease(Semaphore sem) : IDisposable
    {
        public void Dispose() { sem.Release(); sem.Dispose(); }
    }

    /// <summary>Runs the profile and records the live state (even when cancelled midway, so it can still be closed).</summary>
    public static int Run(Profile p, Runner runner, CancellationToken ct)
    {
        var before = Native.TopWindows().Select(w => (long)w.H).ToArray();
        try { return runner.Run(p, ct); }
        finally
        {
            Directory.CreateDirectory(Dir);
            var keep = p.Steps.Where(s => s.Enabled && s.Type == StepType.Launch && s.KeepOpen && !string.IsNullOrWhiteSpace(s.Exe))
                .Select(s => Path.GetFileNameWithoutExtension(s.Exe)).Concat(runner.KeepProcesses).ToArray();
            var state = new State(DateTime.Now, before, runner.Touched.Select(t => new WinRef((long)t.H, t.Pid)).ToArray(), keep, runner.KeepWindows.Select(h => (long)h).ToArray(), runner.ObsStops.ToArray(),
                p.Steps.Where(s => s.Enabled && s.Type == StepType.Launch && !string.IsNullOrWhiteSpace(s.Exe)).Select(s => Path.GetFileNameWithoutExtension(s.Exe)).ToArray(),
                runner.ChatOpened && !runner.ChatKeep, Native.WithDescendants(runner.LaunchedPids).Select(pid => (pid, t: StartOf(pid))).Where(x => x.t != 0).ToDictionary(x => x.pid, x => x.t));
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
        foreach (var stop in state.ObsStops ?? Array.Empty<ObsStop>()) // OBS first: closing its window would cut the stream abruptly
        {
            foreach (var (on, kind, what) in new[] { (stop.Stream, ObsActionKind.StopStream, "live"), (stop.Record, ObsActionKind.StopRecord, "enregistrement") })
            {
                if (!on) continue;
                try { ObsSession.RunAction(stop.Host, stop.Password, kind, 10000, CancellationToken.None); log($"  OBS : {what} arrêté"); }
                catch (Exception e) { log($"  OBS : {what} non arrêté ({e.Message})"); }
            }
        }
        var before = state.Before.ToHashSet();
        var own = (uint)Environment.ProcessId;

        var targets = new Dictionary<IntPtr, Native.Win>();
        var owned = (state.Owned ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // still the same process (same start time), plus the children it started later
        var pids = Native.WithDescendants((state.OwnedPids ?? new()).Where(kv => StartOf(kv.Key) == kv.Value).Select(kv => kv.Key));
        foreach (var w in Native.TopWindows()) // only applications the profile launched: never one the user opened during the live
            if (w.Pid != own && !before.Contains((long)w.H) && (owned.Contains(w.Process) || pids.Contains(w.Pid)) && !w.Path.StartsWith(Deck.AppDir, StringComparison.OrdinalIgnoreCase)) targets[w.H] = w; // never our own app, whichever instance
        if (state.CloseChat) { using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "TomLine.StreamLauncher.ChatClose"); ev.Set(); log("  fermeture : chat unifié"); }
        foreach (var t in state.Touched)
        {
            var h = (IntPtr)t.H;
            if (!Native.IsWindow(h)) continue;
            Native.GetWindowThreadProcessId(h, out var pid);
            if (pid == t.Pid && pid != own) targets[h] = Native.Describe(h); // same pid: guards against a reused handle
        }

        var keep = (state.Keep ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keepWins = (state.KeepWindows ?? Array.Empty<long>()).ToHashSet();
        foreach (var w in targets.Values.Where(w => keep.Contains(w.Process) || keepWins.Contains((long)w.H)).ToList())
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
