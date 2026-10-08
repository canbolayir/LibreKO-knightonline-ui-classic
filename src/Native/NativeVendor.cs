using System.Collections;
using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterVendor(PluginContext context) => NativeWindows.Prepare("vendor", NativeVendor.Prepare);
}

/// <summary>
/// The Classic NPC shop on top of the client's vendor: an explicit approval step before every buy and
/// sale, the shop state the skin reads (<c>vendor_blocked</c>, <c>vendor_dead</c>, <c>vendor_group</c>),
/// the item identity in the catalogue drag payload, catalogue pages packed without empty source pages,
/// and stack count badges that also show a single countable item.
/// </summary>
public static class NativeVendor
{
    public const string UntradableText = "This item cannot be sold.";
    public const string StaleText = "The item or quantity changed. Please try again.";

    private sealed class State
    {
        public bool Confirming;
        public ShopCatalogue? Compacted;
        public object? Companion;
    }

    private static readonly ConditionalWeakTable<World, State> _states = new();

    private static State StateOf(World world) => _states.GetOrCreateValue(world);

    public static void Prepare(HudWindow window, World world)
    {
        if (window.HasMeta("native_vendor")) return;
        window.SetMeta("native_vendor", true);
        window.SetMeta("vendor_blocked", Callable.From(() => Blocked(world)));
        window.SetMeta("vendor_dead", Callable.From(() => Native.Get<bool>(world, "_selfDead")));
        window.SetMeta("vendor_group", Native.Get<int>(world, "_vendorGroup"));

        if (Native.Get<ItemSlotView[]>(world, "_vendorCells") is { } cells)
            foreach (var cell in cells)
            {
                var drag = cell.DragOut;
                cell.DragOut = view => DragOut(world, view, drag);
                ReplaceHandler<ItemSlotView>(cell, "RightClicked", "OnVendorCellBuy", view => CellBuy(world, view));
                ReplaceHandler<ItemSlotView>(cell, "DoubleClicked", "OnVendorCellBuy", view => CellBuy(world, view));
                cell.Dropped = (_, data) => SellDrop(world, data);
            }
        foreach (var well in Native.Descendants(window.Body).OfType<DropWell>())
            well.Dropped = data => SellDrop(world, data);
        if (Native.Get<Button>(world, "_vendorBuy") is { } buy)
            Rewire(buy, () => AskBuy(world, Native.Get<int>(world, "_vendorSelected"), -1));

        NativeGoldLog.Attach(world);
        NativeWindows.Sync(window, () => Sync(window, world));
        // Runs after the client opened the shop, so the packed pages are in place before the first frame.
        if (Net.I is { } net)
        {
            Action<int> opened = _ => Sync(window, world);
            net.TradeNpcEvent += opened;
            world.TreeExiting += () => net.TradeNpcEvent -= opened;
        }
    }

    /// <summary>A purchase, sale, approval or inventory move is pending.</summary>
    public static bool Blocked(World world) =>
        Native.Get<bool>(world, "_tradeInFlight") || StateOf(world).Confirming || Native.Get<bool>(world, "_moveInFlight")
        || Native.Get<ICollection>(world, "_moveQueue") is { Count: > 0 };

    public static bool Confirming(World world) => StateOf(world).Confirming;

    private static bool Busy(World world) => Blocked(world) || Native.Get<bool>(world, "_selfDead");

    /// <summary>Moves an inventory item unless a shop transaction is pending.</summary>
    public static void Move(World world, int from, int to)
    {
        if (Busy(world)) return;
        Native.Call(world, "MoveBetween", from, to);
    }

    private static void Sync(HudWindow window, World world)
    {
        window.SetMeta("vendor_group", Native.Get<int>(world, "_vendorGroup"));
        CompactCatalogue(world);
        RouteCompanion(world);
        if (Native.Get<ItemSlotView[]>(world, "_vendorCells") is { } cells)
            foreach (var cell in cells) NativeCountBadge.Apply(cell);
    }

    /// <summary>Packs the products into consecutive pages; buy requests keep their source coordinates.</summary>
    private static void CompactCatalogue(World world)
    {
        var state = StateOf(world);
        if (Native.Get<ShopCatalogue>(world, "_vendorCatalogue") is not { } catalogue || ReferenceEquals(catalogue, state.Compacted)) return;
        var ids = catalogue.Pages.SelectMany(page => catalogue.Page(page).Where(id => id != 0)).ToArray();
        var compact = new ShopCatalogue(ids.Select((id, i) => new ShopCatalogue.Entry(id, i / ShopCatalogue.PageSize, i % ShopCatalogue.PageSize)),
            ItemData.DisplayName);
        state.Compacted = compact;
        if (!Native.Set(world, "_vendorCatalogue", compact)) return;
        Native.Call(world, "ShowVendorPage", 0);
    }

    /// <summary>Routes the inventory's buy and sell gestures through the approval step.</summary>
    private static void RouteCompanion(World world)
    {
        var state = StateOf(world);
        if (Native.Get<object>(world, "_vendorCompanion") is not { } companion || ReferenceEquals(companion, state.Companion)) return;
        state.Companion = companion;
        Native.Set(companion, "Take", new Func<int, bool>(abs =>
        {
            if (!InMainBag(world, abs)) return false;
            AskSell(world, abs);
            return true;
        }));
        Native.Set(companion, "IntoBag", new Func<int, int, bool>((cell, abs) =>
        {
            var ids = Native.Get<int[]>(world, "_vendorCellIds");
            if (ids == null || cell < 0 || cell >= ids.Length || ids[cell] == 0) return false;
            AskBuy(world, ids[cell], abs);
            return true;
        }));
    }

    private static Variant DragOut(World world, ItemSlotView cell, Func<ItemSlotView, Variant>? native)
    {
        if (Busy(world) || native == null) return default;
        var data = native(cell);
        var ids = Native.Get<int[]>(world, "_vendorCellIds");
        if (data.VariantType == Variant.Type.Dictionary && ids != null && cell.Index >= 0 && cell.Index < ids.Length)
            data.AsGodotDictionary()["id"] = ids[cell.Index];
        return data;
    }

    private static void CellBuy(World world, ItemSlotView cell)
    {
        var ids = Native.Get<int[]>(world, "_vendorCellIds");
        if (ids == null || cell.Index < 0 || cell.Index >= ids.Length || ids[cell.Index] == 0) return;
        Native.Call(world, "SelectVendorItem", ids[cell.Index]);
        AskBuy(world, ids[cell.Index], -1);
    }

    private static void SellDrop(World world, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return;
        var payload = data.AsGodotDictionary();
        if (!payload.ContainsKey("invFrom")) return;
        int abs = payload["invFrom"].AsInt32();
        if (!InMainBag(world, abs)) return;
        if (payload.ContainsKey("id") && Inventory(world)?[abs].ItemId != payload["id"].AsInt32()) return;
        AskSell(world, abs);
    }

    public static void AskBuy(World world, int itemId, int preferred)
    {
        if (Busy(world) || Native.Get<Dictionary<int, ItemData.SellEntry>>(world, "_vendorEntries") is not { } entries
            || !entries.TryGetValue(itemId, out var entry)) return;
        if (ItemData.Get(itemId) is not { } def) return;
        int price = ItemData.BuyPrice(itemId);
        var sheet = Net.I.Sheet;
        long? freeWeight = sheet.MaxWeight > 0 ? sheet.MaxWeight - Convert.ToInt64(Native.Call(world, "CarriedWeight")) : null;
        int room = Convert.ToInt32(Native.Call(world, "BuyRoom", itemId, def, preferred));
        int max = BuyLimit.Max(Native.Get<long>(world, "VendorWallet"), price, freeWeight, def.Weight, room);
        if (max <= 0)
        {
            string problem = CanBuy(world, itemId, 1, preferred);
            Status(world, problem.Length > 0 ? problem : "You can't buy that.");
            return;
        }
        if (def.Countable == 0 || max == 1)
        {
            ConfirmBuy(world, entry, 1, preferred);
            return;
        }
        string currency = Currency(world);
        Native.Get<QuantityPrompt>(world, "_tradePrompt")?.Open(ItemData.Icon(itemId), $"Buy {ItemData.DisplayName(itemId)}",
            $"{price:n0} {currency} each · up to {max:n0}", max, 1, n => ConfirmBuy(world, entry, (int)n, preferred),
            "Buy", n => $"Total {(long)price * n:n0} {currency} · {(long)def.Weight * n / 10f:0.0} wt");
    }

    private static void ConfirmBuy(World world, ItemData.SellEntry entry, int count, int preferred)
    {
        if (Busy(world) || count <= 0 || Native.Get<Dictionary<int, ItemData.SellEntry>>(world, "_vendorEntries")?.ContainsKey(entry.Id) != true) return;
        string problem = CanBuy(world, entry.Id, count, preferred);
        if (problem.Length > 0) { Status(world, problem); return; }
        ConfirmTrade(world, true, entry.Id, count, ItemData.BuyPrice(entry.Id), () => Native.Call(world, "BuyAmount", entry, count, preferred));
    }

    public static void AskSell(World world, int abs)
    {
        if (Busy(world) || !InMainBag(world, abs) || Inventory(world) is not { } inv || inv[abs].IsEmpty) return;
        if (Native.Call(world, "RefuseItemInUse", abs, -1) is true) return;
        if (Native.Get<bool>(world, "LoyaltyShop"))
        {
            Status(world, "The Loyalty Merchant buys nothing back.");
            return;
        }
        var slot = inv[abs];
        if (!Tradable(slot))
        {
            Status(world, UntradableText);
            return;
        }
        int unit = SellUnit(slot.ItemId);
        if (unit <= 0)
        {
            Status(world, "The merchant won't buy that.");
            return;
        }
        if (slot.Count <= 1 || ItemData.Get(slot.ItemId) is not { Countable: not 0 })
        {
            ConfirmSell(world, abs, 1, slot.ItemId);
            return;
        }
        Native.Get<QuantityPrompt>(world, "_tradePrompt")?.Open(ItemData.Icon(slot.ItemId), $"Sell {ItemData.DisplayName(slot.ItemId)}",
            $"{unit:n0} gold each · you carry {slot.Count:n0}", slot.Count, slot.Count, n => ConfirmSell(world, abs, (int)n, slot.ItemId),
            "Sell", n => $"Sells for {(long)unit * n:n0} gold");
    }

    private static void ConfirmSell(World world, int abs, int count, int itemId)
    {
        if (Busy(world) || !InMainBag(world, abs) || Inventory(world) is not { } inv) return;
        if (inv[abs].ItemId != itemId || count <= 0 || inv[abs].Count < count) return;
        if (!Tradable(inv[abs]) || SellUnit(itemId) <= 0 || Native.Get<bool>(world, "LoyaltyShop")) return;
        ConfirmTrade(world, false, itemId, count, SellUnit(itemId), () =>
        {
            if (inv[abs].ItemId != itemId || inv[abs].Count < count) { Status(world, StaleText); return; }
            if (!Native.TryCall(world, "SellSlot", out _, abs, count, itemId)) Native.Call(world, "SellSlot", abs, count);
        });
    }

    private static void ConfirmTrade(World world, bool buy, int itemId, int count, int unitPrice, Action send)
    {
        if (Busy(world) || !Native.Get<bool>(world, "_vendorShown")) return;
        var state = StateOf(world);
        int group = Native.Get<int>(world, "_vendorGroup"), npc = Native.Get<int>(world, "_vendorNpcId");
        string currency = Currency(world), name = ItemData.DisplayName(itemId);
        long total = (long)unitPrice * count;
        state.Confirming = true;
        void Cancel() => state.Confirming = false;
        void Accept()
        {
            if (!state.Confirming) return;
            Cancel();
            if (Native.Get<bool>(world, "_vendorShown") && Native.Get<int>(world, "_vendorGroup") == group
                && Native.Get<int>(world, "_vendorNpcId") == npc && !Busy(world)) send();
        }
        var panel = Native.Get<HudWindow>(world, "_vendorPanel");
        if (panel == null || !panel.HasMeta("vendor_confirmation"))
        {
            Accept();
            return;
        }
        var details = new Godot.Collections.Dictionary
        {
            { "title", buy ? "Buy item" : "Sell item" },
            { "message", $"{name}\nQuantity: {count:n0}\n{(buy ? "You pay" : "You receive")}: {total:n0} {currency}" },
            { "buy", buy }, { "item_id", itemId }, { "item_name", name }, { "quantity", count }, { "total", total }, { "currency", currency },
        };
        panel.GetMeta("vendor_confirmation").AsCallable().Call(details, Callable.From(Accept), Callable.From(Cancel));
    }

    private static string CanBuy(World world, int itemId, int count, int preferred)
    {
        var args = new object?[] { itemId, count, preferred, null, null, null };
        return Native.Call(world, "CanBuy", args) is true ? "" : args[5] as string ?? "";
    }

    private static bool Tradable(ItemSlot slot) =>
        !Native.Has(slot, "IsTradable") || Native.Get<bool>(slot, "IsTradable");

    private static int SellUnit(int itemId) => ItemData.IsSellable(itemId) ? ItemData.SellPrice(itemId) : 0;

    private static string Currency(World world) => Native.Get<string>(world, "VendorCurrency") ?? "gold";

    private static Inventory? Inventory(World world) => Native.Get<Inventory>(world, "Inv");

    private static bool InMainBag(World world, int abs) => Native.Call(world, "InMainBag", abs) is true;

    private static void Status(World world, string text) => Native.Get<FooterBand>(world, "_vendorFooter")?.Status(text, true);

    /// <summary>Replaces the client's handler of a C# event while keeping any other subscriber.</summary>
    internal static void ReplaceHandler<T>(object owner, string member, string nativeMethod, Action<T> handler)
    {
        var current = Native.Get<Delegate>(owner, member);
        var kept = current?.GetInvocationList().Where(d => d.Method.Name != nativeMethod).ToArray() ?? Array.Empty<Delegate>();
        Native.Set(owner, member, Delegate.Combine(kept.Append(handler).ToArray()));
    }

    /// <summary>Replaces every handler of a button's <c>pressed</c> signal.</summary>
    internal static void Rewire(BaseButton button, Action pressed)
    {
        foreach (var connection in button.GetSignalConnectionList(BaseButton.SignalName.Pressed))
            button.Disconnect(BaseButton.SignalName.Pressed, connection["callable"].AsCallable());
        button.Pressed += pressed;
    }
}

/// <summary>Stack count badges that also show "1" for a single countable item, on native item cells.</summary>
public static class NativeCountBadge
{
    public static void Apply(Control cell)
    {
        if (Native.Get<Label>(cell, "_count") is not { } count) return;
        ItemSlot item = cell switch
        {
            ItemSlotView view => view.Item,
            _ => Native.Get<ItemSlot>(cell, Native.Has(cell, "Item") ? "Item" : "Current"),
        };
        var def = item.IsEmpty ? null : ItemData.Get(item.ItemId);
        string text = item.IsEmpty ? "" : NativeUi.CountBadge(def, ItemData.ShownCount(def, item));
        if (count.Text != text) count.Text = text;
    }
}

/// <summary>
/// Reports every gold change in Info: income in green and spending in red, both signed. The client only
/// reports gold it picks up, so this handler runs ahead of the client's own and records the change itself.
/// </summary>
public static class NativeGoldLog
{
    public static readonly Color Income = new("79d892");
    public static readonly Color Expense = new("f07870");

    private static readonly ConditionalWeakTable<World, Action<int>> _handlers = new();

    public static void Attach(World world)
    {
        if (_handlers.TryGetValue(world, out _) || Net.I is not { } net) return;
        Action<int> handler = total => Changed(world, total);
        _handlers.Add(world, handler);
        var current = Native.Get<Delegate>(net, "GoldChangeEvent");
        if (!Native.Set(net, "GoldChangeEvent", Delegate.Combine(handler, current))) return;
        world.TreeExiting += () => Native.Set(net, "GoldChangeEvent", Delegate.Remove(Native.Get<Delegate>(net, "GoldChangeEvent"), handler));
    }

    /// <summary>Applies a new gold total before the client sees it, so the client records no change of its own.</summary>
    public static void Changed(World world, int total)
    {
        var sheet = Net.I.Sheet;
        long change = (long)total - sheet.Gold;
        if (change == 0) return;
        sheet.SetGold(total);
        if (change > 0 && Native.Get<object>(world, "Floaters") is { } floaters) Native.Call(floaters, "Gold", change);
        Native.Call(world, "PluginLogAdd", GameLogKind.Item,
            change > 0 ? $"You received +{change:n0} gold." : $"You spent −{-change:n0} gold.", change > 0 ? Income : Expense);
    }
}
