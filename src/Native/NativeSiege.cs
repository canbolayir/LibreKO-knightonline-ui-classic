using Godot;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterSiege(PluginContext context)
    {
        foreach (string id in NativeSiege.WindowIds) NativeWindows.Prepare(id, NativeSiege.Prepare);
    }
}

/// <summary>
/// Castle siege windows: names the native speech lines, menu options, list tables, notes and actions the
/// Classic siege skin places on the original warfare artwork. Rows stay native; the client rebuilds them
/// on every server reply.
/// </summary>
public static class NativeSiege
{
    public static readonly string[] WindowIds = { "siegeguard", "siegeoffice", "siegetaxlist", "siegeschedule", "siegechallengers", "siegedefenders" };
    public const string Prepared = "classic_siege_controls";

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_siege")) return;
        bool named = window.Id switch
        {
            "siegeguard" or "siegeoffice" or "siegetaxlist" => PrepareSpeech(window),
            "siegeschedule" => PrepareTable(window, world, "Siege.scheduleRows", "siege_ok", "siege_refresh"),
            "siegechallengers" => PrepareTable(window, world, "Siege.challengerRows") && PrepareChallengers(window, world),
            "siegedefenders" => PrepareTable(window, world, "Siege.defenderRows", "siege_challengers", "siege_refresh"),
            _ => false,
        };
        // List windows show the native title and close button on the original artwork.
        if (window.Id is not ("siegeguard" or "siegeoffice" or "siegetaxlist"))
            named &= NativeWindows.Name(Native.Get<Label>(window, "titleLbl"), "siege_title")
                & NativeWindows.Name(Native.Get<Button>(window, "closeButton"), "siege_close");
        if (!named) return;
        window.SetMeta(Prepared, 1);
    }

    /// <summary>NPC speech: two speech lines followed by the native menu options.</summary>
    private static bool PrepareSpeech(HudWindow window)
    {
        var children = window.Body.GetChildren();
        if (children.Count < 2 || children[0] is not PanelContainer speech || children[1] is not VBoxContainer options) return false;
        var lines = Native.Descendants(speech).OfType<Label>().ToArray();
        var buttons = options.GetChildren().OfType<Button>().ToArray();
        if (lines.Length != 2 || buttons.Length != 4) return false;
        lines[0].Name = "siege_greeting"; lines[1].Name = "siege_question";
        for (int i = 0; i < buttons.Length; i++) buttons[i].Name = "siege_option_" + i;
        return true;
    }

    /// <summary>List windows: column headings, the scrolled native rows and the footer actions.</summary>
    private static bool PrepareTable(HudWindow window, WorldRoot world, string rowsField, params string[] actions)
    {
        var rows = Native.Get<VBoxContainer>(world, rowsField);
        if (rows?.GetParent() is not ScrollContainer scroll || !window.Body.IsAncestorOf(rows)) return false;
        var children = window.Body.GetChildren();
        var heads = children.Count > 0 ? Native.Descendants(children[0]).OfType<Label>().ToArray() : Array.Empty<Label>();
        if (heads.Length != 3) return false;
        for (int i = 0; i < heads.Length; i++) heads[i].Name = "siege_head_" + i;
        rows.Name = "siege_rows"; scroll.Name = "siege_scroll";
        if (actions.Length == 0) return true;
        var footer = children.OfType<HBoxContainer>().LastOrDefault();
        var buttons = footer?.GetChildren().OfType<Button>().ToArray() ?? Array.Empty<Button>();
        if (buttons.Length != actions.Length) return false;
        for (int i = 0; i < buttons.Length; i++) buttons[i].Name = actions[i];
        return true;
    }

    private static bool PrepareChallengers(HudWindow window, WorldRoot world)
    {
        var apply = Native.Get<Button>(world, "Siege.apply");
        var cancel = Native.Get<Button>(world, "Siege.cancel");
        var footer = apply?.GetParent();
        var defenders = footer?.GetChildren().OfType<Button>().FirstOrDefault(b => b != apply && b != cancel);
        bool named = NativeWindows.Name(apply, "siege_apply") & NativeWindows.Name(cancel, "siege_apply_cancel")
            & NativeWindows.Name(defenders, "siege_defenders")
            & NativeWindows.Name(Native.Get<Label>(world, "Siege.signedUp"), "siege_signed_up")
            & NativeWindows.Name(Native.Get<Label>(world, "Siege.chosen"), "siege_chosen")
            & NativeWindows.Name(Native.Get<Label>(world, "Siege.fee"), "siege_fee")
            & NativeWindows.Name(Native.Get<Label>(world, "Siege.period"), "siege_period");
        return named && window.Body.IsAncestorOf(apply);
    }
}
