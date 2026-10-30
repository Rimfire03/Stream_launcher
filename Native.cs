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
            if (DwmGetWindowAttribute(h, 14, out var cloaked, 4) == 0 && cloaked != 0) return true;
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

    public static void Move(IntPtr h, Step s)
    {
        var scr = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == s.Monitor) ?? Screen.PrimaryScreen!;
        var b = scr.Bounds;
        if (IsIconic(h) || IsZoomed(h)) ShowWindow(h, 9);
        uint flags = 0x14; // NOZORDER | NOACTIVATE
        if (s.W <= 0 || s.H <= 0) flags |= 0x1; // NOSIZE
        SetWindowPos(h, IntPtr.Zero, b.X + s.X, b.Y + s.Y, s.W, s.H, flags);
        if (s.Maximize) ShowWindow(h, 3); // SW_MAXIMIZE
    }
}
