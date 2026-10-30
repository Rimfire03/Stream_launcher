using System.Text.RegularExpressions;
using Microsoft.Win32;

/// <summary>Checks a profile before it runs: Launch steps whose executable is missing, and the steps tied to them.</summary>
static class Checks
{
    static readonly Regex UrlLike = new(@"^[A-Za-z][A-Za-z0-9+.\-]+:", RegexOptions.Compiled); // http:, shell:, ms-settings: (a drive letter is a single character)

    public static bool ExeExists(string exe)
    {
        exe = Environment.ExpandEnvironmentVariables(exe.Trim().Trim('"'));
        if (exe == "" || UrlLike.IsMatch(exe)) return true; // empty is reported elsewhere; URLs and shell: targets cannot be checked
        if (Path.IsPathRooted(exe)) return File.Exists(exe) || Directory.Exists(exe);

        var names = Path.HasExtension(exe) ? new[] { exe } : new[] { exe, exe + ".exe" };
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(Environment.SystemDirectory).Append(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).Append(Deck.AppDir);
        foreach (var d in dirs)
            foreach (var n in names)
                try { if (File.Exists(Path.Combine(d.Trim('"'), n))) return true; } catch { }

        // "App Paths": how the shell finds chrome.exe & co. without a PATH entry
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            foreach (var n in names)
                try { using var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + n); if (k != null) return true; } catch { }
        return false;
    }

    /// <returns>for each step, null when it runs, else why it is switched off: disabled by the user, or depending on a disabled step
    /// (a step aimed at the windows of a disabled Launch step's application, or at the window of a disabled Browser step)</returns>
    public static string?[] Skipped(IReadOnlyList<Step> steps)
    {
        var res = new string?[steps.Count];
        var enabledProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disabledProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in steps.Where(s => s.Type == StepType.Launch))
        {
            var proc = Path.GetFileNameWithoutExtension(Environment.ExpandEnvironmentVariables(s.Exe.Trim().Trim('"')));
            (s.Enabled ? enabledProcs : disabledProcs).Add(proc);
        }
        disabledProcs.ExceptWith(enabledProcs); // another enabled step still launches this application
        int lastBrowser = -1;
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (!s.Enabled) res[i] = "Désactivée";
            else if (s.Type != StepType.Launch && s.Type != StepType.Browser && s.OnLastWindow && lastBrowser >= 0 && res[lastBrowser] != null)
                res[i] = $"Désactivée : l'étape Navigateur n° {lastBrowser + 1} est désactivée";
            else if (s.Type != StepType.Launch && s.Process != "" && disabledProcs.Contains(s.Process))
                res[i] = $"Désactivée : {s.Process} n'est pas lancée";
            if (s.Type == StepType.Browser) lastBrowser = i;
        }
        return res;
    }

    /// <returns>for each step, null when fine, else why it is skipped: the missing executable, or a step aimed at that application's windows</returns>
    public static string?[] Analyze(IReadOnlyList<Step> steps)
    {
        var res = new string?[steps.Count];
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // process names of the missing executables
        for (int i = 0; i < steps.Count; i++)
            if (steps[i].Enabled && steps[i].Type == StepType.Launch && !ExeExists(steps[i].Exe))
            {
                res[i] = "Introuvable : " + steps[i].Exe;
                missing.Add(Path.GetFileNameWithoutExtension(Environment.ExpandEnvironmentVariables(steps[i].Exe.Trim().Trim('"'))));
            }
        for (int i = 0; i < steps.Count; i++)
            if (res[i] == null && steps[i].Type != StepType.Launch && steps[i].Process != "" && missing.Contains(steps[i].Process))
                res[i] = $"Ignorée : {steps[i].Process} est introuvable";
        return res;
    }
}
