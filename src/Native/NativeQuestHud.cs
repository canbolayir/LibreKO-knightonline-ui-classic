using Godot;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>HUD parts of the quest and premium systems that have no window id or plugin hook.</summary>
public enum NativeQuestHudPart
{
    /// <summary>The active quest tracker (<c>_trackerPanel</c>; rows rebuilt on every quest change).</summary>
    Tracker,
    /// <summary>The account premium chip beside the top-right icons.</summary>
    PremiumChip,
    /// <summary>The clan premium chip.</summary>
    ClanPremiumChip,
    /// <summary>The clan online-bonus chip under the premium chip (3277df5: "Clan" and the O/X marks with a tooltip).</summary>
    ClanBonusChip,
}

/// <summary>
/// Prepares the quest tracker and the premium chips for the Classic skins once the world is attached: names the
/// client's controls and hands each part to its skins. Every native control, callback, timer and refresh stays;
/// the tracker keeps rebuilding its rows and the chips keep their status, time and benefits tooltip.
/// </summary>
public static class NativeQuestHud
{
    public const string TrackerName = "quest_tracker", TrackerRows = "quest_tracker_rows", TrackerHeader = "quest_tracker_header";
    public const string TrackerFold = "quest_tracker_fold", TrackerCount = "quest_tracker_count";
    public const string PremiumName = "premium_chip", PremiumLabel = "premium_label", PremiumDot = "premium_dot";
    public const string ClanPremiumName = "clan_premium_chip", ClanPremiumLabel = "clan_premium_label", ClanPremiumDot = "clan_premium_dot";
    public const string ClanBonusName = "clan_bonus_chip", ClanBonusMarks = "clan_bonus_marks";
    private const string Prepared = "classic_quest_hud";
    private static readonly Dictionary<NativeQuestHudPart, List<Action<Control>>> _skins = new();

    public static void Extend(NativeQuestHudPart part, Action<Control> skin)
    {
        if (!_skins.TryGetValue(part, out var list)) _skins[part] = list = new();
        list.Add(skin);
    }

    internal static void Register(PluginContext context)
    {
        var game = context.Game;
        Action attached = () => { if (NativeHudChat.World is { } world) Prepare(world); };
        NativeHooks.Listen("Game.BecameAvailable", () => game.BecameAvailable += attached, () => game.BecameAvailable -= attached);
    }

    internal static void Reset() => _skins.Clear();

    /// <summary>Names and skins the tracker and chips of a world that has built them; safe to call again.</summary>
    public static void Prepare(WorldRoot world)
    {
        if (!GodotObject.IsInstanceValid(world)) return;
        PrepareTracker(world);
        PreparePremium(world, "Premium.Chip", "Premium.label", "Premium.dot", PremiumName, PremiumLabel, PremiumDot, NativeQuestHudPart.PremiumChip);
        PreparePremium(world, "ClanPremium.chip", "ClanPremium.label", "ClanPremium.dot", ClanPremiumName, ClanPremiumLabel, ClanPremiumDot, NativeQuestHudPart.ClanPremiumChip);
        // The clan bonus chip has no status dot; its marks label is the second child of its row.
        PreparePremium(world, "Premium.ClanBonusChip", "Premium.clanBonusMarks", null, ClanBonusName, ClanBonusMarks, null, NativeQuestHudPart.ClanBonusChip);
    }

    /// <summary>The tracker panel with its row column, header, count and fold button named for the skin.</summary>
    public static void PrepareTracker(WorldRoot world)
    {
        if (Native.Get<PanelContainer>(world, "QuestTracker.TrackerPanel") is not { } panel || !GodotObject.IsInstanceValid(panel) || panel.HasMeta(Prepared)) return;
        if (Native.Get<VBoxContainer>(world, "QuestTracker.trackerBox") is not { } rows) return;
        panel.SetMeta(Prepared, true);
        panel.Name = TrackerName;
        rows.Name = TrackerRows;
        // The header exists on desktop only; the touch tracker has plain rows.
        if (Native.Get<PanelContainer>(world, "QuestTracker.trackerHeader") is { } header) header.Name = TrackerHeader;
        if (Native.Get<Button>(world, "QuestTracker.trackerFold") is { } fold) fold.Name = TrackerFold;
        if (Native.Get<Label>(world, "QuestTracker.trackerCount") is { } count) count.Name = TrackerCount;
        Apply(NativeQuestHudPart.Tracker, panel);
    }

    private static void PreparePremium(WorldRoot world, string chipField, string labelField, string? dotField,
        string chipName, string labelName, string? dotName, NativeQuestHudPart part)
    {
        if (Native.Get<PanelContainer>(world, chipField) is not { } chip || !GodotObject.IsInstanceValid(chip) || chip.HasMeta(Prepared)) return;
        if (Native.Get<Label>(world, labelField) is not { } label) return;
        chip.SetMeta(Prepared, true);
        chip.Name = chipName;
        label.Name = labelName;
        if (dotField != null && dotName != null && Native.Get<Panel>(world, dotField) is { } dot) dot.Name = dotName;
        Apply(part, chip);
    }

    private static void Apply(NativeQuestHudPart part, Control node)
    {
        if (!_skins.TryGetValue(part, out var skins)) return;
        foreach (var skin in skins) skin(node);
    }
}

public static partial class NativeSetup
{
    static partial void RegisterQuestHud(PluginContext context) => NativeQuestHud.Register(context);
}
