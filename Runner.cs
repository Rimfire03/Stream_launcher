using System.Diagnostics;
using FlaUI.Core.Conditions;
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
