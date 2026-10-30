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
                if (root != IntPtr.Zero && pid != Environment.ProcessId)
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
