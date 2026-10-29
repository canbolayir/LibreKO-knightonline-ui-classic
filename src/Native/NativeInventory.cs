using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Inventory services the Classic inventory needs beyond <see cref="LibreKO.Plugins.IGameInventory"/>: the
/// destination of a magic bag right-click, and item destruction through the client's bulk destroy queue,
/// confirmed by the Classic prompt. Partial moves use the client's <c>IGameInventory.MoveAmount</c>.
/// </summary>
public static class NativeInventory
{
    /// <summary>The client world behind the plugin's inventory service.</summary>
    public static WorldRoot? World
    {
        get
        {
            var proxy = Native.Get<object>(Plugin.Kit.Game, "inventory");
            if (proxy != null && Native.Get<object>(proxy, "Source") is { } source && Native.TryGet<WorldRoot>(source, "world", out var world))
                return world;
            return Native.CurrentWorld;
        }
    }

    /// <summary>
    /// The inventory cell a magic bag right-click targets: the first compatible stack that can take at
    /// least one more item, otherwise the first free cell. The amount prompt then decides the count.
    /// </summary>
    public static int TransferToInventorySlot(int from)
    {
        if (World is not { } world || Game.Session.Self.Inventory is not { } inventory) return FirstFreeCell();
        if (from < 0 || from >= inventory.Length || inventory[from].IsEmpty) return -1;
        var single = inventory[from];
        single.Count = 1;
        int countable = ItemData.Get(single.ItemId)?.Countable ?? 0;
        for (int slot = Inventory.GridStart; slot < Inventory.GridStart + Inventory.GridCount && slot < inventory.Length; slot++)
            if (ItemMove.Merges(ItemMoveDirection.MagicBagToInventory, single, inventory[slot], countable)) return slot;
        return inventory.FirstFreeGridSlot();
    }

    /// <summary>
    /// Destroys the item in <paramref name="slot"/> only if it is still <paramref name="itemId"/>, is not a
    /// costume or magic bag item and is not held by an open service. The client queues, sends and applies it.
    /// </summary>
    public static void ConfirmDrop(int slot, int itemId)
    {
        if (World is not { } world || Game.Session.Self.Inventory is not { } inventory
            || Native.Get<DestroyQueue>(world, "InventoryWindow.invDelQueue") is not { } queue)
        {
            Plugin.Kit.Game.Inventory.Drop(slot);
            return;
        }
        if (slot < 0 || slot >= inventory.Length || inventory[slot].ItemId != itemId) return;
        if (slot >= InventoryConstants.CospreStart || Native.Call(world, "BagCompanions.RefuseItemInUse", slot, -1) is true) return;
        if (queue.Running) return;
        queue.Clear();
        if (!queue.Add(slot, itemId)) return;
        Native.Call(world, "InventoryWindow.ConfirmDeleteItem");
    }

    /// <summary>The items the client's bulk destroy queue holds, in destruction order (empty without a queue).</summary>
    public static IReadOnlyList<DestroyQueue.Entry> DestroyEntries =>
        World is { } world && Native.Get<DestroyQueue>(world, "InventoryWindow.invDelQueue") is { } queue ? queue.Entries : Array.Empty<DestroyQueue.Entry>();

    /// <summary>Whether the client destroys items through a bulk queue the Classic prompt can drive.</summary>
    public static bool HasDestroyQueue => World is { } world && Native.Get<DestroyQueue>(world, "InventoryWindow.invDelQueue") != null;

    /// <summary>
    /// Adds an item to the client's destroy queue through its own checks (in use, sealed Cypher Ring,
    /// duplicates). Returns whether it was queued. The client's own queue card stays hidden.
    /// </summary>
    public static bool QueueDestroy(int slot)
    {
        if (World is not { } world || Native.Get<DestroyQueue>(world, "InventoryWindow.invDelQueue") is not { Running: false } queue) return false;
        int before = queue.Count;
        Native.Call(world, "InventoryWindow.AskDeleteItem", slot);
        HideDestroyCard();
        return queue.Count > before;
    }

    /// <summary>Keeps an item: removes it from the destroy queue.</summary>
    public static void UnqueueDestroy(int slot)
    {
        if (World is { } world && Native.Get<DestroyQueue>(world, "InventoryWindow.invDelQueue") is { } queue) queue.Remove(slot);
    }

    /// <summary>Destroys every queued item that is still in place, one server request at a time.</summary>
    public static void ConfirmDestroy()
    {
        if (World is { } world) Native.Call(world, "InventoryWindow.ConfirmDeleteItem");
    }

    /// <summary>Cancels the destroy queue.</summary>
    public static void CancelDestroy()
    {
        if (World is { } world) Native.Call(world, "InventoryWindow.HideDeletePrompt");
    }

    /// <summary>The VIP vault key item, whose art marks the bag's VIP Vault shortcut.</summary>
    public const int VipVaultKey = 800_442_000;

    /// <summary>Opens the VIP vault from the bag, where the client allows it (its zone rules apply).</summary>
    public static void OpenVipVault()
    {
        if (World is { } world) Native.Call(world, "VipWarehouse.OpenVaultFromBag");
    }

    /// <summary>Hides the client's own queue card; the Classic prompt shows the queue instead.</summary>
    public static void HideDestroyCard()
    {
        if (World is { } world && Native.Get<Control>(world, "InventoryWindow.InvDelPanel") is { Visible: true } card) card.Visible = false;
    }

    private static int FirstFreeCell()
    {
        var inventory = Plugin.Kit.Game.Inventory;
        for (int slot = inventory.GridStart; slot < inventory.GridStart + inventory.GridCount; slot++)
            if (inventory.At(slot).IsEmpty) return slot;
        return -1;
    }
}
