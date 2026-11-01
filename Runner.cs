using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

class Runner
{
    public Action<string> Log = _ => { };
    IntPtr lastWindow; // window opened by the latest Browser step
    /// <summary>windows the profile placed, clicked or opened (used to close the live)</summary>
    public readonly HashSet<(IntPtr H, uint Pid)> Touched = new();
    public Action? OpenChat; // opens the unified chat window (set by the GUI, or by the command line which starts the GUI)
    public readonly List<ObsStop> ObsStops = new(); // OBS outputs to stop when the live is stopped (steps with the box ticked)
    public readonly HashSet<uint> LaunchedPids = new(); // processes started by Launch steps: their descendants count as launched by the profile too
    public bool ChatOpened, ChatKeep; // chat step ran: the chat is closed at stop unless KeepOpen
    public readonly HashSet<IntPtr> KeepWindows = new(); // browser windows left open at stop (KeepOpen)
    public readonly HashSet<string> KeepProcesses = new(StringComparer.OrdinalIgnoreCase); // applications shown from the tray with KeepOpen: none of their windows is closed at stop

    void Touch(IntPtr h)
    {
        Native.GetWindowThreadProcessId(h, out var pid);
        Touched.Add((h, pid));
    }

    /// <returns>number of failed steps</returns>
    public int Run(Profile p, CancellationToken ct)
    {
        int errors = 0;
        using var uia = new UIA3Automation();
        var issues = Checks.Analyze(p.Steps); // missing executables are skipped, with the steps aimed at their windows
        var off = Checks.Skipped(p.Steps); // disabled steps, and the ones that depend on them
        for (int i = 0; i < p.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var s = p.Steps[i];
            Log($"[{i + 1}/{p.Steps.Count}] {s}");
            if (off[i] != null)
            {
                Log("  " + off[i] + " : étape ignorée");
                continue;
            }
            if (issues[i] != null)
            {
                Log("  " + issues[i] + " : étape ignorée");
                if (s.Type == StepType.Launch) errors++;
                continue;
            }
            try
            {
                if (ct.WaitHandle.WaitOne(Math.Max(0, s.DelayMs))) throw new OperationCanceledException(ct);
                switch (s.Type)
                {
                    case StepType.Launch: Launch(s); break;
                    case StepType.Place: Place(s, ct); break;
                    case StepType.Window: Window(s, ct); break;
                    case StepType.Tray: Tray(uia, s, ct); break;
                    case StepType.Browser: Browse(s, ct); break;
                    case StepType.Obs: Obs(s, ct); break;
                    case StepType.Show: Show(s, ct); break;
                    case StepType.Chat:
                        if (OpenChat == null) Log("  fenêtre de chat indisponible ici");
                        else { OpenChat(); ChatOpened = true; ChatKeep = s.KeepOpen; }
                        break;
                    default: Click(uia, s, ct); break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { errors++; Log("  ERREUR : " + e.Message); }
        }
        return errors;
    }

    void Launch(Step s)
    {
        if (string.IsNullOrWhiteSpace(s.Exe)) throw new InvalidOperationException("Exe vide");
        if (s.SkipIfRunning && Process.GetProcessesByName(Path.GetFileNameWithoutExtension(s.Exe)).Length > 0)
        {
            Log("  déjà lancé, ignoré");
            return;
        }
        var psi = new ProcessStartInfo(s.Exe, s.Args) { UseShellExecute = true };
        if (File.Exists(s.Exe)) psi.WorkingDirectory = Path.GetDirectoryName(s.Exe)!;
        var proc = Process.Start(psi);
        if (proc != null) LaunchedPids.Add((uint)proc.Id);
    }

    // Window lookup: process and class are hard criteria; the title is a preference, because some apps rewrite
    // their title (document name, unread count, page title...). Without a title match the topmost window of the
    // right process/class is used once a grace period has passed, so a freshly launched window can still get its title.
    IntPtr WaitWindow(Step s, CancellationToken ct)
    {
        var h = FindWindowFor(s, ct);
        Touch(h);
        return h;
    }

    /// <summary>WinForms class names end with a hash that changes with every build ("WindowsForms10.Window.8.app.0.a0f91b_r3_ad1"): compare without it</summary>
    static string ClassKey(string c) => System.Text.RegularExpressions.Regex.Replace(c, @"(\.app\.\d+)\.[0-9a-f]+_r\d+_ad\d+$", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    IntPtr FindWindowFor(Step s, CancellationToken ct)
    {
        if (s.OnLastWindow)
            return lastWindow != IntPtr.Zero && Native.IsWindow(lastWindow) ? lastWindow
                : throw new InvalidOperationException("aucune fenêtre ouverte par une étape Navigateur précédente");
        if (s.Process == "" && s.Title == "" && s.WindowClass == "") throw new InvalidOperationException("Process, Title et WindowClass vides");
        var start = DateTime.UtcNow;
        var end = start.AddMilliseconds(s.TimeoutMs);
        var grace = start.AddMilliseconds(Math.Min(s.TimeoutMs / 3, 5000));
        do
        {
            ct.ThrowIfCancellationRequested();
            var cands = Native.TopWindows().Where(w =>
                (s.Process == "" || w.Process.Equals(s.Process, StringComparison.OrdinalIgnoreCase)) &&
                (s.WindowClass == "" || ClassKey(w.Class).Equals(ClassKey(s.WindowClass), StringComparison.OrdinalIgnoreCase))).ToList();
            var byTitle = s.Title == "" ? null : cands.FirstOrDefault(w => w.Title.Contains(s.Title, StringComparison.OrdinalIgnoreCase));
            if (s.Title == "" && cands.Count > 0) return cands[0].H;
            if (byTitle != null) return byTitle.H;
            if (cands.Count == 0 && DateTime.UtcNow >= grace)
            {
                // no visible window: an app living in the tray keeps its hidden window, show it without any click on the icon
                var hid = Native.HiddenWindows().FirstOrDefault(w =>
                    (s.Process == "" || w.Process.Equals(s.Process, StringComparison.OrdinalIgnoreCase)) &&
                    (s.WindowClass == "" || ClassKey(w.Class).Equals(ClassKey(s.WindowClass), StringComparison.OrdinalIgnoreCase)) &&
                    (s.Title == "" || w.Title.Contains(s.Title, StringComparison.OrdinalIgnoreCase)));
                if (hid != null)
                {
                    Log($"  fenêtre masquée '{hid.Title}' : affichée");
                    Native.ShowWindowAsync(hid.H, 9);
                    Thread.Sleep(600);
                    return hid.H;
                }
            }
            if (!s.TitleStrict && cands.Count > 0 && DateTime.UtcNow >= grace)
            {
                Log($"  titre '{s.Title}' absent : utilisation de la fenêtre '{cands[0].Title}'");
                return cands[0].H;
            }
            Thread.Sleep(250);
        } while (DateTime.UtcNow < end);
        throw new TimeoutException($"fenêtre introuvable ({s.Process} '{s.Title}' {s.WindowClass})");
    }

    /// <summary>Shows the window(s) of an application, hidden in the tray or minimized, without touching its tray icon</summary>
    void Show(Step s, CancellationToken ct)
    {
        var end = DateTime.UtcNow.AddMilliseconds(s.TimeoutMs);
        do
        {
            ct.ThrowIfCancellationRequested();
            var wins = Native.MainWindows(s.Process);
            if (wins.Count > 0)
            {
                bool alreadyShown = wins.Any(w => Native.IsVisible(w.H) && !Native.IsMinimized(w.H));
                if (alreadyShown) Log("  déjà visible : rien à faire");
                foreach (var w in wins)
                {
                    if (!alreadyShown && (!Native.IsVisible(w.H) || Native.IsMinimized(w.H))) { Log($"  fenêtre '{w.Title}' affichée"); Native.ShowWindowAsync(w.H, 9); }
                    if (alreadyShown && !Native.IsVisible(w.H)) continue; // hidden helper of an app that is already on screen
                    if (s.KeepOpen) { KeepWindows.Add(w.H); KeepProcesses.Add(w.Process); } else Touch(w.H);
                }
                Thread.Sleep(600);
                return;
            }
            Thread.Sleep(300);
        } while (DateTime.UtcNow < end);
        throw new TimeoutException($"aucune fenêtre pour l'application '{s.Process}'");
    }

    void Place(Step s, CancellationToken ct)
    {
        var h = WaitWindow(s, ct);
        Native.Move(h, s);
        // many apps restore their own saved position right after showing: apply twice
        Thread.Sleep(600);
        Native.Move(h, s);
    }

    void Window(Step s, CancellationToken ct)
    {
        var h = WaitWindow(s, ct);
        if (s.Action == WindowAction.Deplacer) { Native.Move(h, s); return; }
        // async: a plain ShowWindow blocks while the target app is busy
        Native.ShowWindowAsync(h, s.Action switch { WindowAction.Reduire => 6, WindowAction.Agrandir => 3, _ => 9 }); // SW_MINIMIZE / MAXIMIZE / RESTORE
    }

    // ---- browser ----

    static readonly Dictionary<BrowserKind, string> BrowserExe = new()
    {
        [BrowserKind.Chrome] = "chrome.exe", [BrowserKind.Edge] = "msedge.exe", [BrowserKind.Firefox] = "firefox.exe",
        [BrowserKind.Brave] = "brave.exe", [BrowserKind.Opera] = "opera.exe", [BrowserKind.Vivaldi] = "vivaldi.exe",
    };

    // installed browsers register themselves under App Paths (machine or user, 64 or 32 bit view)
    static string? FindBrowser(BrowserKind kind)
    {
        var key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + BrowserExe[kind];
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryHive.CurrentUser })
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
            {
                using var k = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view).OpenSubKey(key);
                if (k?.GetValue(null) as string is { } path && File.Exists(path)) return path;
            }
        return null;
    }

    // OBS may still be starting (an earlier step launched it): retry while it is unreachable, fail at once when it answers with an error
    void Obs(Step s, CancellationToken ct)
    {
        if (s.ObsAction == ObsActionKind.Scene && string.IsNullOrWhiteSpace(s.ObsScene) && string.IsNullOrWhiteSpace(s.ObsCollection) && string.IsNullOrWhiteSpace(s.ObsProfile)) throw new InvalidOperationException("profil, collection et scène OBS vides");
        var end = DateTime.UtcNow.AddMilliseconds(s.TimeoutMs);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (s.ObsAction == ObsActionKind.Scene) ObsSession.Apply(s.ObsHost, s.ObsPassword, s.ObsProfile, s.ObsCollection, s.ObsScene, Math.Max(5000, s.TimeoutMs), ct);
                else ObsSession.RunAction(s.ObsHost, s.ObsPassword, s.ObsAction, Math.Max(15000, s.TimeoutMs), ct);
                if (s.ObsAction == ObsActionKind.StartStream && s.ObsStopStream) ObsStops.Add(new ObsStop(s.ObsHost, s.ObsPassword, true, false));
                if (s.ObsAction == ObsActionKind.StartRecord && s.ObsStopRecord) ObsStops.Add(new ObsStop(s.ObsHost, s.ObsPassword, false, true));
                return;
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception e) when (!ct.IsCancellationRequested && DateTime.UtcNow < end) { Log("  OBS injoignable, nouvel essai : " + e.Message); Thread.Sleep(1000); }
            catch (Exception e) when (!ct.IsCancellationRequested) { throw new TimeoutException("OBS injoignable (WebSocket activé ? mot de passe ?) : " + e.Message); }
        }
    }

    void Browse(Step s, CancellationToken ct)
    {
        // http(s) only: never hand file:, javascript: etc. to a browser
        if (!Uri.TryCreate(s.Url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            throw new InvalidOperationException("URL http(s) invalide : " + s.Url);
        if (s.Browser == BrowserKind.Defaut)
        {
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
            Log("  navigateur par défaut : la nouvelle fenêtre ne peut pas être suivie" + (s.KeepOpen ? " (« garder ouverte » sans effet)" : ""));
            return;
        }
        var exe = FindBrowser(s.Browser) ?? throw new FileNotFoundException($"{s.Browser} introuvable sur ce poste");
        var proc = Path.GetFileNameWithoutExtension(BrowserExe[s.Browser]);
        var before = Native.TopWindows().Where(w => w.Process.Equals(proc, StringComparison.OrdinalIgnoreCase)).Select(w => w.H).ToHashSet();
        Process.Start(new ProcessStartInfo(exe, (s.Browser == BrowserKind.Firefox ? "-new-window" : "--new-window") + $" \"{u.AbsoluteUri}\"") { UseShellExecute = false });

        // the new window is the one that was not there before, whatever its title ends up being
        var end = DateTime.UtcNow.AddMilliseconds(s.TimeoutMs);
        do
        {
            ct.ThrowIfCancellationRequested();
            var fresh = Native.TopWindows().FirstOrDefault(w => w.Process.Equals(proc, StringComparison.OrdinalIgnoreCase) && !before.Contains(w.H));
            if (fresh != null)
            {
                lastWindow = fresh.H;
                Touch(fresh.H);
                if (s.KeepOpen)
                {
                    // extension pop-ups can appear first: once the window has settled, keep every new window of this browser
                    Thread.Sleep(1500);
                    foreach (var w in Native.TopWindows().Where(w => w.Process.Equals(proc, StringComparison.OrdinalIgnoreCase) && !before.Contains(w.H)))
                        KeepWindows.Add(w.H);
                }
                if (s.PlaceWindow)
                {
                    Native.Move(fresh.H, s);
                    Thread.Sleep(600);
                    Native.Move(fresh.H, s);
                }
                return;
            }
            Thread.Sleep(250);
        } while (DateTime.UtcNow < end);
        if (s.PlaceWindow) throw new TimeoutException("nouvelle fenêtre du navigateur non détectée");
        Log("  nouvelle fenêtre non détectée");
    }

    // ---- notification area (UI Automation on the taskbar) ----

    static IEnumerable<AutomationElement> TaskbarButtons(UIA3Automation uia)
    {
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        return tray == IntPtr.Zero ? Enumerable.Empty<AutomationElement>()
            : uia.FromHandle(tray).FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                // keep the notification area only: drop the Start button and the running/pinned app buttons
                .Where(b => b.ClassName != "ToggleButton" && b.ClassName?.Contains("TaskListButton") != true);
    }

    /// <summary>name of an element; some hidden-flyout elements throw when it is read directly</summary>
    static string NameOf(AutomationElement e) { try { return e.Properties.Name.ValueOrDefault ?? ""; } catch { return ""; } }

    static bool IsChevron(AutomationElement b) =>
        NameOf(b) is { } n && (n.Contains("cach", StringComparison.OrdinalIgnoreCase) || n.Contains("hidden", StringComparison.OrdinalIgnoreCase));

    // buttons of the overflow flyout (hidden icons), opened through the taskbar chevron
    static IEnumerable<AutomationElement> OverflowButtons(UIA3Automation uia)
    {
        var chevron = TaskbarButtons(uia).FirstOrDefault(IsChevron);
        if (chevron == null || !chevron.TryGetClickablePoint(out var p)) return Enumerable.Empty<AutomationElement>();
        Native.Click(p.X, p.Y, false, false);
        Thread.Sleep(700);
        return uia.GetDesktop().FindAllChildren()
            .Where(c => c.ClassName?.Contains("Overflow", StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(c => c.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))).ToList();
    }

    /// <summary>names of the icons in the notification area, visible then hidden ones</summary>
    public static List<string> ListTray()
    {
        using var uia = new UIA3Automation();
        var names = TaskbarButtons(uia).Where(b => !IsChevron(b)).Select(b => NameOf(b)).ToList();
        names.AddRange(OverflowButtons(uia).Select(b => NameOf(b)));
        System.Windows.Forms.SendKeys.SendWait("{ESC}");
        return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
    }

    /// <summary>"Garder ouverte à l'arrêt du live" on a tray step: the windows that appear after the click, and every other window of their application, are left alone at stop</summary>
    void KeepShownWindows(HashSet<IntPtr> seen, bool keep, CancellationToken ct)
    {
        var end = DateTime.UtcNow.AddSeconds(6);
        bool found = false;
        do
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(300);
            var fresh = Native.TopWindows().Where(w => !seen.Contains(w.H)).ToList();
            if (fresh.Count == 0) continue;
            if (!found) { found = true; Thread.Sleep(1200); fresh = Native.TopWindows().Where(w => !seen.Contains(w.H)).ToList(); } // pop-ups can follow the first window
            foreach (var w in fresh)
            {
                if (keep) { KeepWindows.Add(w.H); KeepProcesses.Add(w.Process); }
                else Touched.Add((w.H, w.Pid)); // opened by this step: closed at stop
            }
            return;
        } while (DateTime.UtcNow < end);
        Log("  zone de notification : aucune fenêtre n'est apparue (« garder ouverte » sans effet)");
    }

    /// <summary>the lasting part of an icon name: tooltips carry status and versions that change ("Discord - 3 messages", "OBS 30.1.2 (64-bit) - Profil : X" become "Discord", "OBS")</summary>
    public static string StableTrayName(string name)
    {
        var first = Plain(name.Split('\n')[0]);
        var cut = System.Text.RegularExpressions.Regex.Split(first, @"\s[-–—|]\s|\s?[:(]\s?")[0].Trim();
        cut = System.Text.RegularExpressions.Regex.Replace(cut, @"\s+v?\d+(\.\d+)+.*$", "").Trim();
        return cut.Length >= 3 ? cut : first;
    }

    /// <summary>trimmed, without invisible format characters (the hidden-icons flyout prefixes some names with one, e.g. U+200E before "Discord")</summary>
    static string Plain(string s) => new string(s.Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim();

    // Windows' own icons whose tooltip lists other apps ("Confidentialité Microphone utilisé par : Discord, Voicemod"): never the target of a name that is only mentioned in them
    static readonly string[] SystemIcons = { "Confidentialité", "Privacy", "Volume", "Réseau", "Network", "Horloge", "Clock", "Indicateur d", "Input indicator", "Afficher le Bureau", "Show desktop", "Batterie", "Battery" };

    /// <summary>how well an icon fits the saved name: 4 same name, 3 same lasting name (status and version aside), 2 starts with it, 1 only mentions it (not a Windows icon), 0 no</summary>
    static int TrayScore(AutomationElement b, string saved, string stable)
    {
        if (IsChevron(b)) return 0;
        var full = Plain(NameOf(b));
        var first = Plain(NameOf(b).Split('\n')[0]);
        if (first == "") return 0;
        if (string.Equals(first, Plain(saved), StringComparison.OrdinalIgnoreCase)) return 4;
        if (string.Equals(StableTrayName(first), stable, StringComparison.OrdinalIgnoreCase)) return 3;
        if (first.StartsWith(stable, StringComparison.OrdinalIgnoreCase)) return 2;
        bool windows = SystemIcons.Any(w => first.StartsWith(w, StringComparison.OrdinalIgnoreCase) && !stable.StartsWith(w, StringComparison.OrdinalIgnoreCase));
        return !windows && full.Contains(stable, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    void Tray(UIA3Automation uia, Step s, CancellationToken ct)
    {
        if (s.TrayName == "") throw new InvalidOperationException("TrayName vide");
        // the icon is found by its name each time (never by where it was clicked: icons move, and some change their picture, like Discord in a voice channel):
        // the best fitting name wins, so an icon that merely mentions the app is never taken for it
        var stable = StableTrayName(s.TrayName);
        (AutomationElement? El, int Score) Best(IEnumerable<AutomationElement> buttons) =>
            buttons.Select(x => (El: (AutomationElement?)x, Score: TrayScore(x, s.TrayName, stable))).Where(x => x.Score > 0).OrderByDescending(x => x.Score).FirstOrDefault();
        var end = DateTime.UtcNow.AddMilliseconds(s.TimeoutMs);
        do
        {
            ct.ThrowIfCancellationRequested();
            var best = Best(TaskbarButtons(uia));
            if (best.Score < 3) // not clearly the right one in the visible icons: look in the hidden ones too
            {
                var hidden = Best(OverflowButtons(uia));
                if (hidden.Score > best.Score) best = hidden;
            }
            if (best.El != null && best.El.TryGetClickablePoint(out var p))
            {
                Log($"  icône « {NameOf(best.El).Split('\n')[0].Trim()} » (correspondance {best.Score}/4)");
                var winsBefore = Native.TopWindows();
                if (s.TrayName.Trim() != "" && winsBefore.Any(w => w.Process.Contains(s.TrayName.Trim(), StringComparison.OrdinalIgnoreCase) && !Native.IsMinimized(w.H)))
                {
                    Log("  application déjà visible : clic sur l'icône ignoré"); // the icon would toggle (hide) it
                    return;
                }
                var seen = winsBefore.Select(w => w.H).ToHashSet();
                Native.Click(p.X, p.Y, s.Button == MouseBtn.Right, s.Double);
                // the icon toggles some apps (Discord): a window of that app that was open and is gone means the click hid it, so click again
                Thread.Sleep(1200);
                var now = Native.TopWindows().Select(w => w.H).ToHashSet();
                var hidden = winsBefore.FirstOrDefault(w => !now.Contains(w.H) && w.Process.Contains(s.TrayName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hidden != null && s.TrayName.Trim() != "" && !Native.TopWindows().Any(w => !seen.Contains(w.H)))
                {
                    Log($"  la fenêtre '{hidden.Title}' s'est masquée : nouveau clic sur l'icône");
                    Native.Click(p.X, p.Y, s.Button == MouseBtn.Right, s.Double);
                }
                KeepShownWindows(seen, s.KeepOpen, ct);
                return;
            }
            Thread.Sleep(500);
        } while (DateTime.UtcNow < end);
        Log("  icônes vues : " + string.Join(" | ", TaskbarButtons(uia).Concat(OverflowButtons(uia)).Select(x => Plain(NameOf(x).Split('\n')[0]))));
        throw new TimeoutException($"icône de la zone de notification introuvable ('{s.TrayName}')");
    }

    void Click(UIA3Automation uia, Step s, CancellationToken ct)
    {
        var h = WaitWindow(s, ct);
        Native.Activate(h);
        System.Drawing.Point pt;
        if (s.Mode == ClickMode.Coords)
        {
            Native.GetWindowRect(h, out var r);
            pt = new(r.Left + s.RelX, r.Top + s.RelY);
        }
        else pt = WaitElementPoint(uia, h, s, ct);
        Native.Click(pt.X, pt.Y, s.Button == MouseBtn.Right, s.Double);
    }

    static System.Drawing.Point WaitElementPoint(UIA3Automation uia, IntPtr h, Step s, CancellationToken ct)
    {
        if (s.ElementName == "" && s.ElementId == "") throw new InvalidOperationException("ElementName et ElementId vides");
        var end = DateTime.UtcNow.AddMilliseconds(s.TimeoutMs);
        do
        {
            ct.ThrowIfCancellationRequested();
            var el = uia.FromHandle(h).FindFirstDescendant(cf =>
            {
                ConditionBase c = s.ElementName != "" ? cf.ByName(s.ElementName) : cf.ByAutomationId(s.ElementId);
                return s.ElementName != "" && s.ElementId != "" ? c.And(cf.ByAutomationId(s.ElementId)) : c;
            });
            if (el != null)
            {
                if (el.TryGetClickablePoint(out var p)) return p;
                var b = el.BoundingRectangle;
                if (!b.IsEmpty) return new(b.X + b.Width / 2, b.Y + b.Height / 2);
            }
            Thread.Sleep(300);
        } while (DateTime.UtcNow < end);
        throw new TimeoutException($"élément introuvable ('{s.ElementName}' {s.ElementId})");
    }
}

/// <summary>what to stop in OBS when the live is stopped (kept in the live state file)</summary>
record ObsStop(string Host, string Password, bool Stream, bool Record);
