using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The client's skill rules where the Classic windows offer skills: which skills and items the skill bar takes
/// (<see cref="HotbarAssign"/>, exactly as the client's own skill window applies it), how far a skill is from being
/// learned (<see cref="SkillLearn"/>), and the original skill-detail wording.
/// </summary>
public static class NativeSkills
{
    private const int EmptyHand = 0;
    private const int TextAllWeapons = 4303, TextNoMp = 4320, TextItemConsumed = 4321, TextNoItemConsumed = 4322, TextMpConsumed = 4323,
        TextRequiredLevel = 4324, TextRequiredPoints = 4325, TextBasicItem = 4327, TextNoBasicItem = 4328, TextRequiredItem = 4329,
        TextNoRequiredItem = 4330;
    private const string RequiredItemPrefix = "Required Item : ";
    // The El Morad twins of the client's HotbarAssign.Shouting/Assault (130013/130014). Older cores exempt only the
    // Karus pair; the original skill bar takes both nations' war shouts.
    private const int ElmoradShouting = 230013, ElmoradAssault = 230014;

    /// <summary>
    /// Right-click rule of the client's skill window: passive skills stay off the skill bar, except the war shouts of
    /// both nations.
    /// </summary>
    public static bool CanAddToHotbar(int skillId) =>
        SkillData.Get(skillId) is { } skill && (HotbarAssign.CanAdd(skill.Id, skill.Type1) || IsWarShout(skill.Id));

    /// <summary>The war shouts that go on the skill bar although they are passive: the client's Karus pair and its El Morad twins.</summary>
    public static bool IsWarShout(int skillId) =>
        skillId is HotbarAssign.Shouting or HotbarAssign.Assault or ElmoradShouting or ElmoradAssault;

    /// <summary>Drag rule of the client's skill window, for the skill bar and the Genie skill slots.</summary>
    public static bool CanDragToHotbar(int skillId) =>
        SkillData.Get(skillId) is { } skill && HotbarAssign.CanDrag(skill.Type1, skill.Type2);

    /// <summary>
    /// Whether a skill-bar entry is one the client's skill window could have placed. Entries the bar holds from
    /// before that rule (passive skills) fail; the war shouts of both nations stay, and items and unknown ids are left
    /// to the client.
    /// </summary>
    public static bool HotbarKeeps(int id) =>
        SkillData.Get(id) is not { } skill || CanAddToHotbar(skill.Id) || HotbarAssign.CanDrag(skill.Type1, skill.Type2);

    /// <summary>The client's zone rule for the skill bar: in the Chaos dungeon it takes only the Chaos skill tree.</summary>
    public static bool ZoneAllows(int skillId, int zone) =>
        zone != ChaosBattle.Zone || SkillData.Get(skillId) is { Tree: ChaosBattle.SkillTree };

    /// <summary>
    /// Whether the skill bar takes an item: only items used through a skill effect (potions, scrolls), as with the
    /// client's own right-click and the original hotkey bar. Equipment, materials and quest items stay off it.
    /// </summary>
    public static bool HotbarTakesItem(int itemId) =>
        ItemData.Get(itemId) is { Effect1: not 0 } item && SkillData.IsSkill(item.Effect1);

    /// <summary>
    /// Removes the skills the client's Genie skill picker lists but the skill window would not let go (passive skills),
    /// each time the picker opens. The client fills the picker right before it pops up.
    /// </summary>
    public static void FilterGenieSkillPicker(PopupMenu picker, Node lifetime)
    {
        if (picker.HasMeta(GeniePickerFiltered)) return;
        picker.SetMeta(GeniePickerFiltered, true);
        Action prune = () =>
        {
            for (int i = picker.ItemCount - 1; i >= 0; i--)
                if (picker.GetItemId(i) is var id && id != 0 && SkillData.IsSkill(id) && !CanDragToHotbar(id)) picker.RemoveItem(i);
        };
        NativeHooks.Listen("genie.skill_picker", () => picker.AboutToPopup += prune, () =>
        {
            if (!GodotObject.IsInstanceValid(picker)) return;
            picker.AboutToPopup -= prune;
            picker.RemoveMeta(GeniePickerFiltered);
        }, lifetime);
    }

    /// <summary>
    /// Clears Genie skill cells that hold a skill the Genie cells refuse now (passive skills picked or saved before the
    /// rule). The Genie would otherwise keep casting them. Nothing changes while the Genie runs.
    /// </summary>
    public static void ClearRefusedGenieSkills(IGameGenie genie)
    {
        if (genie.Running) return;
        foreach (var (kind, cells) in GenieCells)
            for (int index = 0; index < cells; index++)
                if (genie.Cell(kind, index).SkillId is var id && id != 0 && SkillData.IsSkill(id) && !CanDragToHotbar(id))
                    genie.SetCell(kind, index, 0);
    }

    /// <summary>
    /// Whether the client's right-click on this bag-grid item adds it to the skill bar: a usable item that is not
    /// equipment, the experience jar or a VIP vault key (those open their own windows).
    /// </summary>
    public static bool BagClickAddsToHotbar(int itemId) =>
        HotbarTakesItem(itemId) && ItemData.Get(itemId) is { } item && ItemData.EquipSlotFor(item) < 0
        && itemId != ExpSeal.EmptyJar && Native.Get<HashSet<int>>(Native.StaticOwner("LibreKO.Features.Windows.VipWarehouseFeature"), "VaultKeys")?.Contains(itemId) != true;

    private const string GeniePickerFiltered = "classic_skill_rules";
    private static readonly (GenieCellKind Kind, int Cells)[] GenieCells =
    {
        (GenieCellKind.Attack, GenieSetup.AttackCells),
        (GenieCellKind.Recovery, GenieSetup.RecoveryCells),
        (GenieCellKind.Support, GenieSetup.SupportCells),
    };

    /// <summary>The client's learn state for a skill, from the points spent in its mastery tree.</summary>
    public static SkillLearnInfo Learn(int skillId, IReadOnlyList<GameMasteryTree> trees, int classCode, int level)
    {
        if (SkillData.Get(skillId) is not { } skill) return new SkillLearnInfo(SkillLearnState.Locked, 0, 0, false);
        int tree = SkillData.MasteryType(skill.Tree);
        int points = trees.FirstOrDefault(t => t.Type == tree).Points;
        return SkillLearn.For(tree, skill.Level, points, classCode, level);
    }

    /// <summary>The client's sentence for a skill that is not learned yet; <paramref name="treeName"/> names its tree.</summary>
    public static string LearnText(SkillLearnInfo learn, string treeName) => learn.State switch
    {
        SkillLearnState.Learned => "",
        SkillLearnState.Learnable => $"Spend {learn.PointsShort} more {(learn.PointsShort == 1 ? "point" : "points")} in {treeName}",
        _ when learn.NeedsClassChange => "Unlocks with your next class change",
        _ => $"Reach level {learn.LevelNeeded}",
    };

    /// <summary>Original MP line: "MP consumed : N", or "Doesn't use MP".</summary>
    public static string MpLine(int mp) =>
        mp == 0 ? ItemData.Text(TextNoMp, "Doesn't use MP") : Format(TextMpConsumed, "MP consumed : %d", mp.ToString());

    /// <summary>Original requirement line: skill points for mastery-tree skills, otherwise the level.</summary>
    public static string RequirementLine(GameSkillInfo info) => info.UsesPoints
        ? Format(TextRequiredPoints, "Required Skill Point : %d", info.RequiredPoints.ToString())
        : Format(TextRequiredLevel, "Required Level : %d", info.RequiredLevel.ToString());

    /// <summary>
    /// Original weapon line. It names the weapon the client's cast gate asks for: the client's weapon name, or
    /// "All Weapons" when the gate wants any weapon in hand.
    /// </summary>
    public static string BasicItemLine(int skillId, string basicItem)
    {
        if (basicItem.Length > 0) return Format(TextBasicItem, "Basic item : %s", RequiredItemPrefix + basicItem);
        if (SkillData.Get(skillId) is { } skill && !SkillUsable.GearAllows(skill.NeedWeapon, EmptyHand, EmptyHand))
            return Format(TextBasicItem, "Basic item : %s", ItemData.Text(TextAllWeapons, RequiredItemPrefix + "All Weapons"));
        return ItemData.Text(TextNoBasicItem, "No basic item");
    }

    /// <summary>Original required-item line.</summary>
    public static string RequiredItemLine(string item) =>
        item.Length > 0 ? Format(TextRequiredItem, "Required item : %s", item) : ItemData.Text(TextNoRequiredItem, "No required item");

    /// <summary>Original consumed-item line.</summary>
    public static string ConsumedItemLine(string item) =>
        item.Length > 0 ? Format(TextItemConsumed, "Item Consumed : %s", item) : ItemData.Text(TextNoItemConsumed, "No item consumed");

    private static string Format(int textId, string fallback, string value)
    {
        string text = ItemData.Text(textId, fallback);
        return text.Contains("%d") ? text.Replace("%d", value) : text.Replace("%s", value);
    }
}
