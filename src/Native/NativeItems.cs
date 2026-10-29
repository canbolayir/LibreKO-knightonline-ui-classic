using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterInventory(PluginContext context)
    {
        foreach (string id in NativeItems.WindowIds)
            NativeWindows.Prepare(id, (window, world) => NativeItems.Prepare(window, world));
    }
}

/// <summary>
/// Prepares the item windows the client added with the 2619 review (EXP Seal, Temporary Item List, Loot
/// Filter, Character Seal and the Cypher Ring sheet) for the Classic item skin: it names the native controls
/// the skin places and mirrors the state the skin shows (seal switch, current page) into metas. The windows
/// keep every native control, callback and server request.
/// </summary>
public static class NativeItems
{
    public static readonly string[] WindowIds = { "exp_seal", "timed_items", "loot_filter", "char_seal", "char_seal_sheet" };

    public const string PreparedMeta = "native_items";
    public const string PageMeta = "items_page";

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta(PreparedMeta)) return;
        window.SetMeta(PreparedMeta, true);
        switch (window.Id)
        {
            case "exp_seal": PrepareExpSeal(window, world); break;
            case "timed_items": PrepareTimedItems(world); break;
            case "loot_filter": PrepareLootFilter(window); break;
            case "char_seal": PrepareCharacterSeal(window, world); break;
            case "char_seal_sheet": PrepareSheet(window, world); break;
        }
    }

    private static void Name(WorldRoot world, string field, string name) => NativeWindows.Name(Native.Get<Node>(world, field), name);

    private static void NameAll(WorldRoot world, string field, string prefix)
    {
        if (Native.Get<Array>(world, field) is not { } nodes) return;
        for (int i = 0; i < nodes.Length; i++) NativeWindows.Name(nodes.GetValue(i) as Node, prefix + i);
    }

    private static void PrepareExpSeal(HudWindow window, WorldRoot world)
    {
        Name(world, "ExpSealWindow.JarRect", "exp_seal_jar");
        Name(world, "ExpSealWindow.percent", "exp_seal_percent");
        Name(world, "ExpSealWindow.amountLabel", "exp_seal_amount");
        Name(world, "ExpSealWindow.onButton", "exp_seal_on");
        Name(world, "ExpSealWindow.offButton", "exp_seal_off");
        Name(world, "ExpSealWindow.Pick", "exp_seal_pick");
        if (Native.Get<OptionButton>(world, "ExpSealWindow.Pick")?.GetParent() is Container store)
            NativeWindows.Name(store.GetChildren().OfType<Button>().FirstOrDefault(b => b is not OptionButton), "exp_seal_obtain");
        if (Native.Get<Button>(world, "ExpSealWindow.onButton")?.GetParent() is Container toggle)
            NativeWindows.Name(toggle.GetChildren().OfType<Label>().FirstOrDefault(), "exp_seal_caption");
        // The switch state the native buttons show by tinting; the Classic buttons show it as pressed.
        NativeWindows.Sync(window, () => window.SetMeta("exp_seal_state", (int)Game.Session.ExpSeal.ExpSealState));
    }

    private static void PrepareTimedItems(WorldRoot world)
    {
        NameAll(world, "TimedItemsWindow.tiRows", "timed_row_");
        NameAll(world, "TimedItemsWindow.tiSlots", "timed_cell_");
        NameAll(world, "TimedItemsWindow.tiNames", "timed_name_");
        NameAll(world, "TimedItemsWindow.tiPlaces", "timed_place_");
        NameAll(world, "TimedItemsWindow.tiTimes", "timed_time_");
        Name(world, "TimedItemsWindow.tiScroll", "timed_scroll");
        Name(world, "TimedItemsWindow.tiHideToday", "timed_hide");
        if (Native.Get<Control>(world, "TimedItemsWindow.tiHideToday")?.GetParent() is not Control canvas) return;
        var labels = canvas.GetChildren().OfType<Label>().Where(l => !l.Name.ToString().StartsWith("timed_")).ToArray();
        if (labels.Length >= 2)
        {
            labels[0].Name = "timed_notice";
            labels[^1].Name = "timed_hide_label";
        }
    }

    private static void PrepareLootFilter(HudWindow window)
    {
        var body = window.Body;
        var checks = body.GetChildren().OfType<CheckBox>().ToArray();
        if (checks.Length > 0) checks[0].Name = "loot_enabled";
        for (int i = 1; i < checks.Length; i++) checks[i].Name = "loot_group_" + (i - 1);
        if (body.GetChildren().OfType<GridContainer>().FirstOrDefault() is { } grades)
        {
            grades.Name = "loot_grades";
            int index = 0;
            foreach (var grade in grades.GetChildren().OfType<CheckBox>()) grade.Name = "loot_grade_" + index++;
        }
        if (body.GetChildren().OfType<HBoxContainer>().FirstOrDefault(r => r.GetChildren().OfType<SpinBox>().Any()) is { } price)
        {
            NativeWindows.Name(price.GetChildren().OfType<Label>().FirstOrDefault(), "loot_price_label");
            NativeWindows.Name(price.GetChildren().OfType<SpinBox>().First(), "loot_price");
        }
        NativeWindows.Name(body.GetChildren().OfType<VBoxContainer>().LastOrDefault(), "loot_rules");
    }

    private static void PrepareCharacterSeal(HudWindow window, WorldRoot world)
    {
        Name(world, "CharacterSealWindow.CsSealingPage", "cs_page_help");
        Name(world, "CharacterSealWindow.CsSelectPage", "cs_page_select");
        Name(world, "CharacterSealWindow.CsCodePage", "cs_page_code");
        Name(world, "CharacterSealWindow.CsSelectTitle", "cs_select_title");
        Name(world, "CharacterSealWindow.CsSelectPrompt", "cs_select_prompt");
        Name(world, "CharacterSealWindow.CsCodeStars", "cs_code_stars");
        Name(world, "CharacterSealWindow.csSelectOk", "cs_select_ok");
        Name(world, "CharacterSealWindow.csKeys", "cs_keys");
        NameAll(world, "CharacterSealWindow.csSlotButtons", "cs_slot_");
        if (Native.Get<VBoxContainer>(world, "CharacterSealWindow.CsSealingPage") is { } help)
        {
            var titles = help.GetChildren().OfType<Label>().ToArray();
            var panels = help.GetChildren().OfType<PanelContainer>().ToArray();
            for (int i = 0; i < titles.Length && i < 2; i++) titles[i].Name = "cs_help_title_" + i;
            for (int i = 0; i < panels.Length && i < 2; i++)
                NativeWindows.Name(panels[i].GetChildren().OfType<Label>().FirstOrDefault(), "cs_help_text_" + i);
            NameButtons(help, "cs_seal", "cs_unseal");
        }
        if (Native.Get<VBoxContainer>(world, "CharacterSealWindow.CsSelectPage") is { } select)
            NativeWindows.Name(select.GetChildren().OfType<HBoxContainer>().LastOrDefault()?.GetChildren().OfType<Button>().LastOrDefault(), "cs_select_cancel");
        if (Native.Get<VBoxContainer>(world, "CharacterSealWindow.CsCodePage") is { } code)
        {
            NativeWindows.Name(code.GetChildren().OfType<Label>().FirstOrDefault(), "cs_code_title");
            var rows = code.GetChildren().OfType<HBoxContainer>().ToArray();
            if (rows.Length >= 2)
            {
                NameButtons(rows[0], "cs_shuffle", "cs_delete");
                NameButtons(rows[1], "cs_code_ok", "cs_code_cancel");
            }
        }
        NativeWindows.Sync(window, () =>
        {
            window.SetMeta(PageMeta, CharacterSealPage(world));
            window.SetMeta("cs_picked", Native.Get<int>(world, "CharacterSealWindow.Picked"));
        });
    }

    /// <summary>The visible Character Seal page: help, select or code.</summary>
    public static string CharacterSealPage(WorldRoot world) =>
        Native.Get<Control>(world, "CharacterSealWindow.CsSelectPage") is { Visible: true } ? "select"
        : Native.Get<Control>(world, "CharacterSealWindow.CsCodePage") is { Visible: true } ? "code"
        : "help";

    private static void NameButtons(Node row, string first, string second)
    {
        var buttons = Native.Descendants(row).OfType<Button>().ToArray();
        if (buttons.Length < 2) return;
        buttons[0].Name = first;
        buttons[1].Name = second;
    }

    private static void PrepareSheet(HudWindow window, WorldRoot world)
    {
        Name(world, "CharacterSealWindow.csSheetName", "sheet_name");
        Name(world, "CharacterSealWindow.csSheetAchievements", "sheet_achievements");
        NameAll(world, "CharacterSealWindow.csSheetCells", "sheet_cell_");
        if (Native.Get<Control>(world, "CharacterSealWindow.csSheetLevel")?.GetParent()?.GetParent() is VBoxContainer identity)
        {
            identity.Name = "sheet_identity";
            NativeWindows.Name(identity.GetParent()?.GetParent()?.GetChildren().OfType<Button>().FirstOrDefault(), "sheet_achievements_button");
        }
        if (Native.Get<Array>(world, "CharacterSealWindow.csSheetStats")?.GetValue(0) is Control stat)
            NativeWindows.Name(stat.GetParent(), "sheet_stats");
        if (Native.Get<Control>(world, "CharacterSealWindow.csSheetGold")?.GetParent()?.GetParent() is VBoxContainer wealth) wealth.Name = "sheet_wealth";
        if (Native.Get<Array>(world, "CharacterSealWindow.csSheetCells") is { Length: > 0 } cells)
        {
            NativeWindows.Name((cells.GetValue(0) as Node)?.GetParent(), "sheet_equip");
            NativeWindows.Name((cells.GetValue(cells.Length - 1) as Node)?.GetParent(), "sheet_bag");
        }
        NativeWindows.Sync(window, () => window.SetMeta(PageMeta,
            Native.Get<Control>(world, "CharacterSealWindow.csSheetAchievements") is { Visible: true } ? "achievements" : ""));
    }
}
