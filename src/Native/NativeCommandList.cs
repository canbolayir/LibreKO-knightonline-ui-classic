using System.Collections;
using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The client's command list (<c>World.CommandList</c>): two native <see cref="ItemList"/>s, the groups of the
/// client's <c>CommandCatalog</c> and the commands of the selected group, run by the client's own
/// <c>RunListedCommand</c> when a row is activated. The Classic window regroups the commands for display; it
/// reads every row the client builds (text, hint, availability) and writes the regrouped rows back into the
/// client's own list and its <c>_cmdShown</c> entries, so activating a row still runs the client's command.
/// </summary>
public static class NativeCommandList
{
    public const string Gate = "classic_command_controls";

    /// <summary>
    /// One command as the client lists it. Rows without a client <c>CommandEntry</c> (the Genie rows) are client
    /// actions the Classic list runs itself; they are listed after the entries the client runs.
    /// </summary>
    public sealed record Row(string Id, string ClientGroup, string Text, string Tooltip, bool Available, object? Entry);

    internal static void Register() => NativeWindows.Prepare("command_list", Prepare);

    /// <summary>Names the native lists, reads the client's rows and hands both to the Classic window.</summary>
    public static void Prepare(HudWindow window, WorldRoot world)
    {
        PlaceAndEscape(window, world);
        if (window.HasMeta(Gate)) return;
        var groups = Native.Get<ItemList>(world, "CommandList.Groups");
        var entries = Native.Get<ItemList>(world, "CommandList.entries");
        if (groups == null || entries == null || Native.Get<IList>(world, "CommandList.cmdShown") == null) return;
        if (!Native.HasMethod(world, "CommandList.FillEntries") || Native.ClientType("LibreKO.Domain.CommandCatalog") == null) return;
        NativeWindows.Name(groups, "command_client_groups");
        NativeWindows.Name(entries, "command_entries");
        window.SetMeta(WorldMeta, world);
        window.SetMeta(Gate, 1);
    }

    /// <summary>The World whose command list the window shows (the skin binds once the gate is set).</summary>
    public const string WorldMeta = "classic_command_world";

    public static ItemList? Groups(WorldRoot world) => Native.Get<ItemList>(world, "CommandList.Groups");
    public static ItemList? Entries(WorldRoot world) => Native.Get<ItemList>(world, "CommandList.entries");

    /// <summary>
    /// Every command of the client's catalog in catalog order, read from the rows the client builds for each of
    /// its groups (so names, hints and the "not available yet" state stay the client's). The client's list
    /// is left showing its selected group.
    /// </summary>
    public static List<Row> Read(WorldRoot world)
    {
        var rows = new List<Row>();
        var catalog = Native.ClientType("LibreKO.Domain.CommandCatalog");
        var entries = Entries(world);
        if (catalog == null || entries == null || Native.Get<Array>(catalog, "Groups") is not { } groups) return rows;
        Color unavailable = Native.Get<Color>(Native.StaticOwner("LibreKO.Features.Windows.CommandListFeature"), "UnavailableColour");
        foreach (var group in groups)
        {
            Native.Call(world, "CommandList.FillEntries", group);
            if (Native.Get<IList>(world, "CommandList.cmdShown") is not { } shown) break;
            for (int i = 0; i < shown.Count && i < entries.ItemCount; i++)
            {
                object entry = shown[i]!;
                string id = Native.Get<object>(entry, "Id")?.ToString() ?? "";
                rows.Add(new Row(id, group.ToString()!, entries.GetItemText(i), entries.GetItemTooltip(i),
                    entries.GetItemCustomFgColor(i) != unavailable, entry));
            }
        }
        Restore(world);
        return rows;
    }

    /// <summary>The client's title of one of its groups (its catalog text), or null when the client has no such group.</summary>
    public static string? GroupTitle(WorldRoot world, string clientGroup)
    {
        if (Groups(world) is not { } list || Native.ClientType("LibreKO.Domain.CommandCatalog") is not { } catalog
            || Native.Get<Array>(catalog, "Groups") is not { } groups) return null;
        for (int i = 0; i < groups.Length && i < list.ItemCount; i++)
            if (groups.GetValue(i)?.ToString() == clientGroup) return list.GetItemText(i);
        return null;
    }

    /// <summary>Shows the client's selected group again, as the client itself would.</summary>
    public static void Restore(WorldRoot world)
    {
        if (Groups(world) is not { } list || list.ItemCount == 0) return;
        int[] picked = list.GetSelectedItems();
        if (Native.ClientType("LibreKO.Domain.CommandCatalog") is { } catalog && Native.Get<Array>(catalog, "Groups") is { } groups)
            Native.Call(world, "CommandList.FillEntries", groups.GetValue(picked.Length > 0 ? picked[0] : 0));
    }

    /// <summary>
    /// Shows <paramref name="rows"/> in the client's command list. The client's <c>_cmdShown</c> receives the
    /// same entries in the same order, so the client's activation handler runs the row that was clicked.
    /// </summary>
    public static void Show(WorldRoot world, IReadOnlyList<Row> rows, Action<ItemList, int, Row> style)
    {
        if (Entries(world) is not { } list || Native.Get<IList>(world, "CommandList.cmdShown") is not { } shown) return;
        list.Clear();
        shown.Clear();
        foreach (var row in rows)
        {
            int index = list.AddItem(row.Text);
            list.SetItemTooltip(index, row.Tooltip);
            // Rows past the client's entries are not the client's: its activation handler ignores them.
            if (row.Entry != null && shown.Count == index) shown.Add(row.Entry);
            style(list, index, row);
        }
    }

    private const string PlacedMeta = "classic_command_placed";

    /// <summary>
    /// The window opens at the right edge of the screen, vertically centred, beside the Classic HUD on that side
    /// (<see cref="ClassicCommandListLayout.OpenSpot"/>). It is the client's default position for the window, so a
    /// position the player dragged it to (saved in <c>windows.cfg</c>) still wins, and resetting window positions
    /// brings it back here. The client's Escape order has no entry for the command list; the entry goes ahead of
    /// the main windows and the whispers (its last two entries).
    /// </summary>
    private static void PlaceAndEscape(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta(PlacedMeta)) return;
        window.SetMeta(PlacedMeta, true);
        window.DockTo(() => OpenSpot(window, world));
        bool Open() => GodotObject.IsInstanceValid(window) && window.Visible;
        void Close() { if (GodotObject.IsInstanceValid(window)) window.Visible = false; }
        NativeEscape.CloseOnEscape(world, Open, Close);
    }

    /// <summary>Where the window opens now: <see cref="ClassicCommandListLayout.OpenSpot"/> for this screen and HUD.</summary>
    public static Vector2 OpenSpot(HudWindow window, WorldRoot world)
    {
        if (!GodotObject.IsInstanceValid(window)) return Vector2.Zero;
        if (!window.IsInsideTree()) return window.Position;
        var hud = GodotObject.IsInstanceValid(world) ? HudRects(world) : new List<Rect2>();
        return ClassicCommandListLayout.OpenSpot(window.GetViewportRect().Size, Footprint(window), hud);
    }

    public static Vector2 Footprint(Control window)
    {
        var min = window.GetCombinedMinimumSize();
        return new Vector2(Mathf.Max(window.Size.X, min.X), Mathf.Max(window.Size.Y, min.Y)) * window.Scale;
    }

    /// <summary>
    /// The HUD the window should not cover: the Classic HUD parts (party list, Info, taskbar, …), the client's Genie
    /// quick panel and the client's own docking obstacles (clock, premium chip, …).
    /// </summary>
    public static List<Rect2> HudRects(WorldRoot world)
    {
        var rects = new List<Rect2>();
        if (!world.IsInsideTree()) return rects;
        // The live HUD parts under one of the World's canvas layers: the parts a walk of those layers finds,
        // without walking the whole HUD on every open.
        foreach (var hud in NationHud.Live)
        {
            if (!GodotObject.IsInstanceValid(hud) || !UnderWorldLayer(hud, world)) continue;
            foreach (var part in hud.GetChildren().OfType<Control>())
                if (part.IsVisibleInTree()) rects.Add(part.GetGlobalRect());
        }
        if (Native.Get<Control>(world, "GenieSub.Sub") is { } genie && GodotObject.IsInstanceValid(genie) && genie.IsVisibleInTree()) rects.Add(genie.GetGlobalRect());
        try { if (Native.TryCall(world, "DockHudRects", out var dock) && dock is IEnumerable<Rect2> client) rects.AddRange(client); }
        catch (Exception) { }
        return rects.Where(r => r.HasArea()).ToList();
    }

    private static bool UnderWorldLayer(Node node, WorldRoot world)
    {
        for (Node? child = node, parent = node.GetParent(); parent != null; child = parent, parent = parent.GetParent())
            if (parent == world) return child is CanvasLayer && child != node;
        return false;
    }

    /// <summary>Runs a listed row the way a double click does (the client's <c>ItemActivated</c> handler).</summary>
    public static void Activate(WorldRoot world, int index)
    {
        if (Entries(world) is { } list) list.EmitSignal(ItemList.SignalName.ItemActivated, (long)index);
    }
}
