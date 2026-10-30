using System.Runtime.InteropServices;
using System.Text;

static class Native
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MSLL { public POINT pt; public uint data, flags, time; public IntPtr extra; }

    [DllImport("user32")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr e);
    [DllImport("user32")] public static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder s, ref int n);
    [DllImport("dwmapi")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);

    public record Win(IntPtr H, uint Pid, string Process, string Title, string Path);

    public static string GetTitle(IntPtr h)
    {
        int n = GetWindowTextLength(h);
        if (n == 0) return "";
        var sb = new StringBuilder(n + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ProcessPath(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            int n = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : "";
        }
        finally { CloseHandle(h); }
    }

    public static Win Describe(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid);
        var path = ProcessPath(pid);
        return new Win(h, pid, System.IO.Path.GetFileNameWithoutExtension(path), GetTitle(h), path);
    }

    public static List<Win> TopWindows()
    {
        var list = new List<Win>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
            // UWP "ghost" windows are visible but cloaked
            if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
            list.Add(Describe(h));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static void Activate(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
        keybd_event(0x12, 0, 0, UIntPtr.Zero); // Alt trick so SetForegroundWindow is allowed
        SetForegroundWindow(h);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        Thread.Sleep(200);
    }

    public static void Click(int x, int y, bool right, bool dbl)
    {
        SetCursorPos(x, y);
        Thread.Sleep(80);
        uint down = right ? 8u : 2u, up = right ? 0x10u : 4u;
        for (int i = 0; i < (dbl ? 2 : 1); i++)
        {
            mouse_event(down, 0, 0, 0, UIntPtr.Zero);
            mouse_event(up, 0, 0, 0, UIntPtr.Zero);
            if (dbl) Thread.Sleep(60);
        }
    }

    // (cols, rows, col, row, spanCols, spanRows) of the predefined zones
    static (int, int, int, int, int, int) Cell(Step s) => s.Zone switch
    {
        Zone.Grille => (Math.Max(s.Cols, 1), Math.Max(s.Rows, 1), s.Col, s.Row, Math.Max(s.SpanCols, 1), Math.Max(s.SpanRows, 1)),
        Zone.Gauche => (2, 1, 0, 0, 1, 1),
        Zone.Droite => (2, 1, 1, 0, 1, 1),
        Zone.Haut => (1, 2, 0, 0, 1, 1),
        Zone.Bas => (1, 2, 0, 1, 1, 1),
        Zone.HautGauche => (2, 2, 0, 0, 1, 1),
        Zone.HautDroite => (2, 2, 1, 0, 1, 1),
        Zone.BasGauche => (2, 2, 0, 1, 1, 1),
        Zone.BasDroite => (2, 2, 1, 1, 1, 1),
        Zone.TiersGauche => (3, 1, 0, 0, 1, 1),
        Zone.TiersCentre => (3, 1, 1, 0, 1, 1),
        _ => (3, 1, 2, 0, 1, 1),
    };

    [DllImport("dwmapi")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

    public static void Move(IntPtr h, Step s)
    {
        var scr = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == s.Monitor) ?? Screen.PrimaryScreen!;
        if (IsIconic(h) || IsZoomed(h)) ShowWindow(h, 9);

        if (s.Zone == Zone.Manuel)
        {
            var b = scr.Bounds;
            uint flags = 0x14; // NOZORDER | NOACTIVATE
            if (s.W <= 0 || s.H <= 0) flags |= 0x1; // NOSIZE
            SetWindowPos(h, IntPtr.Zero, b.X + s.X, b.Y + s.Y, s.W, s.H, flags);
            if (s.Maximize) ShowWindow(h, 3); // SW_MAXIMIZE
            return;
        }

        var (cols, rows, col, row, sc, sr) = Cell(s);
        col = Math.Clamp(col, 0, cols - 1);
        row = Math.Clamp(row, 0, rows - 1);
        var a = scr.WorkingArea;
        // edges computed from the same formula for both neighbours: no gap, no overlap
        int x0 = a.X + a.Width * col / cols, x1 = a.X + a.Width * Math.Min(col + sc, cols) / cols;
        int y0 = a.Y + a.Height * row / rows, y1 = a.Y + a.Height * Math.Min(row + sr, rows) / rows;
        SetWindowPos(h, IntPtr.Zero, x0, y0, x1 - x0, y1 - y0, 0x14);

        // Win10/11 windows have an invisible border (shadow): grow the rect by it so the visible frame fills the zone
        if (GetWindowRect(h, out var w) && DwmGetWindowAttribute(h, 9, out RECT f, Marshal.SizeOf<RECT>()) == 0) // DWMWA_EXTENDED_FRAME_BOUNDS
        {
            int l = f.Left - w.Left, t = f.Top - w.Top, r = w.Right - f.Right, bt = w.Bottom - f.Bottom;
            SetWindowPos(h, IntPtr.Zero, x0 - l, y0 - t, x1 - x0 + l + r, y1 - y0 + t + bt, 0x14);
        }
    }
}
