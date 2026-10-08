using LibreKO;
using LibreKO.Domain;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Inventory services the Classic inventory needs beyond <see cref="LibreKO.Plugins.IGameInventory"/>:
/// partial magic bag moves, the destination of a magic bag right-click, and item destruction confirmed
/// by the Classic prompt for the exact item it showed.
/// </summary>
public static class NativeInventory
{
    /// <summary>The client world behind the plugin's inventory service.</summary>
    public static World? World
    {
        get
        {
            var proxy = Native.Get<object>(Plugin.Kit.Game, "_inventory");
            if (proxy != null && Native.Get<object>(proxy, "Source") is { } source && Native.TryGet<World>(source, "_w", out var world))
                return world;
            return Native.CurrentWorld;
        }
    }

    /// <summary>Moves <paramref name="count"/> items; clients without partial moves move the whole stack.</summary>
    public static void MoveAmount(int from, int to, int count)
    {
        if (World is { } world && Native.TryCall(world, "MoveBetween", out _, from, to, count)) return;
        Plugin.Kit.Game.Inventory.Move(from, to);
    }

    /// <summary>
    /// The inventory cell a magic bag right-click targets: the first compatible stack that can take at
    /// least one more item, otherwise the first free cell. The amount prompt then decides the count.
    /// </summary>
    public static int TransferToInventorySlot(int from)
    {
        if (World is not { } world || !Native.TryGet<Inventory>(world, "Inv", out var inventory)) return FirstFreeCell();
        if (from < 0 || from >= inventory.Length || inventory[from].IsEmpty) return -1;
        var single = inventory[from];
        single.Count = 1;
        int countable = ItemData.Get(single.ItemId)?.Countable ?? 0;
        for (int slot = Inventory.GridStart; slot < Inventory.GridStart + Inventory.GridCount && slot < inventory.Length; slot++)
            if (ItemMove.Merges(ItemMove.MagicBagToInventory, single, inventory[slot], countable)) return slot;
        return inventory.FirstFreeGridSlot();
    }

    /// <summary>
    /// Destroys the item in <paramref name="slot"/> only if it is still <paramref name="itemId"/>, is not a
    /// costume or magic bag item and is not held by an open service. The client sends and applies it.
    /// </summary>
    public static void ConfirmDrop(int slot, int itemId)
    {
        if (World is not { } world || !Native.TryGet<Inventory>(world, "Inv", out var inventory))
        {
            Plugin.Kit.Game.Inventory.Drop(slot);
            return;
        }
        if (slot < 0 || slot >= inventory.Length || inventory[slot].ItemId != itemId) return;
        if (slot >= InventoryConstants.CospreStart || Native.Call(world, "RefuseItemInUse", slot, -1) is true) return;
        if (!Native.Set(world, "_invDelSlot", slot) || !Native.Set(world, "_invDelItemId", itemId)) return;
        Native.Call(world, "ConfirmDeleteItem");
    }

    private static int FirstFreeCell()
    {
        var inventory = Plugin.Kit.Game.Inventory;
        for (int slot = inventory.GridStart; slot < inventory.GridStart + inventory.GridCount; slot++)
            if (inventory.At(slot).IsEmpty) return slot;
        return -1;
    }
}
