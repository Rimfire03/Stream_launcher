using System.Runtime.InteropServices;
using System.Text;

static class Native
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MSLL { public POINT pt; public uint data, flags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE dd, uint flags);

    /// <summary>identity of the physical monitor behind a \.\DISPLAYn name (those names are renumbered by Windows, e.g. after a restart); "" if unknown</summary>
    public static string MonitorId(string deviceName)
    {
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        return EnumDisplayDevices(deviceName, 0, ref dd, 0) ? dd.DeviceID : "";
    }
    [DllImport("user32")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32")] public static extern bool IsWindow(IntPtr h);
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

    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? title);

    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);

    public static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public record Win(IntPtr H, uint Pid, string Process, string Title, string Path, string Class);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size, Usage, Pid; public UIntPtr Heap; public uint Module, Threads, ParentPid; public int PriClass; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [DllImport("kernel32")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool Process32First(IntPtr s, ref ProcessEntry e);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool Process32Next(IntPtr s, ref ProcessEntry e);

    /// <summary>The given processes plus every process descended from them (a parent that already exited still links its children).</summary>
    public static HashSet<uint> WithDescendants(IEnumerable<uint> roots)
    {
        var all = new HashSet<uint>(roots);
        var parent = new Dictionary<uint, uint>();
        var snap = CreateToolhelp32Snapshot(2, 0); // TH32CS_SNAPPROCESS
        if (snap == (IntPtr)(-1)) return all;
        try
        {
            var e = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (bool ok = Process32First(snap, ref e); ok; ok = Process32Next(snap, ref e)) parent[e.Pid] = e.ParentPid;
        }
        finally { CloseHandle(snap); }
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var (pid, ppid) in parent)
                if (pid != 0 && all.Contains(ppid) && all.Add(pid)) grew = true;
        }
        return all;
    }

    public static string GetTitle(IntPtr h)
    {
        int n = GetWindowTextLength(h);
        if (n == 0) return "";
        var sb = new StringBuilder(n + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Title to store for a captured window. "Discussion en cours - Discord" -> "Discord": when one segment of the title
    /// (split on " - ", " – ", " — ", " | ") is the process name, only that stable part is kept; otherwise the full title.
    /// </summary>
    public static string StableTitle(string title, string process)
    {
        if (process == "") return title;
        foreach (var seg in title.Split(new[] { " - ", " – ", " — ", " | " }, StringSplitOptions.RemoveEmptyEntries))
            if (seg.Trim().Replace(" ", "").Equals(process, StringComparison.OrdinalIgnoreCase)) return seg.Trim();
        return title;
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
        return new Win(h, pid, System.IO.Path.GetFileNameWithoutExtension(path), GetTitle(h), path, ClassOf(h));
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

    /// <summary>Titled top-level windows that are hidden (apps living in the tray keep their window, just not shown)</summary>
    public static List<Win> HiddenWindows()
    {
        var list = new List<Win>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) && GetWindowTextLength(h) > 0) list.Add(Describe(h));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Main windows of a process: shown or hidden, big enough not to be a tray helper window</summary>
    public static List<Win> MainWindows(string process) => // shortcut: tray helper windows are told apart by size only, replace if an app breaks it

        TopWindows().Concat(HiddenWindows()).Where(w => w.Process.Equals(process, StringComparison.OrdinalIgnoreCase) && IsBig(w.H)).ToList();

    // a minimized window is a tiny title bar: it counts as big
    static bool IsBig(IntPtr h) => IsIconic(h) || GetWindowRect(h, out var r) && r.Right - r.Left >= 200 && r.Bottom - r.Top >= 120;

    /// <summary>Applications worth picking: a real window (shown or hidden), not a Windows component, not this app</summary>
    public static List<string> AppNames()
    {
        var own = (uint)Environment.ProcessId;
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return TopWindows().Concat(HiddenWindows())
            .Where(w => w.Pid != own && w.Process != "" && !w.Path.StartsWith(sys, StringComparison.OrdinalIgnoreCase) && !w.Path.StartsWith(Deck.AppDir, StringComparison.OrdinalIgnoreCase)
                && IsBig(w.H))
            .Select(w => w.Process).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList { void HrInit(); void AddTab(IntPtr h); void DeleteTab(IntPtr h); void ActivateTab(IntPtr h); void SetActiveAlt(IntPtr h); }
    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")] class TaskbarList { }

    /// <summary>Gives a window shown by hand its taskbar button back (apps hidden in the tray remove it)</summary>
    public static void AddTaskbarButton(IntPtr h)
    {
        try { var t = (ITaskbarList)new TaskbarList(); t.HrInit(); t.AddTab(h); }
        catch { /* cosmetic only */ }
    }

    public static bool IsMinimized(IntPtr h) => IsIconic(h);
    public static bool IsVisible(IntPtr h) => IsWindowVisible(h);

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
        var scr = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == Config.Resolve(s.Monitor)) ?? Screen.PrimaryScreen!;
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
