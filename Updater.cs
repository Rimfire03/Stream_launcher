using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

/// <summary>Version "Va.b.c" (ex. V0.1.0) et mise à jour depuis les releases GitHub.</summary>
static class Updater
{
    const string Repo = "Rimfire03/Stream_launcher";
    const string SignerThumbprint = "168FFE4B6B2D5E042808B001B5146C6D137F532C"; // certificat global TomLine

    static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", "StreamLauncher" } }, Timeout = TimeSpan.FromMinutes(5) };

    public static Version Current => Assembly.GetEntryAssembly()!.GetName().Version!;

    public static string Format(Version v) => $"V{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    static bool TryParse(string tag, out Version v)
    {
        v = new Version();
        var p = tag.TrimStart('V', 'v').Split('-')[0].Split('.'); // "V0.1.19-dev" -> 0.1.19
        if (p.Length != 3 || !p.All(x => int.TryParse(x, out _))) return false;
        v = new Version(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]));
        return true;
    }

    public static bool IsDev => Config.Current.UpdateChannel == "dev";

    /// <summary>Newest release of the chosen channel: stable = "latest" (no pre-release); dev = newest release of any kind, pre-releases included.</summary>
    /// <returns>(version, url) of a newer release, or null if up to date</returns>
    public static async Task<(Version Ver, string Url)?> CheckAsync()
    {
        var api = $"https://api.github.com/repos/{Repo}/releases";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(IsDev ? api + "?per_page=20" : api + "/latest"));
        var releases = IsDev ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
        var current = new Version(Current.Major, Current.Minor, Math.Max(Current.Build, 0));

        (Version Ver, string Url)? best = null;
        foreach (var r in releases)
        {
            if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
            if (!TryParse(r.GetProperty("tag_name").GetString() ?? "", out var v) || v <= current || (best != null && v <= best.Value.Ver)) continue;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
                if (a.GetProperty("name").GetString() is { } n && n.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    best = (v, a.GetProperty("browser_download_url").GetString()!);
                    break;
                }
        }
        return best;
    }

    /// <summary>Downloads the new MSI, checks its signature, then (once this process has exited) installs it and restarts the app.</summary>
    public static async Task DownloadAndRestartAsync(string url)
    {
        var host = new Uri(url).Host;
        if (!host.EndsWith("github.com") && !host.EndsWith("githubusercontent.com")) throw new InvalidOperationException("URL de mise à jour inattendue : " + host);

        var msi = Path.Combine(Path.GetTempPath(), "StreamLauncher-update.msi");
        await File.WriteAllBytesAsync(msi, await Http.GetByteArrayAsync(url));

        try
        {
            #pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(msi));
            #pragma warning restore SYSLIB0057
            if (cert.Thumbprint != SignerThumbprint) throw new InvalidOperationException("signature inattendue");
        }
        catch (Exception e)
        {
            File.Delete(msi);
            throw new InvalidOperationException("Mise à jour refusée, MSI non signé par TomLine : " + e.Message);
        }

        // msiexec asks for elevation itself (per-machine package); wait for this process to exit so the exe is not locked
        var cmd = Path.Combine(Path.GetTempPath(), "streamlauncher-update.cmd");
        File.WriteAllLines(cmd, new[]
        {
            "@echo off",
            "ping -n 3 127.0.0.1 >nul",
            $"msiexec /i \"{msi}\" /passive /norestart",
            $"start \"\" \"{Environment.ProcessPath}\"",
            "del \"%~f0\"",
        });
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { CreateNoWindow = true, UseShellExecute = false });
    }
}
