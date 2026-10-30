using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

class Runner
{
    public Action<string> Log = _ => { };

    /// <returns>number of failed steps</returns>
    public int Run(Profile p, CancellationToken ct)
    {
        int errors = 0;
        using var uia = new UIA3Automation();
        for (int i = 0; i < p.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var s = p.Steps[i];
            Log($"[{i + 1}/{p.Steps.Count}] {s}");
            try
            {
                if (ct.WaitHandle.WaitOne(Math.Max(0, s.DelayMs))) throw new OperationCanceledException(ct);
                switch (s.Type)
                {
                    case StepType.Launch: Launch(s); break;
                    case StepType.Place: Place(s, ct); break;
                    case StepType.Window: Window(s, ct); break;
                    case StepType.Tray: Tray(uia, s, ct); break;
                    case StepType.Browser: Browse(s); break;
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
        Process.Start(psi);
    }

    // Window lookup: process and class are hard criteria; the title is a preference, because some apps rewrite
    // their title (document name, unread count, page title...). Without a title match the topmost window of the
    // right process/class is used once a grace period has passed, so a freshly launched window can still get its title.
    IntPtr WaitWindow(Step s, CancellationToken ct)
    {
        if (s.Process == "" && s.Title == "" && s.WindowClass == "") throw new InvalidOperationException("Process, Title et WindowClass vides");
        var start = DateTime.UtcNow;
        var end = start.AddMilliseconds(s.TimeoutMs);
        var grace = start.AddMilliseconds(Math.Min(s.TimeoutMs / 3, 5000));
        do
        {
            ct.ThrowIfCancellationRequested();
            var cands = Native.TopWindows().Where(w =>
                (s.Process == "" || w.Process.Equals(s.Process, StringComparison.OrdinalIgnoreCase)) &&
                (s.WindowClass == "" || w.Class.Equals(s.WindowClass, StringComparison.OrdinalIgnoreCase))).ToList();
            var byTitle = s.Title == "" ? null : cands.FirstOrDefault(w => w.Title.Contains(s.Title, StringComparison.OrdinalIgnoreCase));
            if (s.Title == "" && cands.Count > 0) return cands[0].H;
            if (byTitle != null) return byTitle.H;
            if (!s.TitleStrict && cands.Count > 0 && DateTime.UtcNow >= grace)
            {
                Log($"  titre '{s.Title}' absent : utilisation de la fenêtre '{cands[0].Title}'");
                return cands[0].H;
            }
            Thread.Sleep(250);
        } while (DateTime.UtcNow < end);
        throw new TimeoutException($"fenêtre introuvable ({s.Process} '{s.Title}' {s.WindowClass})");
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

    void Browse(Step s)
    {
        // http(s) only: never hand file:, javascript: etc. to a browser
        if (!Uri.TryCreate(s.Url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            throw new InvalidOperationException("URL http(s) invalide : " + s.Url);
        if (s.Browser == BrowserKind.Defaut)
        {
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
            return;
        }
        var exe = FindBrowser(s.Browser) ?? throw new FileNotFoundException($"{s.Browser} introuvable sur ce poste");
        Process.Start(new ProcessStartInfo(exe, (s.Browser == BrowserKind.Firefox ? "-new-window" : "--new-window") + $" \"{u.AbsoluteUri}\"") { UseShellExecute = false });
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

    static bool IsChevron(AutomationElement b) =>
        b.Name is { } n && (n.Contains("cach", StringComparison.OrdinalIgnoreCase) || n.Contains("hidden", StringComparison.OrdinalIgnoreCase));

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
        var names = TaskbarButtons(uia).Where(b => !IsChevron(b)).Select(b => b.Name).ToList();
        names.AddRange(OverflowButtons(uia).Select(b => b.Name));
        System.Windows.Forms.SendKeys.SendWait("{ESC}");
        return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
    }

    void Tray(UIA3Automation uia, Step s, CancellationToken ct)
    {
        if (s.TrayName == "") throw new InvalidOperationException("TrayName vide");
        bool Match(AutomationElement b) => b.Name?.Contains(s.TrayName, StringComparison.OrdinalIgnoreCase) == true && !IsChevron(b);
        var end = DateTime.UtcNow.AddMilliseconds(s.TimeoutMs);
        do
        {
            ct.ThrowIfCancellationRequested();
            var el = TaskbarButtons(uia).FirstOrDefault(Match) ?? OverflowButtons(uia).FirstOrDefault(Match);
            if (el != null && el.TryGetClickablePoint(out var p))
            {
                Native.Click(p.X, p.Y, s.Button == MouseBtn.Right, s.Double);
                return;
            }
            Thread.Sleep(500);
        } while (DateTime.UtcNow < end);
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
