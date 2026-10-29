using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Recognises the NPC opening choices that had their own original window: the Inn Hostess (warehouse sub 0x10,
/// co_inn_us) and the Blacksmith and other repairing merchants (co_smith_us). The client answers both with its
/// shared NPC dialogue, so the Classic skin learns which menu it holds from the same network events. The marks
/// follow the client's own choice buttons and end with them.
/// </summary>
public static class NativeNpcOpening
{
    public const string Inn = "inn", Smith = "smith";
    private static readonly ConditionalWeakTable<WorldRoot, NativeHooks.Hook> _inn = new(), _smith = new();
    private static readonly ConditionalWeakTable<Node, string> _choices = new();

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (Game.Connection is not { } net) return;
        var weakWorld = new WeakReference<WorldRoot>(world);
        // The client may subscribe before or after the plugin; mark once its menu is built.
        void Mark(string kind)
        {
            if (weakWorld.TryGetTarget(out var w) && GodotObject.IsInstanceValid(w))
                Callable.From(() => { if (GodotObject.IsInstanceValid(w)) Opened(w, kind); }).CallDeferred();
        }
        if (!Held(_inn, world))
        {
            Action handler = () => Mark(Inn);
            _inn.AddOrUpdate(world, NativeHooks.Listen("Net.WarehouseNpcEvent", () => Game.Session.Warehouse.WarehouseNpcEvent += handler, () => Game.Session.Warehouse.WarehouseNpcEvent -= handler, world));
        }
        if (!Held(_smith, world))
        {
            Action<int> handler = _ => Mark(Smith);
            _smith.AddOrUpdate(world, NativeHooks.Listen("Net.RepairNpcEvent", () => Game.Session.Repair.RepairNpcEvent += handler, () => Game.Session.Repair.RepairNpcEvent -= handler, world));
        }
    }

    private static bool Held(ConditionalWeakTable<WorldRoot, NativeHooks.Hook> hooks, WorldRoot world) => hooks.TryGetValue(world, out var held) && held.Active;

    /// <summary>Marks the choice buttons of the NPC dialogue the client has just built as the <paramref name="kind"/> opening.</summary>
    public static void Opened(WorldRoot world, string kind)
    {
        if (Native.Get<HudWindow>(world, "NpcDialogWindow.Panel") is not { Visible: true }) return;
        if (Native.Get<VBoxContainer>(world, "NpcDialogWindow.menuBox") is not { } menu || !GodotObject.IsInstanceValid(menu)) return;
        foreach (var choice in menu.GetChildren().OfType<Button>()) _choices.AddOrUpdate(choice, kind);
    }

    /// <summary>The opening the native NPC menu holds (<see cref="Inn"/>, <see cref="Smith"/>), or null for any other menu.</summary>
    public static string? Kind(Node menu) =>
        GodotObject.IsInstanceValid(menu) && menu.GetChildCount() > 0 && _choices.TryGetValue(menu.GetChild(0), out var kind) ? kind : null;
}

public static partial class NativeSetup
{
    static partial void RegisterNpc(PluginContext context) => NativeWindows.Prepare("npc_dialog", NativeNpcOpening.Prepare);
}
