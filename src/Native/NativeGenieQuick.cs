using Godot;
using LibreKO;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The client's genie quick panel (<c>_genieSub</c>): a caption row (title, Start/Stop toggle, settings button), the
/// remaining-time gauge with its minutes and three monster rows. The client shows it whenever the Genie has time or
/// runs and has no way to put it away. The preparer names its parts for the Classic skin and keeps the player's
/// minimise and close choice for the session; the client keeps every callback, refresh and its drag handle.
/// </summary>
public static class NativeGenieQuick
{
    public const string Column = "genie_quick_column", Head = "genie_quick_head", Title = "genie_quick_title",
        Toggle = "genie_quick_toggle", Settings = "genie_quick_settings", Gauge = "genie_quick_gauge",
        Points = "genie_quick_points", Row = "genie_quick_row_";

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node, WorldRoot> _worlds = new();

    /// <summary>The player's choice for this session: the panel folded to its caption and time rows.</summary>
    public static bool Minimised { get; set; }

    /// <summary>The player closed the panel; the Genie window or a Genie start brings it back.</summary>
    public static bool Closed { get; set; }

    internal static void Bind(Node panel, WorldRoot world) => _worlds.AddOrUpdate(panel, world);

    public static WorldRoot? WorldOf(Node panel) => _worlds.TryGetValue(panel, out var world) && GodotObject.IsInstanceValid(world) ? world : null;

    public readonly record struct Parts(VBoxContainer Column, HBoxContainer Head, Label Title, Button Toggle, Button Settings,
        Control Gauge, Label Points, IReadOnlyList<HBoxContainer> Rows);

    /// <summary>Finds and names the client's parts: the column, its caption row, the gauge host and the monster rows.</summary>
    public static Parts? Prepare(Control panel)
    {
        if (Native.Descendants(panel).OfType<VBoxContainer>().FirstOrDefault() is not { } column) return null;
        var children = column.GetChildren().OfType<Control>().ToArray();
        if (children.Length < 2 || children[0] is not HBoxContainer head) return null;
        var buttons = head.GetChildren().OfType<Button>().ToArray();
        if (head.GetChildren().OfType<Label>().FirstOrDefault() is not { } title || buttons.Length < 2) return null;
        var gauge = children[1];
        if (gauge.GetChildren().OfType<Label>().FirstOrDefault() is not { } points) return null;
        var rows = children.Skip(2).OfType<HBoxContainer>().ToList();
        column.Name = Column; head.Name = Head; title.Name = Title; buttons[0].Name = Toggle; buttons[1].Name = Settings;
        gauge.Name = Gauge; points.Name = Points;
        for (int i = 0; i < rows.Count; i++) rows[i].Name = Row + i;
        return new Parts(column, head, title, buttons[0], buttons[1], gauge, points, rows);
    }

    /// <summary>Whether the client's Genie window is open.</summary>
    public static bool WindowShown(WorldRoot? world) => world != null && Native.Has(world, "Genie.shown") && Native.Get<bool>(world, "Genie.shown");

    public static bool Running => Game.Session.Genie.GenieRunning == true;

    /// <summary>The client writes the minutes as a bare number; the Classic gauge names the unit.</summary>
    public static string TimeText(string minutes) => int.TryParse(minutes, out int value) ? value + " min" : minutes;
}
