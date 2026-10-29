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
    static partial void RegisterMerchant(PluginContext context)
    {
        foreach (var id in NativeMerchant.WindowIds) NativeWindows.Prepare(id, NativeMerchant.Prepare);
    }
}

/// <summary>
/// The Classic player merchant on top of the client's stalls: the advertisement step before a selling
/// stall opens, the amount prompt metas the skin reads, release activation and drop targets on the
/// merchant cells, the seller's gold cap for buying stalls, and the selling/buying stall signs.
/// </summary>
public static class NativeMerchant
{
    public static readonly string[] WindowIds = { "merchantmenu", "sellstall", "shop", "wishlist", "wishfind", "wantedstall" };

    /// <summary>The most gold a character can carry; a sale to a buying stall cannot exceed it.</summary>
    public const int CoinMax = 2_100_000_000;

    private sealed class State
    {
        public bool Prepared, AdvertAccepted;
        public CanvasLayer? Advert;
    }

    private static readonly ConditionalWeakTable<WorldRoot, State> _states = new();

    private static State StateOf(WorldRoot world) => _states.GetOrCreateValue(world);

    private sealed class CellInput
    {
        public Action<int>? Activate;
        public int Index, DragsAtPress;
    }

    private static readonly ConditionalWeakTable<Control, CellInput> _cells = new();
    private static int _drags;

    public static CanvasLayer? AdvertLayer(WorldRoot world) => StateOf(world).Advert;

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("native_merchant")) return;
        window.SetMeta("native_merchant", true);
        PrepareWorld(world);
        switch (window.Id)
        {
            case "sellstall":
                Cells(world, "Merchant.SellStallCells", index => Native.Call(world, "Merchant.UnstageStallItem", index), (bag, slot) => StageStallItemAt(world, bag, slot));
                Cells(world, "Merchant.sellBagCells", index => Native.Call(world, "Merchant.StageStallItem", index));
                if (Native.Descendants(window.Body).OfType<Button>().FirstOrDefault(b => b.Text == "Confirm") is { } confirm)
                    NativeVendor.Rewire(confirm, () => ConfirmSellStall(world));
                window.VisibilityChanged += () =>
                {
                    if (window.Visible) return;
                    if (StateOf(world).Advert is { } advert) advert.Visible = false;
                    Native.Call(world, "Merchant.CloseAmountPrompt");
                };
                break;
            case "shop":
                Cells(world, "Merchant.shopCells");
                Cells(world, "Merchant.shopBagCells");
                break;
            case "wishlist":
                Cells(world, "MerchantBuy.wishCells");
                break;
            case "wishfind":
                window.VisibilityChanged += () => { if (!window.Visible) Native.Call(world, "Merchant.CloseAmountPrompt"); };
                break;
            case "wantedstall":
                Cells(world, "MerchantBuy.wantedCells", null, (bag, wanted) => SellToWanted(world, bag, wanted));
                Cells(world, "MerchantBuy.wantedBagCells", bag => SellToFirstOrder(world, bag));
                break;
        }
        NativeWindows.Sync(window, () =>
        {
            foreach (var cell in Native.Descendants(window.Body).OfType<PanelContainer>().Where(c => c.GetType().Name == "MerchantCell"))
                NativeCountBadge.Apply(cell);
        });
    }

    /// <summary>The amount prompt and advertisement layers and the stall signs, shared by every merchant window.</summary>
    public static void PrepareWorld(WorldRoot world)
    {
        var state = StateOf(world);
        if (state.Prepared) return;
        state.Prepared = true;
        PrepareAmount(world);
        BuildAdvert(world, state);
        WatchSigns(world);
        Native.Get<CanvasLayer>(world, "Merchant.ServiceLayer")?.AddChild(new NativeMerchantDragWatch { Name = "native_merchant_drags" });
    }

    /// <summary>Names the amount prompt for the Classic prompt skin and validates its quantity and price.</summary>
    public static void PrepareAmount(WorldRoot world)
    {
        if (Native.Get<CanvasLayer>(world, "Merchant.AmountLayer") is not { } layer || layer.HasMeta("merchant_amount")) return;
        layer.SetMeta("merchant_amount", true);
        layer.SetMeta("merchant_error", "");
        if (Native.Get<Control>(world, "Merchant.amountMarketRow") is { } row)
        {
            row.Name = "merchant_market_row";
            if (row.GetChildren().OfType<Button>().FirstOrDefault() is { } history) history.Name = "merchant_market_history";
        }
        NativeWindows.Name(Native.Get<Label>(world, "Merchant.amountMarketHint"), "merchant_market_hint");
        if (Native.Get<Button>(world, "Merchant.amountConfirmBtn") is { } confirm) NativeVendor.Rewire(confirm, () => AcceptAmount(world));
        layer.VisibilityChanged += () =>
        {
            if (!layer.Visible) return;
            layer.SetMeta("merchant_error", "");
            SyncAmount(world);
            // The market price row is decided after the prompt becomes visible.
            Callable.From(() => SyncAmount(world)).CallDeferred();
        };
        layer.AddChild(new NativeMerchantSync(() => SyncAmount(world), layer) { Name = "native_merchant_amount" });
        SyncAmount(world);
    }

    private static void SyncAmount(WorldRoot world)
    {
        if (Native.Get<CanvasLayer>(world, "Merchant.AmountLayer") is not { } layer) return;
        // The Classic prompt moves and hides the price editor itself; the fixed price label is untouched.
        layer.SetMeta(HotNames.MerchantPriceEditable, Native.Get<Control>(world, "Merchant.amountPriceFixed")?.Visible == false);
        layer.SetMeta(HotNames.MerchantQuantity, Native.Get<Control>(world, "Merchant.amountCountRow")?.Visible == true);
        layer.SetMeta(HotNames.MerchantMarketAvailable, Native.Get<Control>(world, "Merchant.amountMarketRow")?.Visible == true);
        layer.SetMeta(HotNames.MerchantMarketOpen, Native.Get<bool>(world, "MarketPrice.marketPriceShown"));
        // Opening or closing price history changes which keys the prompt may use in the same frame.
        if (Native.Get<HudWindow>(world, "MarketPrice.MarketPricePanel") is { } market && !market.HasMeta(HotNames.NativeMerchantMarket))
        {
            market.SetMeta(HotNames.NativeMerchantMarket, true);
            market.VisibilityChanged += () => SyncAmount(world);
        }
    }

    private static void AcceptAmount(WorldRoot world)
    {
        if (Native.Get<CanvasLayer>(world, "Merchant.AmountLayer") is not { } layer) return;
        SyncAmount(world);
        var count = Native.Get<SpinBox>(world, "Merchant.amountCount");
        int amount = 1;
        if (layer.GetMeta("merchant_quantity", false).AsBool() && count != null
            && (!int.TryParse(count.GetLineEdit().Text.Trim(), out amount) || amount < 1 || amount > count.MaxValue))
        {
            layer.SetMeta("merchant_error", "Enter a valid quantity.");
            return;
        }
        if (layer.GetMeta("merchant_price_editable", false).AsBool() && Native.Get<MoneyEdit>(world, "Merchant.AmountPrice")?.Value < 1)
        {
            layer.SetMeta("merchant_error", "Enter a valid price.");
            return;
        }
        if (layer.GetMeta("merchant_quantity", false).AsBool() && count != null) count.Value = amount;
        Native.Call(world, "Merchant.AcceptAmount");
    }

    private static void BuildAdvert(WorldRoot world, State state)
    {
        if (Native.Get<LineEdit>(world, "Merchant.SellAdvert") is not { } edit) return;
        var layer = new CanvasLayer { Name = "merchant_advert", Layer = 77, Visible = false };
        layer.SetMeta("merchant_advert", true);
        layer.SetMeta("merchant_advert_edit", edit);
        world.AddChild(layer);
        var blocker = new ColorRect { Color = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Stop };
        blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(blocker);
        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(centre);
        var box = new VBoxContainer();
        centre.AddChild(box);
        var ok = new Button { Text = "OK", FocusMode = Control.FocusModeEnum.None };
        ok.Pressed += () => { state.AdvertAccepted = true; ConfirmSellStall(world); };
        box.AddChild(ok);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += () => layer.Visible = false;
        box.AddChild(cancel);
        state.Advert = layer;
    }

    /// <summary>Opening a Classic selling stall first asks for its advertisement.</summary>
    public static void ConfirmSellStall(WorldRoot world)
    {
        var state = StateOf(world);
        bool staged = Native.Get<MerchantStallItem[]>(world, "Merchant.myStall")?.Any(item => !item.IsEmpty) == true;
        bool classic = Native.Get<HudWindow>(world, "Merchant.SellStallPanel")?.HasMeta("classic_merchant") == true;
        if (staged && classic && state.Advert != null && !state.AdvertAccepted)
        {
            state.Advert.Visible = true;
            return;
        }
        state.AdvertAccepted = false;
        if (state.Advert != null) state.Advert.Visible = false;
        Native.Call(world, "Merchant.ConfirmSellStall");
    }

    /// <summary>Lists a bag item in the listing slot it was dropped on, or the first free one.</summary>
    public static void StageStallItemAt(WorldRoot world, int gridIndex, int preferredSlot)
    {
        var stall = Native.Get<MerchantStallItem[]>(world, "Merchant.myStall");
        var sources = Native.Get<int[]>(world, "Merchant.myStallSrc");
        if (stall == null || sources == null || Game.Session.Self.Inventory is not { } inv) return;
        if (gridIndex < 0 || gridIndex >= Inventory.GridCount || preferredSlot >= stall.Length) return;
        int absSlot = Inventory.GridStart + gridIndex;
        if (absSlot >= inv.Length || inv[absSlot].IsEmpty) return;
        if (Array.IndexOf(sources, absSlot) >= 0)
        {
            Native.Call(world, "Merchant.SetSellStatus", "That one is already on the stall.", true);
            return;
        }
        int dst = preferredSlot;
        if (dst >= 0 && !stall[dst].IsEmpty)
        {
            Native.Call(world, "Merchant.SetSellStatus", "That listing slot is occupied.", true);
            return;
        }
        if (dst < 0) dst = Array.FindIndex(stall, item => item.IsEmpty);
        if (dst < 0)
        {
            Native.Call(world, "Merchant.SetSellStatus", $"The stall only holds {stall.Length} items.", true);
            return;
        }
        Native.Call(world, "Merchant.AskStallPrice", inv[absSlot], absSlot, dst);
    }

    /// <summary>Sells to a buying stall; the full stack is offered and the payout cannot exceed the gold cap.</summary>
    public static void SellToWanted(WorldRoot world, int gridIndex, int wantedSlot)
    {
        var layer = Native.Get<CanvasLayer>(world, "Merchant.AmountLayer");
        var accept = Native.Get<Delegate>(world, "Merchant.amountAccept");
        Native.Call(world, "MerchantBuy.SellToWanted", gridIndex, wantedSlot);
        if (layer == null || !layer.Visible || ReferenceEquals(accept, Native.Get<Delegate>(world, "Merchant.amountAccept"))) return;
        var wanted = Native.Get<MerchantStallItem[]>(world, "MerchantBuy.wantedItems");
        if (wanted == null || wantedSlot < 0 || wantedSlot >= wanted.Length || wanted[wantedSlot].Price < 1) return;
        int capacity = (CoinMax - Game.Session.Self.Sheet.Gold) / wanted[wantedSlot].Price;
        if (capacity < 1)
        {
            Native.Call(world, "Merchant.CloseAmountPrompt");
            Native.Call(world, "MerchantBuy.SetWantedStatus", "You cannot carry any more gold.", true);
            return;
        }
        if (Native.Get<SpinBox>(world, "Merchant.amountCount") is { } count)
        {
            count.MaxValue = Math.Max(1, Math.Min(count.MaxValue, capacity));
            count.Value = count.MaxValue;
        }
    }

    private static void SellToFirstOrder(WorldRoot world, int gridIndex)
    {
        if (Game.Session.Self.Inventory is not { } inv || Native.Get<MerchantStallItem[]>(world, "MerchantBuy.wantedItems") is not { } wanted) return;
        int abs = Inventory.GridStart + gridIndex;
        if (abs >= inv.Length) return;
        int order = Array.FindIndex(wanted, item => !item.IsEmpty && item.ItemId == inv[abs].ItemId);
        if (order >= 0) SellToWanted(world, gridIndex, order);
    }

    /// <summary>Puts release activation over a merchant grid and routes its drops through the plugin.</summary>
    private static void Cells(WorldRoot world, string field, Action<int>? activate = null, Action<int, int>? drop = null)
    {
        if (Native.Get<Array>(world, field) is not { } cells) return;
        foreach (var cell in cells.OfType<Control>())
        {
            var native = Native.Get<Action<int>>(cell, "OnActivate");
            Native.Set(cell, "OnActivate", null);
            if (drop != null) Native.Set(cell, "OnDropFrom", drop);
            if (_cells.TryGetValue(cell, out _)) continue;
            var input = new CellInput { Activate = activate ?? native, Index = Native.Get<int>(cell, "Index") };
            _cells.Add(cell, input);
            cell.GuiInput += ev => CellGuiInput(cell, input, ev);
        }
    }

    /// <summary>
    /// Activates a merchant cell on right press, or on a left release that did not start a drag, so a left
    /// drag can begin without opening a prompt. A release after a drag reaches the drop target instead.
    /// </summary>
    private static void CellGuiInput(Control cell, CellInput input, InputEvent ev)
    {
        if (ev is not InputEventMouseButton click) return;
        if (click.ButtonIndex == MouseButton.Left && click.Pressed)
        {
            input.DragsAtPress = _drags;
            return;
        }
        if (input.Activate != null && (click.ButtonIndex == MouseButton.Right && click.Pressed
            || click.ButtonIndex == MouseButton.Left && !click.Pressed && input.DragsAtPress == _drags))
        {
            input.Activate(input.Index);
            if (cell.IsInsideTree()) cell.AcceptEvent();
        }
    }

    internal static void DragBegan() => _drags++;

    /// <summary>Marks stall signs as selling or buying and rebuilds a sign when the stall switches.</summary>
    private static void WatchSigns(WorldRoot world)
    {
        if (Native.Get<Control>(world, "MerchantStalls.stallSignLayer") is not { } signs) return;
        NativeWindows.OnDescendantAdded(signs, node =>
        {
            if (node is not PanelContainer sign || sign.GetParent() != signs) return;
            foreach (var stall in Stalls(world))
                if (ReferenceEquals(Native.Get<PanelContainer>(stall.Stall, "Sign"), sign)) Mark(sign, stall.Stall);
        });
        bool queued = false;
        void Signs()
        {
            foreach (var (charId, stall) in Stalls(world).ToArray())
            {
                if (Native.Get<PanelContainer>(stall, "Sign") is not { } sign || !GodotObject.IsInstanceValid(sign)) continue;
                if (!sign.HasMeta(BuyingMeta)) Mark(sign, stall);
                if (sign.GetMeta(BuyingMeta).AsBool() != Native.Get<bool>(stall, "IsBuying"))
                {
                    Native.Call(Native.StaticOwner("LibreKO.Features.Windows.MerchantStallsFeature"), "FreeStallSign", stall);
                    Native.Call(world, "MerchantStalls.RefreshStallSign", charId, stall);
                    continue;
                }
                if (_watchedSigns.TryAdd(sign, sign))
                    sign.VisibilityChanged += () => { if (GodotObject.IsInstanceValid(sign) && sign.IsVisibleInTree()) Shown(); };
                // A hidden sign draws nothing; its badges follow when the client shows it again.
                if (!sign.IsVisibleInTree()) continue;
                foreach (var cell in Native.Get<Array>(stall, "SignCells")?.OfType<Control>() ?? Enumerable.Empty<Control>())
                    NativeCountBadge.Apply(cell);
            }
        }
        // The client shows and hides signs as they move on screen; restyle the badges before the frame draws.
        void Shown()
        {
            if (queued) return;
            queued = true;
            Callable.From(() => { queued = false; if (Plugin.Active && GodotObject.IsInstanceValid(world)) Signs(); }).CallDeferred();
        }
        signs.VisibilityChanged += () => { if (signs.IsVisibleInTree()) Shown(); };
        signs.AddChild(new NativeMerchantSync(Signs) { Name = "native_merchant_signs" });
    }

    private static readonly StringName BuyingMeta = "merchant_buying";
    private static readonly StringName SignsGroup = "merchant_signs";
    private static readonly ConditionalWeakTable<Control, Control> _watchedSigns = new();

    private static void Mark(PanelContainer sign, object stall)
    {
        sign.SetMeta(BuyingMeta, Native.Get<bool>(stall, "IsBuying"));
        if (!sign.IsInGroup(SignsGroup)) sign.AddToGroup(SignsGroup);
    }

    private static IEnumerable<(int CharId, object Stall)> Stalls(WorldRoot world)
    {
        if (Native.Get<IDictionary>(world, "MerchantStalls.Stalls") is not { } stalls) yield break;
        foreach (DictionaryEntry entry in stalls)
            if (entry.Value != null) yield return ((int)entry.Key, entry.Value);
    }
}

/// <summary>Notes every drag that begins, so a merchant cell does not also activate on its release.</summary>
public partial class NativeMerchantDragWatch : Node
{
    public override void _Notification(int what)
    {
        if (what == Control.NotificationDragBegin) NativeMerchant.DragBegan();
    }
}

/// <summary>
/// Runs a merchant synchronisation every frame ahead of the Classic controls; with a layer, only while that layer
/// is shown (the layer's own show handler syncs it when it opens).
/// </summary>
public partial class NativeMerchantSync : Node
{
    private readonly Action? _sync;
    private readonly CanvasLayer? _layer;

    public NativeMerchantSync() { }

    public NativeMerchantSync(Action sync, CanvasLayer? layer = null)
    {
        _sync = sync;
        _layer = layer;
        ProcessPriority = int.MinValue;
    }

    public override void _Ready()
    {
        if (_layer == null) return;
        _layer.VisibilityChanged += Follow;
        Follow();
    }

    private void Follow()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) SetProcess(_layer.Visible);
    }

    public override void _Process(double delta) => _sync?.Invoke();
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName MerchantMarketAvailable = "merchant_market_available";
    internal static readonly StringName MerchantMarketOpen = "merchant_market_open";
    internal static readonly StringName MerchantPriceEditable = "merchant_price_editable";
    internal static readonly StringName MerchantQuantity = "merchant_quantity";
    internal static readonly StringName NativeMerchantMarket = "native_merchant_market";
}
