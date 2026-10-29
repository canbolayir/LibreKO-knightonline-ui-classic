using Godot;
using LibreKO;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Windows;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The client's "Quest Journal" key (F10, D-pad right on a pad) opens the Character window's Quest list, the same
/// toggle as the HUD's Q. Classic shows the client's journal window as the Quest Details subpage, which is empty
/// until a quest is picked. Only that key's entries in the World's hotkey tables are replaced: the tracker, NPC
/// quest flows and the plugin window API still open the journal (Quest Details) on their quest.
/// </summary>
internal static class NativeQuestKey
{
    internal static void Attach(WorldRoot world, PluginGame game)
    {
        var attached = new WeakReference<WorldRoot>(world);
        // Deferred: the World builds its hotkeys after it attaches the plugin game API, and rebuilds them on a rebind.
        Action route = () => Callable.From(() =>
        {
            if (Plugin.Active && attached.TryGetTarget(out var w) && GodotObject.IsInstanceValid(w) && w.IsInsideTree()) Route(w, game);
        }).CallDeferred();
        NativeHooks.Listen("KeyBinds.Changed (quest key)", () => KeyBinds.Changed += route, () => KeyBinds.Changed -= route, world);
        route();
    }

    private static void Route(WorldRoot world, PluginGame game)
    {
        var key = (world, nameof(NativeQuestKey));
        NativeHooks.ReleaseKey(key);
        Action toggle = () => CharacterWindow.ToggleQuestList(game);
        var restore = new List<Action>();
        Patch(world, "Hotkeys.hotkeys", KeyBinds.Get(KeyAction.Quests), toggle, restore);
        Patch(world, "Hotkeys.padHotkeys", KeyBinds.GetPad(KeyAction.Quests), toggle, restore);
        if (restore.Count > 0) NativeHooks.Hold("World hotkeys (quest key)", () => { foreach (var undo in restore) undo(); }, world, key);
    }

    private static void Patch<TChord>(WorldRoot world, string member, TChord chord, Action toggle, List<Action> restore) where TChord : notnull
    {
        if (Native.Get<Dictionary<TChord, Action>>(world, member) is not { } table || !table.TryGetValue(chord, out var native)) return;
        table[chord] = toggle;
        // A rebuilt table (rebind, world reload) no longer holds the replacement and is left alone.
        restore.Add(() => { if (table.TryGetValue(chord, out var now) && ReferenceEquals(now, toggle)) table[chord] = native; });
    }
}
