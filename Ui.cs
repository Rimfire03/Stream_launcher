using System.ComponentModel;
using System.Drawing.Drawing2D;

/// <summary>Charte graphique et petits composants de l'interface.</summary>
static class Ui
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#F5F4FA");
    public static readonly Color Card = Color.White;
    public static readonly Color Header = ColorTranslator.FromHtml("#1F1B2E");
    public static readonly Color Primary = ColorTranslator.FromHtml("#6441A5");
    public static readonly Color PrimaryHover = ColorTranslator.FromHtml("#7A57BE");
    public static readonly Color Danger = ColorTranslator.FromHtml("#C0392B");
    public static readonly Color DangerHover = ColorTranslator.FromHtml("#D64535");
    public static readonly Color Text = ColorTranslator.FromHtml("#231F33");
    public static readonly Color Muted = ColorTranslator.FromHtml("#6B6880");
    public static readonly Color Border = ColorTranslator.FromHtml("#DDD9EA");
    public static readonly Color Selected = ColorTranslator.FromHtml("#ECE6FA");
    public static readonly Color Hover = ColorTranslator.FromHtml("#F1EEF8");
    public static readonly Color LiveGreen = ColorTranslator.FromHtml("#2E9E5B");

    public static readonly Font Base = new("Segoe UI", 9.5f);
    public static readonly Font Bold = new("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 14f);
    public static readonly Font Mono = new("Consolas", 9f);

    public enum Kind { Primary, Secondary, Danger, Ghost }

    public static Button Btn(string text, Kind kind, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 32),
            FlatStyle = FlatStyle.Flat, Font = Base, Cursor = Cursors.Hand, Padding = new Padding(10, 0, 10, 0), Margin = new Padding(0, 0, 8, 0),
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = kind is Kind.Secondary or Kind.Ghost ? 1 : 0;
        b.FlatAppearance.BorderColor = kind == Kind.Ghost ? Bg : Border;
        Style(b, kind);
        b.Click += onClick;
        return b;
    }

    public static void Style(Button b, Kind kind)
    {
        (b.BackColor, b.ForeColor, b.FlatAppearance.MouseOverBackColor) = kind switch
        {
            Kind.Primary => (Primary, Color.White, PrimaryHover),
            Kind.Danger => (Danger, Color.White, DangerHover),
            Kind.Secondary => (Card, Text, Hover),
            _ => (Bg, Text, Border),
        };
    }

    public static Label Heading(string text) => new() { Text = text, Font = Bold, ForeColor = Muted, AutoSize = true, Padding = new Padding(0, 0, 0, 4) };

    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---- step presentation ----

    public static (string Label, Color Color) Badge(StepType t) => t switch
    {
        StepType.Launch => ("LANCER", Primary),
        StepType.Place => ("PLACER", ColorTranslator.FromHtml("#0E8A7D")),
        StepType.Click => ("CLIC", ColorTranslator.FromHtml("#D9822B")),
        StepType.Window => ("FENÊTRE", ColorTranslator.FromHtml("#3B6FD4")),
        StepType.Tray => ("TRAY", ColorTranslator.FromHtml("#B24A7A")),
        StepType.Obs => ("OBS", ColorTranslator.FromHtml("#4B5563")),
        StepType.Chat => ("CHAT", ColorTranslator.FromHtml("#0E8A7D")),
        _ => ("WEB", ColorTranslator.FromHtml("#2E7D32")),
    };

    static string ZoneLabel(Step s) => s.Zone switch
    {
        Zone.Manuel => $"{s.W}x{s.H} en {s.X},{s.Y}{(s.Maximize ? " (agrandie)" : "")}",
        Zone.Grille => $"grille {s.Cols}x{s.Rows}, case {s.Col},{s.Row}",
        var z => Fr.Of(z).ToLowerInvariant(),
    };

    static string Target(Step s) => s.OnLastWindow ? "fenêtre du navigateur ouvert"
        : s.Process != "" ? s.Process + (s.Title != "" ? $" · « {s.Title} »" : "") : s.Title != "" ? $"« {s.Title} »" : "fenêtre non définie";

    public static (string Main, string Sub) Describe(Step s) => s.Type switch
    {
        StepType.Launch => (Path.GetFileName(s.Exe.TrimEnd('\\', '/')) is { Length: > 0 } f ? f : s.Exe, string.IsNullOrWhiteSpace(s.Args) ? s.Exe : $"{s.Args}  ·  {s.Exe}"),
        StepType.Place => ($"Placer {(s.Process != "" ? s.Process : "la fenêtre")}", $"{Config.Label(s.Monitor)} · {ZoneLabel(s)} · {Target(s)}"),
        StepType.Window => ($"{(s.Action == WindowAction.Deplacer ? "Déplacer" : s.Action == WindowAction.Reduire ? "Réduire" : s.Action == WindowAction.Agrandir ? "Agrandir" : "Restaurer")} la fenêtre",
            s.Action == WindowAction.Deplacer ? $"{Target(s)} → {Config.Label(s.Monitor)} · {ZoneLabel(s)}" : Target(s)),
        StepType.Click => (s.Mode == ClickMode.Element ? $"Cliquer sur « {s.ElementName} »" : $"Cliquer à {s.RelX}, {s.RelY}",
            $"{Target(s)} · bouton {(s.Button == MouseBtn.Right ? "droit" : "gauche")}{(s.Double ? " · double-clic" : "")}"),
        StepType.Tray => ($"Ouvrir « {s.TrayName} » depuis la zone de notification", $"bouton {(s.Button == MouseBtn.Right ? "droit" : "gauche")}{(s.Double ? " · double-clic" : "")}"),
        StepType.Chat => ("Ouvrir le chat unifié", "Twitch, Kick et YouTube dans une fenêtre dédiée"),
        StepType.Obs => (s.ObsScene != "" ? $"Activer la scène « {s.ObsScene} »" : s.ObsCollection != "" ? $"Charger la collection « {s.ObsCollection} »" : $"Charger le profil « {s.ObsProfile} »",
            "OBS Studio" + (s.ObsProfile != "" && (s.ObsScene != "" || s.ObsCollection != "") ? $" · profil « {s.ObsProfile} »" : "") + (s.ObsCollection != "" && s.ObsScene != "" ? $" · collection « {s.ObsCollection} »" : "") + $" · {s.ObsHost}"),
        _ => (Uri.TryCreate(s.Url, UriKind.Absolute, out var u) ? $"Ouvrir {u.Host}" : "Ouvrir un site", $"{Fr.Of(s.Browser)} · {s.Url}{(s.PlaceWindow ? " · fenêtre placée" : "")}"),
    };
}

/// <summary>Montre dans la grille de propriétés seulement les réglages utiles au type d'étape.</summary>
class StepView : ICustomTypeDescriptor
{
    readonly Step s;
    public StepView(Step s) => this.s = s;
    public Step Step => s;

    HashSet<string> Visible()
    {
        var h = new HashSet<string> { "DelayMs", "TimeoutMs" };
        void Target() => h.UnionWith(new[] { "Process", "WindowClass", "Title", "TitleStrict", "OnLastWindow" });
        void Placement()
        {
            h.Add("Monitor"); h.Add("Zone");
            if (s.Zone == Zone.Grille) h.UnionWith(new[] { "Cols", "Rows", "Col", "Row", "SpanCols", "SpanRows" });
            if (s.Zone == Zone.Manuel) h.UnionWith(new[] { "X", "Y", "W", "H", "Maximize" });
        }
        switch (s.Type)
        {
            case StepType.Launch: h.UnionWith(new[] { "Exe", "Args", "SkipIfRunning", "KeepOpen" }); break;
            case StepType.Place: Target(); Placement(); break;
            case StepType.Window: Target(); h.Add("Action"); if (s.Action == WindowAction.Deplacer) Placement(); break;
            case StepType.Click:
                Target(); h.UnionWith(new[] { "Mode", "Button", "Double" });
                h.UnionWith(s.Mode == ClickMode.Element ? new[] { "ElementName", "ElementId" } : new[] { "RelX", "RelY" });
                break;
            case StepType.Tray: h.UnionWith(new[] { "TrayName", "Button", "Double" }); break;
            case StepType.Chat: break; // nothing to set: only the delay
            case StepType.Obs: h.UnionWith(new[] { "ObsHost", "ObsPassword", "ObsProfile", "ObsCollection", "ObsScene" }); break;
            default: h.UnionWith(new[] { "Browser", "Url", "PlaceWindow", "KeepOpen" }); if (s.PlaceWindow) Placement(); break;
        }
        return h;
    }

    public PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
    {
        var vis = Visible();
        var all = TypeDescriptor.GetProperties(s, attributes ?? Array.Empty<Attribute>());
        return new PropertyDescriptorCollection(all.Cast<PropertyDescriptor>().Where(p => vis.Contains(p.Name)).ToArray());
    }

    public PropertyDescriptorCollection GetProperties() => GetProperties(null);
    public object GetPropertyOwner(PropertyDescriptor? pd) => s;
    public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(s, true);
    public string? GetClassName() => "Étape";
    public string? GetComponentName() => null;
    public TypeConverter GetConverter() => TypeDescriptor.GetConverter(s, true);
    public EventDescriptor? GetDefaultEvent() => null;
    public PropertyDescriptor? GetDefaultProperty() => null;
    public object? GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(s, editorBaseType, true);
    public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
    public EventDescriptorCollection GetEvents(Attribute[]? attributes) => EventDescriptorCollection.Empty;
}
