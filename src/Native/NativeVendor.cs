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
    static partial void RegisterVendor(PluginContext context)
    {
        NativeWindows.Prepare("vendor", NativeVendor.Prepare);
        // Kaishan's buy-back window belongs to the shop family.
        NativeWindows.Prepare(NativeRepurchase.WindowId, NativeRepurchase.Prepare);
    }
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

    private static readonly ConditionalWeakTable<WorldRoot, State> _states = new();

    private static State StateOf(WorldRoot world) => _states.GetOrCreateValue(world);

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("native_vendor")) return;
        window.SetMeta("native_vendor", true);
        window.SetMeta("vendor_blocked", Callable.From(() => Blocked(world)));
        window.SetMeta("vendor_dead", Callable.From(() => Native.Get<bool>(world, "Avatar.Dead")));
        window.SetMeta("vendor_group", Native.Get<int>(world, "Vendor.group"));

        if (Native.Get<ItemSlotView[]>(world, "Vendor.Cells") is { } cells)
            foreach (var cell in cells)
            {
                var drag = cell.DragOut;
                cell.DragOut = view => DragOut(world, view, drag);
                ReplaceHandler<ItemSlotView>(cell, "RightClicked", "OnCellBuy", view => CellBuy(world, view));
                ReplaceHandler<ItemSlotView>(cell, "DoubleClicked", "OnCellBuy", view => CellBuy(world, view));
                cell.Dropped = (_, data) => SellDrop(world, data);
            }
        foreach (var well in Native.Descendants(window.Body).OfType<DropWell>())
            well.Dropped = data => SellDrop(world, data);
        if (Native.Get<Button>(world, "Vendor.buy") is { } buy)
            Rewire(buy, () => AskBuy(world, Native.Get<int>(world, "Vendor.selected"), -1));

        NativeGoldLog.Attach(world);
        NativeWindows.Sync(window, () => Sync(window, world));
        // Runs after the client opened the shop, so the packed pages are in place before the first frame.
        if (Game.Connection is { } net)
        {
            Action<int> opened = _ => Sync(window, world);
            NativeHooks.Listen("Net.TradeNpcEvent", () => Game.Session.Vendor.TradeNpcEvent += opened, () => Game.Session.Vendor.TradeNpcEvent -= opened, world);
        }
    }

    /// <summary>A purchase, sale, approval or inventory move is pending.</summary>
    public static bool Blocked(WorldRoot world) =>
        Game.Session.Vendor.TradeInFlight || StateOf(world).Confirming || Native.Get<bool>(world, "InventoryWindow.moveInFlight")
        || Native.Get<ICollection>(world, "InventoryWindow.moveQueue") is { Count: > 0 };

    public static bool Confirming(WorldRoot world) => StateOf(world).Confirming;

    private static bool Busy(WorldRoot world) => Blocked(world) || Native.Get<bool>(world, "Avatar.Dead");

    /// <summary>Moves an inventory item unless a shop transaction is pending.</summary>
    public static void Move(WorldRoot world, int from, int to)
    {
        if (Busy(world)) return;
        Native.Call(world, "InventoryWindow.MoveBetween", from, to);
    }

    private static void Sync(HudWindow window, WorldRoot world)
    {
        window.SetMeta(HotNames.VendorGroup, Native.Get<int>(world, "Vendor.group"));
        CompactCatalogue(world);
        RouteCompanion(world);
        if (Native.Get<ItemSlotView[]>(world, "Vendor.Cells") is { } cells)
            foreach (var cell in cells) NativeCountBadge.Apply(cell);
    }

    /// <summary>Packs the products into consecutive pages; buy requests keep their source coordinates.</summary>
    private static void CompactCatalogue(WorldRoot world)
    {
        var state = StateOf(world);
        if (Native.Get<ShopCatalogue>(world, "Vendor.Catalogue") is not { } catalogue || ReferenceEquals(catalogue, state.Compacted)) return;
        var ids = catalogue.Pages.SelectMany(page => catalogue.Page(page).Where(id => id != 0)).ToArray();
        var compact = new ShopCatalogue(ids.Select((id, i) => new ShopCatalogue.Entry(id, i / ShopCatalogue.PageSize, i % ShopCatalogue.PageSize)),
            ItemData.DisplayName);
        state.Compacted = compact;
        if (!Native.Set(world, "Vendor.Catalogue", compact)) return;
        Native.Call(world, "Vendor.ShowPage", 0);
    }

    /// <summary>Routes the inventory's buy and sell gestures through the approval step.</summary>
    private static void RouteCompanion(WorldRoot world)
    {
        var state = StateOf(world);
        if (Native.Get<object>(world, "Vendor.companion") is not { } companion || ReferenceEquals(companion, state.Companion)) return;
        state.Companion = companion;
        Native.Set(companion, "Take", new Func<int, bool>(abs =>
        {
            if (!InMainBag(world, abs)) return false;
            AskSell(world, abs);
            return true;
        }));
        Func<int, int, bool> buy = (cell, abs) =>
        {
            var ids = Native.Get<int[]>(world, "Vendor.CellIds");
            if (ids == null || cell < 0 || cell >= ids.Length || ids[cell] == 0) return false;
            AskBuy(world, ids[cell], abs);
            return true;
        };
        // ff78eef: IntoBag also receives the amount picked with the mouse wheel; the buy approval asks for the count.
        Native.Set(companion, "IntoBag", Native.MemberType(companion, "IntoBag") == typeof(Func<int, int, int?, bool>)
            ? new Func<int, int, int?, bool>((cell, abs, _) => buy(cell, abs)) : buy);
    }

    private static Variant DragOut(WorldRoot world, ItemSlotView cell, Func<ItemSlotView, Variant>? native)
    {
        if (Busy(world) || native == null) return default;
        var data = native(cell);
        var ids = Native.Get<int[]>(world, "Vendor.CellIds");
        if (data.VariantType == Variant.Type.Dictionary && ids != null && cell.Index >= 0 && cell.Index < ids.Length)
            data.AsGodotDictionary()["id"] = ids[cell.Index];
        return data;
    }

    private static void CellBuy(WorldRoot world, ItemSlotView cell)
    {
        var ids = Native.Get<int[]>(world, "Vendor.CellIds");
        if (ids == null || cell.Index < 0 || cell.Index >= ids.Length || ids[cell.Index] == 0) return;
        Native.Call(world, "Vendor.SelectItem", ids[cell.Index]);
        AskBuy(world, ids[cell.Index], -1);
    }

    private static void SellDrop(WorldRoot world, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return;
        var payload = data.AsGodotDictionary();
        if (!payload.ContainsKey("invFrom")) return;
        int abs = payload["invFrom"].AsInt32();
        if (!InMainBag(world, abs)) return;
        if (payload.ContainsKey("id") && Inventory(world)?[abs].ItemId != payload["id"].AsInt32()) return;
        AskSell(world, abs);
    }

    public static void AskBuy(WorldRoot world, int itemId, int preferred)
    {
        if (Busy(world) || Native.Get<Dictionary<int, ItemData.SellEntry>>(world, "Vendor.entries") is not { } entries
            || !entries.TryGetValue(itemId, out var entry)) return;
        if (ItemData.Get(itemId) is not { } def) return;
        int price = ItemData.BuyPrice(itemId);
        var sheet = Game.Session.Self.Sheet;
        long? freeWeight = sheet.MaxWeight > 0 ? sheet.MaxWeight - Convert.ToInt64(Native.Call(world, "InventoryWindow.CarriedWeight")) : null;
        int room = Convert.ToInt32(Native.Call(world, "Vendor.BuyRoom", itemId, def, preferred));
        int max = BuyLimit.Max(Native.Get<long>(world, "Vendor.Wallet"), price, freeWeight, def.Weight, room);
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
        Native.Get<QuantityPrompt>(world, "Vendor.TradePrompt")?.Open(ItemIcons.Get(itemId), $"Buy {ItemData.DisplayName(itemId)}",
            $"{price:n0} {currency} each · up to {max:n0}", max, 1, n => ConfirmBuy(world, entry, (int)n, preferred),
            "Buy", n => $"Total {(long)price * n:n0} {currency} · {(long)def.Weight * n / 10f:0.0} wt");
    }

    private static void ConfirmBuy(WorldRoot world, ItemData.SellEntry entry, int count, int preferred)
    {
        if (Busy(world) || count <= 0 || Native.Get<Dictionary<int, ItemData.SellEntry>>(world, "Vendor.entries")?.ContainsKey(entry.Id) != true) return;
        string problem = CanBuy(world, entry.Id, count, preferred);
        if (problem.Length > 0) { Status(world, problem); return; }
        ConfirmTrade(world, true, entry.Id, count, ItemData.BuyPrice(entry.Id), () => Native.Call(world, "Vendor.BuyAmount", entry, count, preferred));
    }

    public static void AskSell(WorldRoot world, int abs)
    {
        if (Busy(world) || !InMainBag(world, abs) || Inventory(world) is not { } inv || inv[abs].IsEmpty) return;
        if (Native.Call(world, "BagCompanions.RefuseItemInUse", abs, -1) is true) return;
        if (Native.Get<bool>(world, "Vendor.LoyaltyShop"))
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
        Native.Get<QuantityPrompt>(world, "Vendor.TradePrompt")?.Open(ItemIcons.Get(slot.ItemId), $"Sell {ItemData.DisplayName(slot.ItemId)}",
            $"{unit:n0} gold each · you carry {slot.Count:n0}", slot.Count, slot.Count, n => ConfirmSell(world, abs, (int)n, slot.ItemId),
            "Sell", n => $"Sells for {(long)unit * n:n0} gold");
    }

    private static void ConfirmSell(WorldRoot world, int abs, int count, int itemId)
    {
        if (Busy(world) || !InMainBag(world, abs) || Inventory(world) is not { } inv) return;
        if (inv[abs].ItemId != itemId || count <= 0 || inv[abs].Count < count) return;
        if (!Tradable(inv[abs]) || SellUnit(itemId) <= 0 || Native.Get<bool>(world, "Vendor.LoyaltyShop")) return;
        ConfirmTrade(world, false, itemId, count, SellUnit(itemId), () =>
        {
            if (inv[abs].ItemId != itemId || inv[abs].Count < count) { Status(world, StaleText); return; }
            if (!Native.TryCall(world, "Vendor.SellSlot", out _, abs, count, itemId)) Native.Call(world, "Vendor.SellSlot", abs, count);
        });
    }

    private static void ConfirmTrade(WorldRoot world, bool buy, int itemId, int count, int unitPrice, Action send)
    {
        if (Busy(world) || !Native.Get<bool>(world, "Vendor.shown")) return;
        var state = StateOf(world);
        int group = Native.Get<int>(world, "Vendor.group"), npc = Game.Session.NpcQuest.LastNpcId;
        string currency = Currency(world), name = ItemData.DisplayName(itemId);
        long total = (long)unitPrice * count;
        state.Confirming = true;
        void Cancel() => state.Confirming = false;
        void Accept()
        {
            if (!state.Confirming) return;
            Cancel();
            if (Native.Get<bool>(world, "Vendor.shown") && Native.Get<int>(world, "Vendor.group") == group
                && Game.Session.NpcQuest.LastNpcId == npc && !Busy(world)) send();
        }
        var panel = Native.Get<HudWindow>(world, "Vendor.Panel");
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

    private static string CanBuy(WorldRoot world, int itemId, int count, int preferred)
    {
        var args = new object?[] { itemId, count, preferred, null, null, null };
        return Native.Call(world, "Vendor.CanBuy", args) is true ? "" : args[5] as string ?? "";
    }

    private static bool Tradable(ItemSlot slot) =>
        !Native.Has(slot, "IsTradable") || Native.Get<bool>(slot, "IsTradable");

    private static int SellUnit(int itemId) => ItemData.IsSellable(itemId) ? ItemData.SellPrice(itemId) : 0;

    private static string Currency(WorldRoot world) => Native.Get<string>(world, "Vendor.Currency") ?? "gold";

    private static Inventory? Inventory(WorldRoot world) => Game.Session.Self.Inventory;

    private static bool InMainBag(WorldRoot world, int abs) => Game.Session.Self.Inventory.InMainBag(abs);

    private static void Status(WorldRoot world, string text) => Native.Get<FooterBand>(world, "Vendor.footer")?.Status(text, true);

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
        if (Native.Get<Label>(cell, Native.Has(cell, "countLabel") ? "countLabel" : "count") is not { } count) return;
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
/// Reports gold changes the client does not log itself in Info: income in green and spending in red,
/// both signed. Shop trades and server gold notices are logged by the client (its shop and coin lines),
/// and gold moved between your own bag, storages and trade offer is not income, so those are skipped.
/// </summary>
public static class NativeGoldLog
{
    public static readonly Color Income = new("79d892");
    public static readonly Color Expense = new("f07870");

    private static readonly ConditionalWeakTable<WorldRoot, Action<GoldChange>> _handlers = new();
    private static int? _clientLogged;

    public static void Attach(WorldRoot world)
    {
        if (_handlers.TryGetValue(world, out _)) return;
        Action<GoldChange> handler = change => Changed(world, change);
        Action<GoldChangeReply> logged = reply => _clientLogged = reply.Total;
        _handlers.Add(world, handler);
        NativeHooks.Listen("Progress.GoldChangeLogEvent", () => Game.Session.Progress.GoldChangeLogEvent += logged, () => Game.Session.Progress.GoldChangeLogEvent -= logged, world);
        NativeHooks.Listen("Progress.GoldChangeEvent", () => Game.Session.Progress.GoldChangeEvent += handler, () => Game.Session.Progress.GoldChangeEvent -= handler, world);
    }

    /// <summary>Logs a gold change unless the client logs it or it only moved between your own holdings.</summary>
    public static void Changed(WorldRoot world, GoldChange change)
    {
        bool clientLogs = _clientLogged == change.Total || Game.Session.Vendor.TradeInFlight;
        _clientLogged = null;
        if (change.Delta == 0 || change.Transfer || clientLogs) return;
        Native.Call(world, "PluginBridge.LogAdd", GameLogKind.Item,
            change.Delta > 0 ? $"You received +{change.Delta:n0} gold." : $"You spent −{-change.Delta:n0} gold.", change.Delta > 0 ? Income : Expense);
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName VendorGroup = "vendor_group";
}
