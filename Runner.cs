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

    static IntPtr WaitWindow(Step s, CancellationToken ct)
    {
        if (s.Process == "" && s.Title == "") throw new InvalidOperationException("Process et Title vides");
        var end = DateTime.UtcNow.AddMilliseconds(s.TimeoutMs);
        do
        {
            ct.ThrowIfCancellationRequested();
            var w = Native.TopWindows().FirstOrDefault(w =>
                (s.Process == "" || w.Process.Equals(s.Process, StringComparison.OrdinalIgnoreCase)) &&
                (s.Title == "" || w.Title.Contains(s.Title, StringComparison.OrdinalIgnoreCase)));
            if (w != null) return w.H;
            Thread.Sleep(250);
        } while (DateTime.UtcNow < end);
        throw new TimeoutException($"fenêtre introuvable ({s.Process} '{s.Title}')");
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
