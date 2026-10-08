using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>Native window preparers of the character, quest, NPC and party windows.</summary>
public static class NativeCharacterNpc
{
    private static readonly (string Id, NativeWindows.Preparer Prepare)[] Preparers =
    {
        ("quests", NativeQuestLog.Prepare),
        ("presets", NativeQuestLog.PreparePresets),
        ("npc_dialog", NativeNpc.Prepare),
        ("npc_dialog", (window, world) => NativeEvents.Prepare(world)),
        ("quest_available", NativeNpc.PrepareNotification),
        ("npc_dialog", KeepContentAnchor),
        ("quest_available", KeepContentAnchor),
        ("quest_receipt", KeepContentAnchor),
        ("party", NativeParty.PrepareRoster),
        ("seek_party", NativeParty.PrepareBoard),
    };

    /// <summary>
    /// A window marked <c>content_open_anchor</c> places itself; the client's default placement and
    /// settling recentre keep its position instead of moving it.
    /// </summary>
    private static void KeepContentAnchor(HudWindow window, World world)
    {
        var layout = window.Layout;
        if (Native.Get<Func<Godot.Vector2>>(layout, "_defaultPosition") is not { } placement) return;
        Native.Set(layout, "_defaultPosition", (Func<Godot.Vector2>)(() => window.HasMeta("content_open_anchor") ? window.Position : placement()));
    }

    internal static void Register()
    {
        foreach (var (id, prepare) in Preparers) NativeWindows.Prepare(id, prepare);
    }

    /// <summary>Runs this group's preparers for a window built outside the client's window flow.</summary>
    public static void Prepare(HudWindow window, World world)
    {
        foreach (var (id, prepare) in Preparers)
            if (string.Equals(id, window.Id, StringComparison.OrdinalIgnoreCase)) prepare(window, world);
    }
}

public static partial class NativeSetup
{
    static partial void RegisterCharacter(PluginContext context) => NativeCharacterNpc.Register();
}
