using LibreKO.Domain;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Skill window data the client's plugin bridge reports differently from the Classic presentation:
/// the weapon requirement follows the equipment group the client checks when casting, and mastery
/// trees the class cannot use are hidden.
/// </summary>
public static class NativeSkills
{
    private const int AnyWeaponGroup = 0, DaggerGroup = 1, SwordGroup = 2, AxeGroup = 3, MaceGroup = 4, SpearGroup = 5,
        ShieldGroup = 6, BowGroup = 7, LongbowGroup = 8, LauncherGroup = 10, StaffGroup = 11, JamadarGroup = 14, HeavyMaceGroup = 18;

    /// <summary>Names the equipment group checked by the client's gear test, including unrestricted weapon skills.</summary>
    public static string EquippedWeaponRequirementName(int itemGroup) => itemGroup switch
    {
        WeaponAnimation.GroupNeedsNoWeapon => "",
        AnyWeaponGroup => "Any weapon",
        DaggerGroup => "Dagger",
        SwordGroup => "Sword",
        AxeGroup => "Axe",
        MaceGroup or HeavyMaceGroup => "Mace",
        SpearGroup => "Spear, Polearm",
        ShieldGroup => "Shield",
        BowGroup => "Bow, Crossbow",
        LongbowGroup => "Longbow",
        LauncherGroup => "Launcher",
        StaffGroup => "Staff",
        JamadarGroup => "Jamadar",
        _ => $"Weapon group {itemGroup}",
    };

    extension(IGameSkills skills)
    {
        /// <summary>Skill details with the weapon requirement taken from the casting equipment group.</summary>
        public GameSkillInfo ClassicInfo(int skillId)
        {
            var info = skills.Info(skillId);
            return SkillData.Get(skillId) is { } skill ? info with { BasicItem = EquippedWeaponRequirementName(skill.ItemGroup) } : info;
        }

        /// <summary>Mastery trees, shown only when the class can invest in them.</summary>
        public IReadOnlyList<GameMasteryTree> ClassicTrees(int classCode) =>
            skills.Trees.Select(tree => tree with { Shown = MasteryPoints.ClassHasTree(classCode, tree.Type) }).ToArray();
    }
}
