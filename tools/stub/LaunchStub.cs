using System;
using System.Diagnostics;
using System.IO;

// Tiny launcher copied (under many names) into Profils\<profil>\. Stream Deck resolves hard links back to the original exe,
// so each profile launcher is a real copy of this stub: it starts ..\..\StreamLauncher.exe and passes its own file name,
// which carries the action ("<profil> - bascule.exe"). Built with the .NET Framework csc (see Build-Stub.ps1).
static class LaunchStub
{
    static void Main()
    {
        var me = Process.GetCurrentProcess().MainModule.FileName;
        var app = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(me), "..", "..", "StreamLauncher.exe"));
        if (!File.Exists(app)) return;
        Process.Start(new ProcessStartInfo(app, "--link \"" + Path.GetFileName(me) + "\"") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(app) });
    }
}
