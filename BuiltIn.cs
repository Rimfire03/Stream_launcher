/// <summary>
/// Application credentials built into a release. They are NOT in the source repository: tools\Build-Release.ps1 reads them from the
/// private keys repository and writes KickSecrets.g.cs (git-ignored) for the time of the build. A local debug build has none,
/// so the user enters their own (Live > Éditer… > Identifiants…).
/// </summary>
static partial class BuiltIn
{
    public static string KickId = "";
    public static string KickSecret = "";

    static BuiltIn() => Load();

    static partial void Load(); // implemented by the generated KickSecrets.g.cs, when present
}
