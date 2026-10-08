using System.IO.Compression;
using System.Text.Json;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Classic Magic Anvil placement rules. A bench socket only accepts a material that can still complete
/// an upgrade recipe for the staged origin: the scroll class must suit the origin's item class, one
/// scroll and one protection at most, and the pair must have a server upgrade setting with a nonzero
/// rate. The data is a snapshot of the server seed tables (see <c>tools/generate_anvil_rules.py</c>);
/// the server still validates every preview and upgrade.
/// </summary>
public static class AnvilPlacementRules
{
    public const string SettingsResource = "KnightOnlineUiClassic.AnvilUpgradeSettings";
    public const string OriginsResource = "KnightOnlineUiClassic.AnvilOrigins";

    private const int HighScrollFirst = 379016000;
    private const int HighScrollLast = 379035000;
    private const int DispelScrollFirst = 379138000;
    private const int DispelScrollLast = 379141000;
    private const int ClassUpgradeScroll = 379152000;
    private const int AccessoryScrollFirst = 379159000;
    private const int AccessoryScrollLast = 379164000;
    private const int MiddleScrollFirst = 379205000;
    private const int MiddleScrollLast = 379220000;
    private const int LowScrollFirst = 379221000;
    private const int LowScrollLast = 379235000;
    private const int TrainingScroll = 379255000;
    private const int ReverseScroll = 379256000;
    private const int RebirthScroll = 379257000;
    private const int TrinaPiece = 700002000;
    private const int KarivdisPiece = 379258000;
    private const int TrinaPieceMiddle = 352900000;
    private const int TrinaPieceLow = 353000000;
    private const int TrinaPieceAccessory = 354000000;
    private const int BlessingLogos = 890092000;
    private const int LogosGradeLimit = 10;
    private const int AnyGrade = 99;
    private const int ItemBenchFirstMaterial = 1;
    private const int AccessoryBenchFirstMaterial = 3;

    private const int OriginId = 0;
    private const int OriginClass = 1;
    private const int OriginType = 2;
    private const int OriginGrade = 3;
    private const int OriginKind = 4;
    private const int OriginScrolls = 5;

    private static readonly int[] AccessoryKinds = { 91, 92, 93, 94 };
    private static readonly int[] LogosItemTypes = { 4, 5, 11, 12 };

    private enum Scroll { None, Low, Middle, High, ClassUpgrade, Reverse, Rebirth, Accessory }

    private sealed record Setting(int ReqItem1, int ReqItem2, int ItemType, int ItemGrade, int SuccessRate)
    {
        public bool Usable => SuccessRate > 0;
        public bool Uses(int item) => ReqItem1 == item || ReqItem2 == item;
        public bool Uses(int first, int second) => Uses(first) && Uses(second);
        public bool Suits(int itemType, int grade) => ItemType == itemType && (ItemGrade == grade || ItemGrade == AnyGrade);
    }

    private static readonly Lazy<Setting[]> Settings = new(() =>
    {
        using var stream = Resource(SettingsResource);
        return JsonSerializer.Deserialize<Setting[]>(stream)!;
    });

    private static readonly Lazy<Dictionary<int, int[]>> Origins = new(() =>
    {
        using var source = Resource(OriginsResource);
        using var stream = new GZipStream(source, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<int[][]>(stream)!.ToDictionary(row => row[OriginId]);
    });

    /// <summary>Number of upgradable origins in the snapshot.</summary>
    public static int OriginCount => Origins.Value.Count;

    /// <summary>
    /// Whether <paramref name="items"/> (socket 0 = origin, or sockets 0–2 = accessories on the compound
    /// bench) can still form a recipe. A protection without a scroll is allowed when some recipe scroll
    /// of the origin would complete it.
    /// </summary>
    public static bool Allows(ReadOnlySpan<int> items, bool compound)
    {
        if (items[0] == 0) return Allows(items, compound, 0, 0, 0, false);
        if (!Origins.Value.TryGetValue(items[0], out var origin)) return false;
        var scrolls = origin.AsSpan(OriginScrolls);
        bool hasScroll = false, hasProtection = false;
        for (int i = FirstMaterial(compound); i < items.Length; i++)
        {
            if (Classify(items[i]) != Scroll.None)
            {
                if (!scrolls.Contains(items[i])) return false;
                hasScroll = true;
            }
            else if (items[i] != 0) hasProtection = true;
        }
        bool accessory = AccessoryKinds.Contains(origin[OriginKind]);
        if (hasScroll || !hasProtection)
            return Allows(items, compound, origin[OriginClass], origin[OriginType], origin[OriginGrade], accessory);

        var proposed = items.ToArray();
        int slot = Array.FindIndex(proposed, FirstMaterial(compound), id => id == 0);
        if (slot < 0) return false;
        foreach (int scroll in origin.AsSpan(OriginScrolls))
        {
            proposed[slot] = scroll;
            if (Allows(proposed, compound, origin[OriginClass], origin[OriginType], origin[OriginGrade], accessory)) return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="id"/> is an upgrade material on the item or compound bench.</summary>
    public static bool IsMaterial(int id, bool compound)
    {
        bool role = compound
            ? Classify(id) == Scroll.Accessory || id == TrinaPieceAccessory
            : Classify(id) is not (Scroll.None or Scroll.Accessory) || Protection(id) && id != TrinaPieceAccessory;
        return role && (id == BlessingLogos || Settings.Value.Any(s => s.Usable && s.Uses(id)));
    }

    /// <summary>The rules for an origin with the given item class, type and grade.</summary>
    public static bool Allows(ReadOnlySpan<int> items, bool compound, int itemClass, int itemType, int grade, bool accessory)
    {
        int scroll = 0, protection = 0;
        for (int i = FirstMaterial(compound); i < items.Length; i++)
        {
            int id = items[i];
            if (id == 0) continue;
            if (!IsMaterial(id, compound)) return false;
            if (Classify(id) != Scroll.None)
            {
                if (scroll != 0) return false;
                scroll = id;
            }
            else
            {
                if (protection != 0) return false;
                protection = id;
            }
        }
        var category = Classify(scroll);
        bool logos = protection == BlessingLogos;
        if (logos && category is Scroll.Reverse or Scroll.Accessory) return false;
        if (scroll != 0 && protection != 0 && !logos && !Settings.Value.Any(s => s.Usable && s.Uses(scroll, protection))) return false;
        if (items[0] == 0) return true;
        if (compound != accessory) return false;
        if (scroll != 0 && !compound && !AllowedClass(itemClass, category)) return false;
        if (logos && (grade >= LogosGradeLimit || !LogosItemTypes.Contains(itemType))) return false;
        if (scroll == 0)
            return protection == 0 || logos
                || Settings.Value.Any(s => s.Suits(itemType, grade) && s.Usable && s.Uses(protection));
        if (logos) return true;
        var settings = Settings.Value.Where(s => s.Suits(itemType, grade));
        if (protection == 0) return settings.Any(s => s.Usable && s.Uses(scroll));
        return settings.FirstOrDefault(s => s.Uses(scroll, protection)) is { Usable: true };
    }

    private static int FirstMaterial(bool compound) => compound ? AccessoryBenchFirstMaterial : ItemBenchFirstMaterial;

    private static Scroll Classify(int id) => id switch
    {
        >= HighScrollFirst and <= HighScrollLast or >= DispelScrollFirst and <= DispelScrollLast => Scroll.High,
        ClassUpgradeScroll => Scroll.ClassUpgrade,
        >= AccessoryScrollFirst and <= AccessoryScrollLast => Scroll.Accessory,
        >= MiddleScrollFirst and <= MiddleScrollLast => Scroll.Middle,
        >= LowScrollFirst and <= LowScrollLast or TrainingScroll => Scroll.Low,
        ReverseScroll => Scroll.Reverse,
        RebirthScroll => Scroll.Rebirth,
        _ => Scroll.None,
    };

    private static bool Protection(int id) =>
        id is TrinaPiece or KarivdisPiece or TrinaPieceMiddle or TrinaPieceLow or TrinaPieceAccessory or BlessingLogos;

    // Item classes from the server item tables: 0/1 low, 2 middle, 3/7/33 high, 4/5/35 rebirth gear.
    private static bool AllowedClass(int itemClass, Scroll scroll) => itemClass switch
    {
        0 or 1 => scroll is Scroll.Low or Scroll.Middle or Scroll.High or Scroll.ClassUpgrade,
        2 => scroll is Scroll.Middle or Scroll.High or Scroll.ClassUpgrade,
        3 or 7 or 33 => scroll is Scroll.High or Scroll.Reverse or Scroll.ClassUpgrade,
        4 or 5 or 35 => scroll is Scroll.Rebirth or Scroll.Reverse or Scroll.High,
        _ => false,
    };

    private static Stream Resource(string name) =>
        typeof(AnvilPlacementRules).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException("Missing embedded anvil data: " + name);
}
