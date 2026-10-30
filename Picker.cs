using System.Runtime.InteropServices;

/// <summary>
/// One-shot global mouse hook. Pick mode: the next click on a foreign window is swallowed and that window reported.
/// Move mode: clicks go through; when a drag leaves a foreign window at a different rect, that window is reported.
/// Create and dispose on the UI thread.
/// </summary>
class WindowPicker : IDisposable
{
    const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;

    readonly Control sync;
    readonly bool move;
    readonly Action<IntPtr> found;
    readonly Native.HookProc proc; // kept in a field so the GC does not collect the delegate
    readonly IntPtr hook;
    IntPtr downWin;
    Native.RECT downRect;
    bool eating, done;

    public WindowPicker(Control sync, bool move, Action<IntPtr> found)
    {
        this.sync = sync;
        this.move = move;
        this.found = found;
        proc = Callback;
        hook = Native.SetWindowsHookEx(WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new InvalidOperationException("Hook souris impossible");
    }

    // the unified chat is one of our own windows, but it can be captured like any other (our main window and the pick bar cannot)
    static bool IsChatWindow(IntPtr h) => Application.OpenForms.OfType<ChatWindow>().Any(f => f.IsHandleCreated && f.Handle == h);

    IntPtr Callback(int code, IntPtr w, IntPtr l)
    {
        int msg = (int)w;
        if (code >= 0 && !done)
        {
            if (msg == WM_LBUTTONDOWN)
            {
                var pt = Marshal.PtrToStructure<Native.MSLL>(l).pt;
                var hw = Native.WindowFromPoint(pt);
                var root = hw == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(hw, 3); // GA_ROOTOWNER
                Native.GetWindowThreadProcessId(root == IntPtr.Zero ? hw : root, out var pid);
                if (root != IntPtr.Zero && (pid != Environment.ProcessId || IsChatWindow(root)))
                {
                    downWin = root;
                    Native.GetWindowRect(root, out downRect);
                    if (!move) { eating = true; return (IntPtr)1; }
                }
                else downWin = IntPtr.Zero; // our own bar: let the click through
            }
            else if (msg == WM_LBUTTONUP)
            {
                if (eating)
                {
                    eating = false;
                    Report(downWin);
                    return (IntPtr)1;
                }
                if (move && downWin != IntPtr.Zero)
                {
                    var win = downWin;
                    var before = downRect;
                    downWin = IntPtr.Zero;
                    // let the window settle (snap animation) before comparing
                    Task.Delay(300).ContinueWith(_ =>
                    {
                        Native.GetWindowRect(win, out var after);
                        if (after.Left != before.Left || after.Top != before.Top || after.Right != before.Right || after.Bottom != before.Bottom)
                            Report(win);
                    });
                }
            }
        }
        return Native.CallNextHookEx(hook, code, w, l);
    }

    void Report(IntPtr win)
    {
        if (done) return;
        done = true;
        sync.BeginInvoke(() => found(win));
    }

    public void Dispose() => Native.UnhookWindowsHookEx(hook);
}

/// <summary>
/// One-shot global mouse hook for recording a notification-area icon: the first click on a taskbar-tray or overflow-flyout
/// icon is reported (its name, whether it was a right click); the click itself goes through. The "show hidden icons"
/// chevron and clicks elsewhere are ignored so the user can open the flyout first. Create and dispose on the UI thread.
/// </summary>
class TrayPicker : IDisposable
{
    const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204;

    readonly Control sync;
    readonly Action<string, bool> found;
    // created lazily inside the worker task (MTA): an instance made on the UI thread (STA) would need that thread to pump while the hook waits, and always time out
    FlaUI.UIA3.UIA3Automation? uia;
    readonly Native.HookProc proc;
    readonly IntPtr hook;
    bool done;

    public TrayPicker(Control sync, Action<string, bool> found)
    {
        this.sync = sync;
        this.found = found;
        proc = Callback;
        hook = Native.SetWindowsHookEx(WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new InvalidOperationException("Hook souris impossible");
    }

    IntPtr Callback(int code, IntPtr w, IntPtr l)
    {
        int msg = (int)w;
        if (code >= 0 && !done && (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN))
        {
            try
            {
                var pt = Marshal.PtrToStructure<Native.MSLL>(l).pt;
                var hw = Native.WindowFromPoint(pt);
                var root = hw == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(hw, 3);
                var cls = root == IntPtr.Zero ? "" : Native.ClassOf(root);
                if (cls == "Shell_TrayWnd" || cls.Contains("Overflow", StringComparison.OrdinalIgnoreCase))
                {
                    // the hook runs before the app gets the click: the element is still under the cursor
                    var t = Task.Run(() =>
                    {
                        var el = (uia ??= new FlaUI.UIA3.UIA3Automation()).FromPoint(new System.Drawing.Point(pt.X, pt.Y));
                        var c = el?.ClassName ?? "";
                        return c.Contains("TaskListButton") || c == "ToggleButton" ? null : el?.Name;
                    });
                    var name = t.Wait(400) ? t.Result : null;
                    if (!string.IsNullOrWhiteSpace(name) && !name.Contains("cach", StringComparison.OrdinalIgnoreCase) && !name.Contains("hidden", StringComparison.OrdinalIgnoreCase))
                    {
                        done = true;
                        var first = name.Split('\n')[0].Trim(); // tooltips can carry several lines of live status
                        bool right = msg == WM_RBUTTONDOWN;
                        sync.BeginInvoke(() => found(first, right));
                    }
                }
            }
            catch { }
        }
        return Native.CallNextHookEx(hook, code, w, l);
    }

    public void Dispose()
    {
        Native.UnhookWindowsHookEx(hook);
        uia?.Dispose();
    }
}
