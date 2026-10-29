using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Native;

/// <summary>
/// World Map and Fortune Teller fixtures. The World Map opens through the client's own toggle (its request
/// goes nowhere offline) and receives zone lists through the native reply handler; the Fortune Teller uses
/// the client's <c>BuildFortuneUiPreview</c>. Both run this group's preparer as the window flow would.
/// </summary>
public static partial class PreviewFixtures
{
    /// <summary>The World Map detached with its native layout, so header dragging stays live.</summary>
    public static HudWindow BuildGlobalMapPreview(WorldRoot world)
    {
        Call(world, "GlobalMap.Build");
        Call(world, "GlobalMap.Toggle");
        var window = Detach<HudWindow>(world, "GlobalMap.panel");
        NativeMapFortune.Prepare(window, world);
        return window;
    }

    private static readonly (string Name, byte Nation)[] GlobalMapZones =
    {
        ("Luferson Castle", Nations.Karus), ("El Morad Castle", Nations.ElMorad), ("Moradon", 0), ("Eslant", 0),
        ("Ronark Land", 0), ("Ardream", 0), ("Ronark Land Base", 0), ("Delos", Nations.Karus),
        ("Lunar Valley", 0), ("Bifrost", Nations.ElMorad), ("Forgotten Temple", 0), ("Juraid Mountain", 0),
        ("Krowaz's Dominion", 0), ("Border Defense War", 0), ("Chaos Dungeon", 0), ("Under the Castle", Nations.ElMorad),
        ("Battle Zone of the Ancient Northern Alliance", 0), ("Desperation Abyss", 0),
    };

    /// <summary>Delivers a zone list through the native reply handler: filled (8 zones), long (18) or empty.</summary>
    public static void FillGlobalMap(WorldRoot world, string view)
    {
        int count = view switch { "long" => GlobalMapZones.Length, "empty" => 0, _ => 8 };
        var zones = GlobalMapZones.Take(count).Select((zone, i) => new GlobalMapZone
        {
            ZoneId = i + 1, Name = zone.Name, OwnerNation = zone.Nation, Population = i % 4 == 3 ? 0 : 12 + i * 37 % 211,
        }).ToList();
        Call(world, "GlobalMap.OnList", zones);
    }

    public static HudWindow BuildFortuneWindowPreview(WorldRoot world, bool revealed)
    {
        var window = (HudWindow)Call(world, "Preview.BuildFortune", revealed)!;
        NativeMapFortune.Prepare(window, world);
        return window;
    }

    /// <summary>The Fortune Teller detached with its native layout, so header dragging stays live.</summary>
    public static HudWindow BuildFortuneDragPreview(WorldRoot world)
    {
        Call(world, "Fortune.Build"); Call(world, "Fortune.Open");
        var window = Detach<HudWindow>(world, "Fortune.Panel");
        NativeMapFortune.Prepare(window, world);
        return window;
    }
}
