using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

enum StepType { Launch, Place, Click }
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
    [Category("Placement"), Description("Manuel = X/Y/W/H. Grille = écran scindé en Cols x Rows. Autres = zones prédéfinies (moitiés, quarts, tiers). Zones calculées sur la zone de travail (hors barre des tâches).")] public Zone Zone { get; set; }
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
            Zone.Manuel => $"Placer {Process} '{Title}' → {Monitor} {X},{Y} {W}x{H}{(Maximize ? " max" : "")}",
            Zone.Grille => $"Placer {Process} '{Title}' → {Monitor} grille {Cols}x{Rows} case {Col},{Row} ({SpanCols}x{SpanRows})",
            _ => $"Placer {Process} '{Title}' → {Monitor} zone {Zone}",
        },
        _ => Mode == ClickMode.Element
            ? $"Clic {Button}{(Double ? " x2" : "")} sur « {ElementName} » ({Process})"
            : $"Clic {Button}{(Double ? " x2" : "")} à {RelX},{RelY} ({Process})",
    };
}

class MonitorConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? c) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? c) =>
        new(Screen.AllScreens.Select(s => s.DeviceName).ToArray());
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

    static string FilePath => Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "profiles.json");
    static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Config Load()
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
