using System.Runtime.InteropServices;
using FlaUI.UIA3;

/// <summary>Global low-level mouse hook that turns each click into a Click step. Create and dispose on the UI thread.</summary>
class Recorder : IDisposable
{
    const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204;

    readonly List<Step> steps;
    readonly Action changed;
    // created lazily inside the worker task (MTA): an instance made on the UI thread (STA) would need that thread to pump while the hook waits, and always time out
    UIA3Automation? uia;
    readonly Native.HookProc proc; // kept in a field so the GC does not collect the delegate
    readonly IntPtr hook;
    DateTime last = DateTime.MinValue;

    public Recorder(List<Step> steps, Action changed)
    {
        this.steps = steps;
        this.changed = changed;
        proc = Callback;
        hook = Native.SetWindowsHookEx(WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new InvalidOperationException("Hook souris impossible");
    }

    IntPtr Callback(int code, IntPtr w, IntPtr l)
    {
        int msg = (int)w;
        if (code >= 0 && (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN))
        {
            try { Record(Marshal.PtrToStructure<Native.MSLL>(l).pt, msg == WM_RBUTTONDOWN); } catch { }
        }
        return Native.CallNextHookEx(hook, code, w, l);
    }

    void Record(Native.POINT pt, bool right)
    {
        var hw = Native.WindowFromPoint(pt);
        if (hw == IntPtr.Zero) return;
        var root = Native.GetAncestor(hw, 3); // GA_ROOTOWNER: popups/menus map back to their main window
        if (root == IntPtr.Zero) root = hw;
        var win = Native.Describe(root);
        if (win.Pid == Environment.ProcessId) return; // our own UI (stop button)

        // the hook runs before the target app gets the click, so the element is still there; cap the wait
        string name = "", id = "";
        try
        {
            var t = Task.Run(() =>
            {
                var el = (uia ??= new UIA3Automation()).FromPoint(new System.Drawing.Point(pt.X, pt.Y));
                return (el?.Properties.Name.ValueOrDefault ?? "", el?.Properties.AutomationId.ValueOrDefault ?? "");
            });
            if (t.Wait(300)) (name, id) = t.Result;
        }
        catch { }

        Native.GetWindowRect(root, out var r);
        var now = DateTime.UtcNow;
        double elapsed = last == DateTime.MinValue ? 1000 : (now - last).TotalMilliseconds;
        last = now;

        var btn = right ? MouseBtn.Right : MouseBtn.Left;
        var prev = steps.LastOrDefault();
        int relX = pt.X - r.Left, relY = pt.Y - r.Top;
        if (prev is { Type: StepType.Click, Double: false } && elapsed < 450 && prev.Button == btn &&
            prev.Process == win.Process && Math.Abs(prev.RelX - relX) < 5 && Math.Abs(prev.RelY - relY) < 5)
        {
            prev.Double = true;
            changed();
            return;
        }

        steps.Add(new Step
        {
            Type = StepType.Click,
            DelayMs = (int)Math.Clamp(elapsed, 200, 10000),
            Process = win.Process,
            Title = win.Title,
            Mode = name != "" ? ClickMode.Element : ClickMode.Coords,
            ElementName = name,
            ElementId = id,
            RelX = relX,
            RelY = relY,
            Button = btn,
        });
        changed();
    }

    public void Dispose()
    {
        Native.UnhookWindowsHookEx(hook);
        uia?.Dispose();
    }
}
