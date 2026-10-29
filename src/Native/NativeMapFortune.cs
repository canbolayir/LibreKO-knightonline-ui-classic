using Godot;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterMapFortune(PluginContext context)
    {
        foreach (string id in NativeMapFortune.WindowIds) NativeWindows.Prepare(id, NativeMapFortune.Prepare);
    }
}

/// <summary>
/// World Map and Fortune Teller: names the native list, card fan, reading stage and actions the Classic
/// skin places on the Zone List artwork. Map rows stay native; the client rebuilds them on every reply.
/// </summary>
public static class NativeMapFortune
{
    public static readonly string[] WindowIds = { "globalmap", "fortune" };
    public const string Prepared = "classic_map_fortune_controls";

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_map_fortune")) return;
        bool named = NativeWindows.Name(Native.Get<Label>(window, "titleLbl"), "mapfortune_title")
            & NativeWindows.Name(Native.Get<Button>(window, "closeButton"), "mapfortune_close");
        named &= window.Id == "globalmap" ? PrepareMap(window, world) : PrepareFortune(window, world);
        if (named) window.SetMeta(Prepared, 1);
    }

    /// <summary>Section title, the three column headings and the scrolled zone rows.</summary>
    private static bool PrepareMap(HudWindow window, WorldRoot world)
    {
        var rows = Native.Get<VBoxContainer>(world, "GlobalMap.list");
        if (rows?.GetParent() is not ScrollContainer scroll || !window.Body.IsAncestorOf(rows)) return false;
        var children = window.Body.GetChildren();
        if (children.Count < 3 || children[0] is not Label section || children[1] is not HBoxContainer header) return false;
        var heads = header.GetChildren().OfType<Label>().ToArray();
        if (heads.Length != 3) return false;
        section.Name = "map_section";
        for (int i = 0; i < heads.Length; i++) heads[i].Name = "map_head_" + i;
        scroll.Name = "map_scroll"; rows.Name = "map_rows";
        return true;
    }

    /// <summary>Card fan, greeting lines, the face with its turn and reveal animations, stars, reading and OK.</summary>
    private static bool PrepareFortune(HudWindow window, WorldRoot world)
    {
        var pick = Native.Get<Control>(world, "Fortune.Pick");
        var stage = Native.Get<Control>(world, "Fortune.Stage");
        var cards = Native.Get<List<TextureButton>>(world, "Fortune.cards");
        var lines = Native.Get<List<Label>>(world, "Fortune.lines");
        if (pick?.GetParent() is not Control canvas || stage == null || cards == null || lines == null || !window.Body.IsAncestorOf(canvas)) return false;
        var greeting = pick.GetChildren().OfType<Label>().ToArray();
        if (greeting.Length != 2 || cards.Count == 0 || lines.Count == 0) return false;
        canvas.Name = "fortune_canvas"; pick.Name = "fortune_pick"; stage.Name = "fortune_stage";
        greeting[0].Name = "fortune_greeting"; greeting[1].Name = "fortune_prompt";
        for (int i = 0; i < cards.Count; i++) cards[i].Name = "fortune_card_" + i;
        for (int i = 0; i < lines.Count; i++) lines[i].Name = "fortune_line_" + i;
        return NativeWindows.Name(Native.Get<Control>(world, "Fortune.face"), "fortune_face")
            & NativeWindows.Name(Native.Get<Control>(world, "Fortune.spin"), "fortune_spin")
            & NativeWindows.Name(Native.Get<Control>(world, "Fortune.Final"), "fortune_final")
            & NativeWindows.Name(Native.Get<Control>(world, "Fortune.stars"), "fortune_stars")
            & NativeWindows.Name(Native.Get<Label>(world, "Fortune.name"), "fortune_name")
            & NativeWindows.Name(Native.Get<Label>(world, "Fortune.reveal"), "fortune_reveal")
            & NativeWindows.Name(Native.Get<Button>(world, "Fortune.okButton"), "fortune_ok");
    }
}
