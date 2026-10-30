using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

[TypeConverter(typeof(FrEnumConverter))] enum StepType { Launch, Place, Click, Window, Tray, Browser, Obs }
[TypeConverter(typeof(FrEnumConverter))] enum BrowserKind { Defaut, Chrome, Edge, Firefox, Brave, Opera, Vivaldi }
[TypeConverter(typeof(FrEnumConverter))] enum WindowAction { Reduire, Agrandir, Restaurer, Deplacer }
[TypeConverter(typeof(FrEnumConverter))] enum ClickMode { Element, Coords }
[TypeConverter(typeof(FrEnumConverter))] enum MouseBtn { Left, Right }
[TypeConverter(typeof(FrEnumConverter))] enum Zone { Manuel, Grille, Gauche, Droite, Haut, Bas, HautGauche, HautDroite, BasGauche, BasDroite, TiersGauche, TiersCentre, TiersDroit }

class Step
{
    [DisplayName("Type d'étape"), Category("Délais")] public StepType Type { get; set; }
    [DisplayName("Attente avant (ms)"), Category("Délais"), Description("Attente avant l'étape (ms)")] public int DelayMs { get; set; } = 500;
    [DisplayName("Délai maximum (ms)"), Category("Délais"), Description("Délai max pour trouver la fenêtre / l'élément (ms)")] public int TimeoutMs { get; set; } = 15000;

    [DisplayName("Application, fichier ou URL"), Category("Lancement"), Description("Exécutable, document ou URL")] public string Exe { get; set; } = "";
    [DisplayName("Arguments"), Category("Lancement")] public string Args { get; set; } = "";
    [DisplayName("Ne pas relancer si déjà lancée"), Category("Lancement"), Description("Ne pas relancer si un processus du même nom tourne déjà"), TypeConverter(typeof(FrBool))] public bool SkipIfRunning { get; set; } = true;

    [DisplayName("Garder ouverte à l'arrêt du live"), Category("Arrêt du live"), Description("Si oui, les fenêtres de cette application ne sont pas fermées quand on arrête le live"), TypeConverter(typeof(FrBool))] public bool KeepOpen { get; set; }

    [DisplayName("Serveur WebSocket"), Category("OBS Studio"), Description("Adresse du serveur WebSocket d'OBS (Outils > Réglages du serveur WebSocket dans OBS). Par défaut localhost:4455.")] public string ObsHost { get; set; } = "localhost:4455";
    [DisplayName("Mot de passe WebSocket"), Category("OBS Studio"), Description("Mot de passe du serveur WebSocket d'OBS (laisser vide s'il n'y en a pas). Enregistré en clair dans profiles.json."), PasswordPropertyText(true)] public string ObsPassword { get; set; } = "";
    [DisplayName("Scène à activer"), Category("OBS Studio"), Description("Scène OBS à passer en sortie. La liste est lue dans OBS quand il est lancé ; sinon, tape le nom exact."), TypeConverter(typeof(ObsSceneConverter))] public string ObsScene { get; set; } = "";

    [DisplayName("Processus (sans .exe)"), Category("Fenêtre cible"), Description("Nom du processus sans .exe")] public string Process { get; set; } = "";
    [DisplayName("Titre contient"), Category("Fenêtre cible"), Description("Texte contenu dans le titre (vide = n'importe quel titre)")] public string Title { get; set; } = "";

    [DisplayName("Fenêtre du navigateur"), Category("Fenêtre cible"), Description("Vrai : cible la fenêtre ouverte par l'étape Navigateur précédente, sans chercher par titre ni processus (idéal pour les pages dont le titre change)."), TypeConverter(typeof(FrBool))] public bool OnLastWindow { get; set; }
    [DisplayName("Classe de fenêtre"), Category("Fenêtre cible"), Description("Classe de fenêtre Windows (stable même si le titre change). Capturée automatiquement ; vide = ignorée.")] public string WindowClass { get; set; } = "";
    [DisplayName("Titre obligatoire"), Category("Fenêtre cible"), Description("Faux : si aucune fenêtre ne contient le titre, la fenêtre du bon processus (et de la bonne classe) est utilisée après un court délai. Vrai : le titre est obligatoire."), TypeConverter(typeof(FrBool))] public bool TitleStrict { get; set; }

    [DisplayName("Écran"), Category("Placement"), TypeConverter(typeof(MonitorConverter)), Description("Nom de l'écran, ex. \\\\.\\DISPLAY2")] public string Monitor { get; set; } = "";
    [Category("Placement"), DisplayName("Multitâche"), Description("Multitâche (écran scindé) : Manuel = X/Y/W/H. Grille = écran scindé en Cols x Rows. Autres = zones prédéfinies (moitiés, quarts, tiers). Zones calculées sur la zone de travail (hors barre des tâches).")] public Zone Zone { get; set; }
    [DisplayName("Colonnes"), Category("Placement"), Description("Grille : nombre de colonnes")] public int Cols { get; set; } = 2;
    [DisplayName("Lignes"), Category("Placement"), Description("Grille : nombre de lignes")] public int Rows { get; set; } = 1;
    [DisplayName("Colonne de départ"), Category("Placement"), Description("Grille : colonne de départ (0 = première)")] public int Col { get; set; }
    [DisplayName("Ligne de départ"), Category("Placement"), Description("Grille : ligne de départ (0 = première)")] public int Row { get; set; }
    [DisplayName("Colonnes occupées"), Category("Placement"), Description("Grille : nombre de colonnes occupées")] public int SpanCols { get; set; } = 1;
    [DisplayName("Lignes occupées"), Category("Placement"), Description("Grille : nombre de lignes occupées")] public int SpanRows { get; set; } = 1;
    [DisplayName("Position X"), Category("Placement"), Description("Mode Manuel : position X relative à l'écran")] public int X { get; set; }
    [DisplayName("Position Y"), Category("Placement")] public int Y { get; set; }
    [DisplayName("Largeur"), Category("Placement"), Description("0 = ne pas redimensionner")] public int W { get; set; }
    [DisplayName("Hauteur"), Category("Placement")] public int H { get; set; }
    [DisplayName("Fenêtre agrandie"), Category("Placement"), TypeConverter(typeof(FrBool))] public bool Maximize { get; set; }

    [DisplayName("Navigateur"), Category("Navigateur"), Description("Type Browser : navigateur à ouvrir (Defaut = navigateur par défaut de Windows, sans garantie de nouvelle fenêtre)")] public BrowserKind Browser { get; set; }
    [DisplayName("Adresse (URL)"), Category("Navigateur"), Description("Adresse http(s) à ouvrir dans une nouvelle fenêtre du navigateur")] public string Url { get; set; } = "https://";
    [DisplayName("Placer la nouvelle fenêtre"), Category("Navigateur"), Description("Place la nouvelle fenêtre du navigateur avec le placement de cette étape (Monitor, Multitâche/Zone, X, Y, W, H). Sans effet avec Defaut."), TypeConverter(typeof(FrBool))] public bool PlaceWindow { get; set; }
    [DisplayName("Nom de l'icône"), Category("Zone de notification"), Description("Type Tray : texte du nom (info-bulle) de l'icône de la zone de notification à cliquer pour faire apparaître l'application. Le bouton \"Icônes du tray\" liste les noms disponibles. Bouton, Double : voir catégorie Clic.")] public string TrayName { get; set; } = "";
    [DisplayName("Action"), Category("Action fenêtre"), Description("Type Window : réduire, agrandir, restaurer ou déplacer la fenêtre cible (Process / Title). Déplacer applique le placement (Monitor, Multitâche/Zone, X, Y, W, H) de cette étape.")] public WindowAction Action { get; set; }

    [DisplayName("Mode de clic"), Category("Clic"), Description("Element = cherche le contrôle par nom (UI Automation). Coords = position relative à la fenêtre.")] public ClickMode Mode { get; set; } = ClickMode.Element;
    [DisplayName("Nom de l'élément"), Category("Clic")] public string ElementName { get; set; } = "";
    [DisplayName("Identifiant de l'élément"), Category("Clic")] public string ElementId { get; set; } = "";
    [DisplayName("X dans la fenêtre"), Category("Clic")] public int RelX { get; set; }
    [DisplayName("Y dans la fenêtre"), Category("Clic")] public int RelY { get; set; }
    [DisplayName("Bouton de la souris"), Category("Clic")] public MouseBtn Button { get; set; }
    [DisplayName("Double-clic"), Category("Clic"), TypeConverter(typeof(FrBool))] public bool Double { get; set; }

    /// <summary>why this step will be skipped (missing executable...), set by <see cref="Checks.Analyze"/>; not saved</summary>
    [JsonIgnore, Browsable(false)] public string? Issue { get; set; }

    public Step Clone() => (Step)MemberwiseClone();

    public override string ToString() => $"+{DelayMs}ms  " + Type switch
    {
        StepType.Launch => $"Lancer {Exe} {Args}".TrimEnd(),
        StepType.Place => Zone switch
        {
            Zone.Manuel => $"Placer {Process} '{Title}' → {Config.Label(Monitor)} {X},{Y} {W}x{H}{(Maximize ? " max" : "")}",
            Zone.Grille => $"Placer {Process} '{Title}' → {Config.Label(Monitor)} grille {Cols}x{Rows} case {Col},{Row} ({SpanCols}x{SpanRows})",
            _ => $"Placer {Process} '{Title}' → {Config.Label(Monitor)} multitâche {Fr.Of(Zone)}",
        },
        StepType.Browser => $"Ouvrir {Fr.Of(Browser)} sur {Url}",
        StepType.Obs => $"OBS : scène « {ObsScene} »",
        StepType.Tray => $"Ouvrir depuis la zone de notification « {TrayName} » ({Fr.Of(Button)}{(Double ? " x2" : "")})",
        StepType.Window => $"{Fr.Of(Action)} la fenêtre {Process} '{Title}'",
        _ => Mode == ClickMode.Element
            ? $"Clic {Fr.Of(Button)}{(Double ? " x2" : "")} sur « {ElementName} » ({Process})"
            : $"Clic {Fr.Of(Button)}{(Double ? " x2" : "")} à {RelX},{RelY} ({Process})",
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
    /// <summary>profile list folded to a thin strip</summary>
    public bool SidebarCollapsed { get; set; }
    /// <summary>update channel: "main" (stable releases) or "dev" (pre-releases built from the dev branch)</summary>
    public string UpdateChannel { get; set; } = "main";
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

    static string FilePath => Path.Combine(Deck.AppDir, "profiles.json");
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

/// <summary>Libellés français des valeurs d'énumération (affichage uniquement : le JSON garde les noms internes).</summary>
static class Fr
{
    static readonly Dictionary<string, string> L = new()
    {
        ["StepType.Launch"] = "Lancer une application", ["StepType.Place"] = "Placer une fenêtre", ["StepType.Click"] = "Clic",
        ["StepType.Window"] = "Action sur une fenêtre", ["StepType.Tray"] = "Icône de la zone de notification", ["StepType.Browser"] = "Site web", ["StepType.Obs"] = "Scène OBS",
        ["ClickMode.Element"] = "Élément (par son nom)", ["ClickMode.Coords"] = "Coordonnées dans la fenêtre",
        ["MouseBtn.Left"] = "Gauche", ["MouseBtn.Right"] = "Droit",
        ["WindowAction.Reduire"] = "Réduire", ["WindowAction.Agrandir"] = "Agrandir", ["WindowAction.Restaurer"] = "Restaurer", ["WindowAction.Deplacer"] = "Déplacer",
        ["BrowserKind.Defaut"] = "Navigateur par défaut",
        ["Zone.Manuel"] = "Manuel (position et taille)", ["Zone.Grille"] = "Grille personnalisée",
        ["Zone.Gauche"] = "Moitié gauche", ["Zone.Droite"] = "Moitié droite", ["Zone.Haut"] = "Moitié haute", ["Zone.Bas"] = "Moitié basse",
        ["Zone.HautGauche"] = "Quart haut gauche", ["Zone.HautDroite"] = "Quart haut droit", ["Zone.BasGauche"] = "Quart bas gauche", ["Zone.BasDroite"] = "Quart bas droit",
        ["Zone.TiersGauche"] = "Tiers gauche", ["Zone.TiersCentre"] = "Tiers central", ["Zone.TiersDroit"] = "Tiers droit",
    };

    public static string Of(Enum e) => L.TryGetValue($"{e.GetType().Name}.{e}", out var s) ? s : e.ToString();

    public static object? Parse(Type enumType, string text)
    {
        foreach (var v in Enum.GetValues(enumType))
            if (Of((Enum)v).Equals(text, StringComparison.OrdinalIgnoreCase) || v.ToString()!.Equals(text, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }
}

class FrEnumConverter : EnumConverter
{
    readonly Type type;
    public FrEnumConverter(Type type) : base(type) => this.type = type;

    public override object? ConvertTo(ITypeDescriptorContext? c, System.Globalization.CultureInfo? ci, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is Enum e ? Fr.Of(e) : base.ConvertTo(c, ci, value, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? c, System.Globalization.CultureInfo? ci, object value) =>
        value is string s ? Fr.Parse(type, s) ?? base.ConvertFrom(c, ci, value) : base.ConvertFrom(c, ci, value);
}

class FrBool : BooleanConverter
{
    public override object? ConvertTo(ITypeDescriptorContext? c, System.Globalization.CultureInfo? ci, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is bool b ? (b ? "Oui" : "Non") : base.ConvertTo(c, ci, value, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? c, System.Globalization.CultureInfo? ci, object value) =>
        value is string s && s.Equals("Oui", StringComparison.OrdinalIgnoreCase) ? true
        : value is string t && t.Equals("Non", StringComparison.OrdinalIgnoreCase) ? false : base.ConvertFrom(c, ci, value);
}
