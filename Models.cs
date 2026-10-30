using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

enum StepType { Launch, Place, Click, Window, Tray }
enum WindowAction { Reduire, Agrandir, Restaurer, Deplacer }
enum ClickMode { Element, Coords }
enum MouseBtn { Left, Right }
enum Zone { Manuel, Grille, Gauche, Droite, Haut, Bas, HautGauche, HautDroite, BasGauche, BasDroite, TiersGauche, TiersCentre, TiersDroit }

class Step
{
    [Category("Général")] public StepType Type { get; set; }
    [Category("Général"), Description("Attente avant l'étape (ms)")] public int DelayMs { get; set; } = 500;
    [Category("Général"), Description("Délai max pour trouver la fenêtre / l'élément (ms)")] public int TimeoutMs { get; set; } = 15000;

    [Category("Lancement"), Description("Exécutable, document ou URL")] public string Exe { get; set; } = "";
    [Category("Lancement")] public string Args { get; set; } = "";
    [Category("Lancement"), Description("Ne pas relancer si un processus du même nom tourne déjà")] public bool SkipIfRunning { get; set; } = true;

    [Category("Fenêtre cible"), Description("Nom du processus sans .exe")] public string Process { get; set; } = "";
    [Category("Fenêtre cible"), Description("Texte contenu dans le titre (vide = n'importe quel titre)")] public string Title { get; set; } = "";

    [Category("Placement"), TypeConverter(typeof(MonitorConverter)), Description("Nom de l'écran, ex. \\\\.\\DISPLAY2")] public string Monitor { get; set; } = "";
    [Category("Placement"), DisplayName("Multitâche"), Description("Multitâche (écran scindé) : Manuel = X/Y/W/H. Grille = écran scindé en Cols x Rows. Autres = zones prédéfinies (moitiés, quarts, tiers). Zones calculées sur la zone de travail (hors barre des tâches).")] public Zone Zone { get; set; }
    [Category("Placement"), Description("Grille : nombre de colonnes")] public int Cols { get; set; } = 2;
    [Category("Placement"), Description("Grille : nombre de lignes")] public int Rows { get; set; } = 1;
    [Category("Placement"), Description("Grille : colonne de départ (0 = première)")] public int Col { get; set; }
    [Category("Placement"), Description("Grille : ligne de départ (0 = première)")] public int Row { get; set; }
    [Category("Placement"), Description("Grille : nombre de colonnes occupées")] public int SpanCols { get; set; } = 1;
    [Category("Placement"), Description("Grille : nombre de lignes occupées")] public int SpanRows { get; set; } = 1;
    [Category("Placement"), Description("Mode Manuel : position X relative à l'écran")] public int X { get; set; }
    [Category("Placement")] public int Y { get; set; }
    [Category("Placement"), Description("0 = ne pas redimensionner")] public int W { get; set; }
    [Category("Placement")] public int H { get; set; }
    [Category("Placement")] public bool Maximize { get; set; }

    [Category("Zone de notification"), Description("Type Tray : texte du nom (info-bulle) de l'icône de la zone de notification à cliquer pour faire apparaître l'application. Le bouton \"Icônes du tray\" liste les noms disponibles. Bouton, Double : voir catégorie Clic.")] public string TrayName { get; set; } = "";
    [Category("Action fenêtre"), Description("Type Window : réduire, agrandir, restaurer ou déplacer la fenêtre cible (Process / Title). Déplacer applique le placement (Monitor, Multitâche/Zone, X, Y, W, H) de cette étape.")] public WindowAction Action { get; set; }

    [Category("Clic"), Description("Element = cherche le contrôle par nom (UI Automation). Coords = position relative à la fenêtre.")] public ClickMode Mode { get; set; } = ClickMode.Element;
    [Category("Clic")] public string ElementName { get; set; } = "";
    [Category("Clic")] public string ElementId { get; set; } = "";
    [Category("Clic")] public int RelX { get; set; }
    [Category("Clic")] public int RelY { get; set; }
    [Category("Clic")] public MouseBtn Button { get; set; }
    [Category("Clic")] public bool Double { get; set; }

    public override string ToString() => $"+{DelayMs}ms  " + Type switch
    {
        StepType.Launch => $"Lancer {Exe} {Args}".TrimEnd(),
        StepType.Place => Zone switch
        {
            Zone.Manuel => $"Placer {Process} '{Title}' → {Config.Label(Monitor)} {X},{Y} {W}x{H}{(Maximize ? " max" : "")}",
            Zone.Grille => $"Placer {Process} '{Title}' → {Config.Label(Monitor)} grille {Cols}x{Rows} case {Col},{Row} ({SpanCols}x{SpanRows})",
            _ => $"Placer {Process} '{Title}' → {Config.Label(Monitor)} multitâche {Zone}",
        },
        StepType.Tray => $"Ouvrir depuis la zone de notification « {TrayName} » ({Button}{(Double ? " x2" : "")})",
        StepType.Window => $"{Action} la fenêtre {Process} '{Title}'",
        _ => Mode == ClickMode.Element
            ? $"Clic {Button}{(Double ? " x2" : "")} sur « {ElementName} » ({Process})"
            : $"Clic {Button}{(Double ? " x2" : "")} à {RelX},{RelY} ({Process})",
    };
}

// Steps store the Windows device name (\\.\DISPLAYn, stable under renaming); the grid shows "Custom name (device)".
class MonitorConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? c) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? c) =>
        new(Screen.AllScreens.Select(s => s.DeviceName).ToArray());
    public override object? ConvertTo(ITypeDescriptorContext? c, System.Globalization.CultureInfo? ci, object? v, Type t) =>
        t == typeof(string) && v is string s ? Config.Label(s) : base.ConvertTo(c, ci, v, t);
    public override object? ConvertFrom(ITypeDescriptorContext? c, System.Globalization.CultureInfo? ci, object v) =>
        v is string s ? Config.Resolve(s) : base.ConvertFrom(c, ci, v);
}

class Profile
{
    public string Name { get; set; } = "Nouveau profil";
    public List<Step> Steps { get; set; } = new();
    public override string ToString() => Name;
}

class Config
{
    public List<Profile> Profiles { get; set; } = new();
    /// <summary>device name (\\.\DISPLAY2) -> custom name</summary>
    public Dictionary<string, string> ScreenNames { get; set; } = new();

    [JsonIgnore] public static Config Current { get; private set; } = new();

    public static string Label(string device) =>
        Current.ScreenNames.TryGetValue(device, out var n) && n != "" ? $"{n} ({device})" : device;

    /// <summary>accepts a device name, a custom name, or a "Custom (device)" label</summary>
    public static string Resolve(string input)
    {
        input = input.Trim();
        foreach (var s in Screen.AllScreens)
            if (input.Equals(s.DeviceName, StringComparison.OrdinalIgnoreCase) || input.Equals(Label(s.DeviceName), StringComparison.OrdinalIgnoreCase) ||
                (Current.ScreenNames.TryGetValue(s.DeviceName, out var n) && input.Equals(n, StringComparison.OrdinalIgnoreCase)))
                return s.DeviceName;
        return input;
    }

    static string FilePath => Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "profiles.json");
    static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Config Load() => Current = LoadFile();

    static Config LoadFile()
    {
        if (!File.Exists(FilePath)) return new();
        try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Opt) ?? new(); }
        catch { File.Copy(FilePath, FilePath + ".bak", true); return new(); }
    }

    public void Save()
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opt));
        File.Move(tmp, FilePath, true);
    }
}
