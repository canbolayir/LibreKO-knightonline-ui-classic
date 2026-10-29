using System.Collections;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterProgression(PluginContext context)
    {
        foreach (var id in NativeProgression.WindowIds) NativeWindows.Prepare(id, NativeProgression.Prepare);
    }
}

/// <summary>
/// Prepares the native achievements, attendance, rankings and genie windows for the Classic skin: names
/// the native controls the skin places and mirrors the native selection, claim and request state into
/// metas. Every control keeps its own callback; the windows are only recomposed.
/// </summary>
public static class NativeProgression
{
    public static readonly string[] WindowIds = { "achievements", "attendance", "rank", "genie" };
    public const string Gate = "classic_progression_controls";

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        // The Genie skill picker offers only skills the skill window lets go (no passive skills).
        if (window.Id == "genie" && Native.Get<PopupMenu>(world, "Genie.SkillPicker") is { } skillPicker)
            NativeSkills.FilterGenieSkillPicker(skillPicker, world);
        if (window.HasMeta(Gate)) return;
        bool ready = window.Id switch
        {
            "achievements" => PrepareAchievements(window, world),
            "attendance" => PrepareAttendance(window, world),
            "rank" => PrepareRank(window, world),
            "genie" => PrepareGenie(window, world),
            _ => false,
        };
        if (ready) window.SetMeta(Gate, 1);
    }

    private static bool Named(Node? node, string name) => NativeWindows.Name(node, name);

    private static bool PrepareAchievements(HudWindow window, WorldRoot world)
    {
        var tabs = Native.Get<IDictionary>(world, "Achievement.achTabButtons");
        var list = Native.Get<VBoxContainer>(world, "Achievement.achList");
        if (tabs == null || list == null) return false;
        if (!Named(Native.Get<HBoxContainer>(world, "Achievement.achTabs"), "ach_tabs")
            || !Named(Native.Get<Button>(world, "Achievement.achSummaryTabButton"), "ach_tab_summary")
            || !Named(Native.Get<Label>(world, "Achievement.achSummary"), "ach_summary")
            || !Named(Native.Get<CheckButton>(world, "Achievement.AchHideClaimed"), "ach_hide_claimed")
            || !Named(Native.Get<ScrollContainer>(world, "Achievement.achScroll"), "ach_scroll")
            || !Named(Native.Get<VBoxContainer>(world, "Achievement.achSummaryPage"), "ach_summary_page")) return false;
        list.Name = "ach_list";
        foreach (DictionaryEntry tab in tabs) Named(tab.Value as Node, "ach_tab_" + Convert.ToInt32(tab.Key));
        // The worn-title banner, the search and status filters and the category column beside the list.
        var banner = Native.Get<Label>(world, "Achievement.achTitleBanner");
        if (Named(banner, "ach_title") && banner!.GetParent() is HBoxContainer bannerRow)
        {
            bannerRow.Name = "ach_title_row";
            Named(bannerRow.GetParent(), "ach_title_banner");
            Named(bannerRow.GetChildren().OfType<Button>().FirstOrDefault(), "ach_change_title");
        }
        var search = Native.Get<LineEdit>(world, "Achievement.achSearch");
        if (Named(search, "ach_search")) Named(search!.GetParent(), "ach_filters");
        Named(Native.Get<OptionButton>(world, "Achievement.AchStatus"), "ach_status");
        var categories = Native.Get<VBoxContainer>(world, "Achievement.achCategoryBox");
        if (Named(categories, "ach_categories")) Named(categories!.GetParent(), "ach_category_panel");
        Named(Native.Get<HBoxContainer>(world, "Achievement.achListRow"), "ach_list_row");
        NativeWindows.Sync(window, () => SyncAchievements(window, world, tabs, list));
        return true;
    }

    private static void SyncAchievements(HudWindow window, WorldRoot world, IDictionary tabs, VBoxContainer list)
    {
        bool summary = Native.Get<bool>(world, "Achievement.ViewingSummary");
        int selected = summary ? -1 : Convert.ToInt32(Native.Get<object>(world, "Achievement.Tab") ?? 0);
        window.SetMeta(HotNames.AchSelected, selected);
        var waiting = Native.Get<IDictionary>(world, "Achievement.achClaimablePerTab");
        foreach (DictionaryEntry tab in tabs)
            if (tab.Value is Button button)
            {
                int key = Convert.ToInt32(tab.Key);
                button.SetMeta(HotNames.ProgressionSelected, key == selected);
                button.SetMeta(HotNames.ProgressionWaiting, waiting != null && waiting.Contains(tab.Key) ? Convert.ToInt32(waiting[tab.Key]) : 0);
            }
        Native.Get<Button>(world, "Achievement.achSummaryTabButton")?.SetMeta(HotNames.ProgressionSelected, summary);
        // Rows follow the native visible order; mirror each entry's claim state onto its row.
        if (Native.Get<IList>(world, "Achievement.achVisible") is not { } visible) return;
        int index = 0;
        foreach (var row in list.GetChildren().OfType<PanelContainer>())
        {
            if (row.IsQueuedForDeletion()) continue;
            if (index >= visible.Count) break;
            var entry = (AchievementEntry)visible[index++]!;
            row.SetMeta(HotNames.AchRow, entry.Id);
            row.SetMeta(HotNames.AchClaimed, entry.Claimed);
            row.SetMeta(HotNames.AchClaimable, entry.Claimable);
        }
    }

    private static bool PrepareAttendance(HudWindow window, WorldRoot world)
    {
        var daily = Native.Get<GridContainer>(world, "Attendance.Grid");
        var bonus = Native.Get<GridContainer>(world, "Attendance.bonusRow");
        var count = Native.Get<Label>(world, "Attendance.countLabel");
        if (daily == null || bonus == null || count == null || count.GetParent() is not HBoxContainer header) return false;
        if (!Named(Native.Get<Label>(world, "Attendance.notice"), "attendance_notice")) return false;
        daily.Name = "attendance_daily";
        bonus.Name = "attendance_bonus";
        count.Name = "attendance_count";
        header.Name = "attendance_header";
        Named(header.GetChildren().OfType<Label>().FirstOrDefault(l => l != count), "attendance_daily_title");
        var body = window.Body;
        var children = body.GetChildren().OfType<Control>().ToList();
        int bonusSection = children.FindIndex(c => c.IsAncestorOf(bonus));
        if (bonusSection < 1) return false;
        Named(children[bonusSection - 1] as Label, "attendance_bonus_title");
        if (bonusSection + 1 < children.Count && children[bonusSection + 1] is HBoxContainer legend)
        {
            legend.Name = "attendance_legend";
            int i = 0;
            foreach (var item in legend.GetChildren().OfType<HBoxContainer>()) item.Name = "attendance_legend_" + i++;
        }
        NativeWindows.Sync(window, () => SyncAttendance(world, daily, bonus));
        return true;
    }

    private static void SyncAttendance(WorldRoot world, GridContainer daily, GridContainer bonus)
    {
        var slots = Native.Get<int[]>(world, "Attendance.slots");
        var states = Native.Get<byte[]>(world, "Attendance.states");
        if (slots == null || states == null) return;
        void Mirror(GridContainer grid, int offset)
        {
            int index = offset;
            foreach (var cell in grid.GetChildren().OfType<VBoxContainer>())
            {
                if (cell.IsQueuedForDeletion()) continue;
                if (index >= states.Length) break;
                byte state = states[index];
                cell.SetMeta(HotNames.AttendanceSlot, slots[index++]);
                cell.SetMeta(HotNames.AttendanceState, (int)state);
                cell.SetMeta(HotNames.AttendanceClaimable, state is >= AttendanceSlotState.Claimable and <= AttendanceSlotState.ClaimableExtra);
                cell.SetMeta(HotNames.AttendanceClaimed, state is AttendanceSlotState.Claimed or AttendanceSlotState.Expired);
            }
        }
        Mirror(daily, 0);
        Mirror(bonus, AttendanceWire.DailySlots);
    }

    /// <summary>Ladder cells of a <c>LadderRow</c>, in column order.</summary>
    public static readonly string[] LadderCells = { "Rank", "Mark", "Name", "Clan", "Points", "Bonus" };

    private static bool PrepareRank(HudWindow window, WorldRoot world)
    {
        // The PK ladder: two fixed ten-row ladders (Karus, El Morad), each with the player's own row under a rule.
        var karus = Native.Get<Array>(world, "Rank.ladderKarus");
        var elmorad = Native.Get<Array>(world, "Rank.ladderElMorad");
        var ownKarus = Native.Get<object>(world, "Rank.ladderOwnKarus");
        var ownElMorad = Native.Get<object>(world, "Rank.ladderOwnElMorad");
        if (karus == null || elmorad == null || ownKarus == null || ownElMorad == null) return false;
        foreach (var (rows, own, side) in new[] { (karus, ownKarus, "karus"), (elmorad, ownElMorad, "elmorad") })
        {
            for (int i = 0; i < rows.Length; i++)
                if (rows.GetValue(i) is not { } row || !NameLadderRow(row, $"rank_{side}_row_{i}")) return false;
            if (!NameLadderRow(own, $"rank_{side}_own")) return false;
            if (Native.Get<Label>(own, "Rank")?.GetParent()?.GetParent() is not VBoxContainer column) return false;
            column.Name = "rank_side_" + side;
            var children = column.GetChildren().OfType<Control>().ToArray();
            Named(children.FirstOrDefault() as Label, "rank_caption_" + side);
            if (children.Length > 1 && children[1] is HBoxContainer header)
            {
                header.Name = "rank_header_" + side;
                int c = 0;
                foreach (var label in header.GetChildren().OfType<Label>()) label.Name = $"rank_{side}_heading_{c++}";
            }
            Named(children.OfType<HSeparator>().FirstOrDefault(), "rank_rule_" + side);
        }
        Named(window.Body.GetChildren().OfType<Label>().LastOrDefault(), "rank_notice");
        return true;
    }

    private static bool NameLadderRow(object row, string name)
    {
        if (Native.Get<Label>(row, "Rank")?.GetParent() is not HBoxContainer line) return false;
        line.Name = name;
        foreach (var cell in LadderCells)
            if (!Named(Native.Get<Control>(row, cell), name + "_" + cell.ToLowerInvariant())) return false;
        Named(Native.Get<Control>(row, "Points")?.GetParent(), name + "_score");
        return true;
    }

    /// <summary>
    /// The Genie page: 28 skill cells (Attack 8, Recovery 8, Support 12 with a party check under each), the mode
    /// toggles per column, the potion, familiar and monster options, the Misc settings and the "Set Others" rows.
    /// The mode toggles are named by their <see cref="GenieMode"/> (<c>genie_mode_attack</c> …) and the Set Others
    /// rows by their native order; every control keeps its native callback.
    /// </summary>
    private static bool PrepareGenie(HudWindow window, WorldRoot world)
    {
        var slots = Native.Get<Button[]>(world, "Genie.slots");
        var modes = Native.Get<IDictionary>(world, "Genie.modes");
        var party = Native.Get<CheckBox[]>(world, "Genie.genieParty");
        var start = Native.Get<Button>(world, "Genie.start");
        var monsters = Native.Get<ItemList>(world, "Genie.monsterList");
        var scrolls = Native.Get<Button[]>(world, "GenieScrolls.scrollButtons");
        if (slots == null || modes == null || party == null || start == null || monsters == null || scrolls == null
            || slots.Any(s => s == null) || party.Any(p => p == null)) return false;
        if (slots[0].GetParent()?.GetParent() is not VBoxContainer firstColumn || firstColumn.GetParent() is not HBoxContainer columns
            || columns.GetParent() is not VBoxContainer main || main.GetParent() is not TabContainer tabs) return false;
        tabs.Name = "genie_tabs"; main.Name = "genie_main";
        // The Misc tab is a scroll page around its option column.
        var page = tabs.GetChildren().OfType<Control>().FirstOrDefault(c => c != main);
        if (page is ScrollContainer miscScroll) { miscScroll.Name = "genie_misc_scroll"; page = miscScroll.GetChildren().OfType<VBoxContainer>().FirstOrDefault(); }
        if (page is not VBoxContainer misc) return false;
        misc.Name = "genie_misc";
        for (int i = 0; i < slots.Length; i++) slots[i].Name = "genie_slot_" + i;
        for (int i = 0; i < party.Length; i++) party[i].Name = "genie_party_" + i;
        foreach (DictionaryEntry mode in modes) Named(mode.Value as Node, GenieModeName(mode.Key));
        int column = 0;
        foreach (var box in columns.GetChildren().OfType<VBoxContainer>())
        {
            var labels = box.GetChildren().OfType<Label>().ToArray();
            Named(labels.FirstOrDefault(), "genie_skill_title_" + column);
            // The support column's note under its cells (the party check tooltip says the same).
            if (labels.Length > 1) Named(labels[^1], "genie_skill_hint_" + column);
            Named(box.GetChildren().OfType<GridContainer>().FirstOrDefault(), "genie_skill_grid_" + column++);
        }
        NameSpin(Native.Get<SpinBox>(world, "Genie.recoverySpin"), "genie_recovery");
        // Potion options: title, HP check, HP potion, HP threshold, MP …, Familiar potion check, potion, threshold.
        Named(Native.Get<Button>(world, "GeniePotions.hpSelect"), "genie_hp_item");
        Named(Native.Get<Button>(world, "GeniePotions.mpSelect"), "genie_mp_item");
        Named(Native.Get<Button>(world, "GeniePotions.petSelect"), "genie_pet_item");
        Named(Native.Get<CheckButton>(world, "Genie.petPotion"), "genie_pet_potion");
        NameSpin(Native.Get<SpinBox>(world, "Genie.hpThreshold"), "genie_hp");
        NameSpin(Native.Get<SpinBox>(world, "Genie.mpThreshold"), "genie_mp");
        NameSpin(Native.Get<SpinBox>(world, "Genie.petPercent"), "genie_pet");
        if (Native.Get<SpinBox>(world, "Genie.hpThreshold")?.GetParent()?.GetParent() is VBoxContainer potions)
            Named(potions.GetChildren().OfType<Label>().FirstOrDefault(), "genie_potion_title");
        monsters.Name = "genie_monster_list";
        if (monsters.GetParent() is VBoxContainer monsterBox)
        {
            Named(monsterBox.GetChildren().OfType<Label>().FirstOrDefault(), "genie_monster_title");
            var actions = monsterBox.GetChildren().OfType<HBoxContainer>().FirstOrDefault()?.GetChildren().OfType<Button>().ToArray() ?? Array.Empty<Button>();
            if (actions.Length == 2) { actions[0].Name = "genie_monster_add"; actions[1].Name = "genie_monster_remove"; }
        }
        var run = Native.Get<Label>(world, "Genie.RunLabel");
        Named(run, "genie_run");
        if (run != null && run.GetIndex() > 0) Named(main.GetChild(run.GetIndex() - 1) as Label, "genie_hint");
        start.Name = "genie_start";
        var footer = start.GetParent().GetChildren().OfType<Button>().ToArray();
        if (footer.Length == 3) { footer[1].Name = "genie_save"; footer[2].Name = "genie_spirit"; }
        NameSpin(Native.Get<SpinBox>(world, "Genie.range"), "genie_range");
        Named(Native.Get<CheckButton>(world, "Genie.autoParty"), "genie_auto_party");
        var code = Native.Get<LineEdit>(world, "Genie.ptCode");
        Named(code, "genie_ptcode");
        Named(Native.Get<CheckButton>(world, "GenieScrolls.autoScrolls"), "genie_auto_scrolls");
        for (int i = 0; i < scrolls.Length; i++) Named(scrolls[i], "genie_scroll_" + i);
        Named(Native.Get<Label>(world, "GenieScrolls.scrollStatus"), "genie_scroll_status");
        NameSpin(Native.Get<SpinBox>(world, "GenieHammer.hammerThreshold"), "genie_hammer_threshold");
        Named(Native.Get<Label>(world, "GenieHammer.hammerStatus"), "genie_hammer_status");
        Named(Native.Get<Label>(world, "Genie.tipLabel"), "genie_tip");
        Named(Native.Get<Label>(world, "Genie.status"), "genie_daily_status");
        Named(Native.Get<Button>(world, "Genie.claimBtn"), "genie_claim");
        // Misc is built in a fixed order; its section titles and hints precede or follow the named fields.
        var items = misc.GetChildren().OfType<Control>().ToList();
        void Before(Node? field, string name) { int at = field is Control c ? items.IndexOf(c) : -1; if (at > 0) Named(items[at - 1] as Label, name); }
        Before(Native.Get<SpinBox>(world, "Genie.range")?.GetParent(), "genie_settings_title");
        Before(code, "genie_ptcode_title");
        Before(Native.Get<CheckButton>(world, "GenieScrolls.autoScrolls"), "genie_scrolls_title");
        Before(Native.Get<SpinBox>(world, "GenieHammer.hammerThreshold")?.GetParent(), "genie_hammer_title");
        Before(Native.Get<Label>(world, "Genie.tipLabel"), "genie_daily_title");
        int hints = code is Control edit ? items.IndexOf(edit) : -1;
        string[] hintNames = { "genie_ptcode_hint", "genie_leader_hint" };
        for (int i = 0; hints >= 0 && i < hintNames.Length && hints + 1 + i < items.Count; i++) Named(items[hints + 1 + i] as Label, hintNames[i]);
        if (scrolls[0].GetParent() is HBoxContainer scrollRow)
        {
            scrollRow.Name = "genie_scroll_row";
            int at = items.IndexOf(scrollRow);
            if (at >= 0 && at + 1 < items.Count) Named(items[at + 1] as Label, "genie_scroll_hint");
        }
        Named(misc.GetChildren().OfType<HSeparator>().FirstOrDefault(), "genie_rule");
        // Movement and curse options under the range, and the return-to-town and automation rules ("Set Others").
        Named(Native.Get<CheckButton>(world, "Genie.returnHome"), "genie_return_home");
        Named(Native.Get<CheckButton>(world, "Genie.huntInPlace"), "genie_hunt_in_place");
        Named(Native.Get<CheckButton>(world, "Genie.hideRange"), "genie_hide_range");
        Named(Native.Get<CheckButton>(world, "Genie.cureCurse"), "genie_cure_curse");
        if (Native.Get<IDictionary>(world, "GenieTown.other") is { } others)
        {
            int row = 0;
            foreach (DictionaryEntry other in others)
                if (other.Value is CheckButton toggle && toggle.GetParent() is HBoxContainer line)
                {
                    toggle.Name = "genie_other_" + row + "_toggle";
                    line.Name = "genie_other_" + row;
                    line.SetMeta("genie_other", Convert.ToInt32(other.Key));
                    Named(line.GetChildren().OfType<SpinBox>().FirstOrDefault(), "genie_other_" + row + "_count");
                    if (row++ == 0 && items.IndexOf(line) is var at and > 0) Named(items[at - 1] as Label, "genie_others_title");
                }
        }
        Named(Native.Get<PopupMenu>(world, "Genie.SkillPicker"), "genie_skill_picker");
        Named(Native.Get<PopupMenu>(world, "GeniePotions.PotionPicker"), "genie_potion_picker");
        Named(Native.Get<PopupMenu>(world, "GenieScrolls.scrollPicker"), "genie_scroll_picker");
        // The pickers live on the genie layer; hand them to the skin through the window.
        int picker = 0;
        foreach (var menu in GeniePickers(world)) window.SetMeta("genie_picker_" + picker++, menu);
        NativeWindows.Sync(window, () =>
        {
            window.SetMeta("genie_pending", Native.Get<bool>(world, "Genie.RequestPending"));
            window.SetMeta("genie_running", Game.Session.Genie.GenieRunning);
        });
        return true;
    }

    /// <summary>The Classic name of a mode toggle: <c>genie_mode_</c> and the <see cref="GenieMode"/> name in lower case.</summary>
    public static string GenieModeName(object mode) => "genie_mode_" + (mode is GenieMode flag ? flag.ToString() : mode.ToString() ?? "").ToLowerInvariant();

    private static void NameSpin(SpinBox? spin, string name)
    {
        if (spin == null) return;
        spin.Name = name;
        if (spin.GetParent() is HBoxContainer row)
        {
            row.Name = name + "_row";
            Named(row.GetChildren().OfType<Label>().FirstOrDefault(), name + "_label");
        }
    }

    /// <summary>The native genie popups, which live on the genie layer rather than in the window.</summary>
    public static IEnumerable<PopupMenu> GeniePickers(WorldRoot world)
    {
        foreach (var field in new[] { "Genie.SkillPicker", "GeniePotions.PotionPicker", "GenieScrolls.scrollPicker" })
            if (Native.Get<PopupMenu>(world, field) is { } menu) yield return menu;
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName AchClaimable = "ach_claimable";
    internal static readonly StringName AchClaimed = "ach_claimed";
    internal static readonly StringName AchRow = "ach_row";
    internal static readonly StringName AchSelected = "ach_selected";
    internal static readonly StringName AttendanceClaimable = "attendance_claimable";
    internal static readonly StringName AttendanceClaimed = "attendance_claimed";
    internal static readonly StringName AttendanceSlot = "attendance_slot";
    internal static readonly StringName AttendanceState = "attendance_state";
    internal static readonly StringName ProgressionSelected = "progression_selected";
    internal static readonly StringName ProgressionWaiting = "progression_waiting";
}
