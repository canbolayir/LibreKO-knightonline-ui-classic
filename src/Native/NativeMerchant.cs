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

    private static readonly ConditionalWeakTable<World, State> _states = new();

    private static State StateOf(World world) => _states.GetOrCreateValue(world);

    private sealed class CellInput
    {
        public Action<int>? Activate;
        public int Index, DragsAtPress;
    }

    private static readonly ConditionalWeakTable<Control, CellInput> _cells = new();
    private static int _drags;

    public static CanvasLayer? AdvertLayer(World world) => StateOf(world).Advert;

    public static void Prepare(HudWindow window, World world)
    {
        if (window.HasMeta("native_merchant")) return;
        window.SetMeta("native_merchant", true);
        PrepareWorld(world);
        switch (window.Id)
        {
            case "sellstall":
                Cells(world, "_sellStallCells", index => Native.Call(world, "UnstageStallItem", index), (bag, slot) => StageStallItemAt(world, bag, slot));
                Cells(world, "_sellBagCells", index => Native.Call(world, "StageStallItem", index));
                if (Native.Descendants(window.Body).OfType<Button>().FirstOrDefault(b => b.Text == "Confirm") is { } confirm)
                    NativeVendor.Rewire(confirm, () => ConfirmSellStall(world));
                window.VisibilityChanged += () =>
                {
                    if (window.Visible) return;
                    if (StateOf(world).Advert is { } advert) advert.Visible = false;
                    Native.Call(world, "CloseAmountPrompt");
                };
                break;
            case "shop":
                Cells(world, "_shopCells");
                Cells(world, "_shopBagCells");
                break;
            case "wishlist":
                Cells(world, "_wishCells");
                break;
            case "wishfind":
                window.VisibilityChanged += () => { if (!window.Visible) Native.Call(world, "CloseAmountPrompt"); };
                break;
            case "wantedstall":
                Cells(world, "_wantedCells", null, (bag, wanted) => SellToWanted(world, bag, wanted));
                Cells(world, "_wantedBagCells", bag => SellToFirstOrder(world, bag));
                break;
        }
        NativeWindows.Sync(window, () =>
        {
            foreach (var cell in Native.Descendants(window.Body).OfType<PanelContainer>().Where(c => c.GetType().Name == "MerchantCell"))
                NativeCountBadge.Apply(cell);
        });
    }

    /// <summary>The amount prompt and advertisement layers and the stall signs, shared by every merchant window.</summary>
    public static void PrepareWorld(World world)
    {
        var state = StateOf(world);
        if (state.Prepared) return;
        state.Prepared = true;
        PrepareAmount(world);
        BuildAdvert(world, state);
        WatchSigns(world);
        Native.Get<CanvasLayer>(world, "_mctLayer")?.AddChild(new NativeMerchantDragWatch { Name = "native_merchant_drags" });
    }

    /// <summary>Names the amount prompt for the Classic prompt skin and validates its quantity and price.</summary>
    public static void PrepareAmount(World world)
    {
        if (Native.Get<CanvasLayer>(world, "_amountLayer") is not { } layer || layer.HasMeta("merchant_amount")) return;
        layer.SetMeta("merchant_amount", true);
        layer.SetMeta("merchant_error", "");
        if (Native.Get<Control>(world, "_amountMarketRow") is { } row)
        {
            row.Name = "merchant_market_row";
            if (row.GetChildren().OfType<Button>().FirstOrDefault() is { } history) history.Name = "merchant_market_history";
        }
        NativeWindows.Name(Native.Get<Label>(world, "_amountMarketHint"), "merchant_market_hint");
        if (Native.Get<Button>(world, "_amountConfirmBtn") is { } confirm) NativeVendor.Rewire(confirm, () => AcceptAmount(world));
        layer.VisibilityChanged += () =>
        {
            if (!layer.Visible) return;
            layer.SetMeta("merchant_error", "");
            SyncAmount(world);
            // The market price row is decided after the prompt becomes visible.
            Callable.From(() => SyncAmount(world)).CallDeferred();
        };
        layer.AddChild(new NativeMerchantSync(() => SyncAmount(world)) { Name = "native_merchant_amount" });
        SyncAmount(world);
    }

    private static void SyncAmount(World world)
    {
        if (Native.Get<CanvasLayer>(world, "_amountLayer") is not { } layer) return;
        // The Classic prompt moves and hides the price editor itself; the fixed price label is untouched.
        layer.SetMeta("merchant_price_editable", Native.Get<Control>(world, "_amountPriceFixed")?.Visible == false);
        layer.SetMeta("merchant_quantity", Native.Get<Control>(world, "_amountCountRow")?.Visible == true);
        layer.SetMeta("merchant_market_available", Native.Get<Control>(world, "_amountMarketRow")?.Visible == true);
        layer.SetMeta("merchant_market_open", Native.Get<bool>(world, "_marketPriceShown"));
        // Opening or closing price history changes which keys the prompt may use in the same frame.
        if (Native.Get<HudWindow>(world, "_marketPricePanel") is { } market && !market.HasMeta("native_merchant_market"))
        {
            market.SetMeta("native_merchant_market", true);
            market.VisibilityChanged += () => SyncAmount(world);
        }
    }

    private static void AcceptAmount(World world)
    {
        if (Native.Get<CanvasLayer>(world, "_amountLayer") is not { } layer) return;
        SyncAmount(world);
        var count = Native.Get<SpinBox>(world, "_amountCount");
        int amount = 1;
        if (layer.GetMeta("merchant_quantity", false).AsBool() && count != null
            && (!int.TryParse(count.GetLineEdit().Text.Trim(), out amount) || amount < 1 || amount > count.MaxValue))
        {
            layer.SetMeta("merchant_error", "Enter a valid quantity.");
            return;
        }
        if (layer.GetMeta("merchant_price_editable", false).AsBool() && Native.Get<MoneyEdit>(world, "_amountPrice")?.Value < 1)
        {
            layer.SetMeta("merchant_error", "Enter a valid price.");
            return;
        }
        if (layer.GetMeta("merchant_quantity", false).AsBool() && count != null) count.Value = amount;
        Native.Call(world, "AcceptAmount");
    }

    private static void BuildAdvert(World world, State state)
    {
        if (Native.Get<LineEdit>(world, "_sellAdvert") is not { } edit) return;
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
    public static void ConfirmSellStall(World world)
    {
        var state = StateOf(world);
        bool staged = Native.Get<MerchantStallItem[]>(world, "_myStall")?.Any(item => !item.IsEmpty) == true;
        bool classic = Native.Get<HudWindow>(world, "_sellStallPanel")?.HasMeta("classic_merchant") == true;
        if (staged && classic && state.Advert != null && !state.AdvertAccepted)
        {
            state.Advert.Visible = true;
            return;
        }
        state.AdvertAccepted = false;
        if (state.Advert != null) state.Advert.Visible = false;
        Native.Call(world, "ConfirmSellStall");
    }

    /// <summary>Lists a bag item in the listing slot it was dropped on, or the first free one.</summary>
    public static void StageStallItemAt(World world, int gridIndex, int preferredSlot)
    {
        var stall = Native.Get<MerchantStallItem[]>(world, "_myStall");
        var sources = Native.Get<int[]>(world, "_myStallSrc");
        if (stall == null || sources == null || Native.Get<Inventory>(world, "Inv") is not { } inv) return;
        if (gridIndex < 0 || gridIndex >= Inventory.GridCount || preferredSlot >= stall.Length) return;
        int absSlot = Inventory.GridStart + gridIndex;
        if (absSlot >= inv.Length || inv[absSlot].IsEmpty) return;
        if (Array.IndexOf(sources, absSlot) >= 0)
        {
            Native.Call(world, "SetSellStatus", "That one is already on the stall.", true);
            return;
        }
        int dst = preferredSlot;
        if (dst >= 0 && !stall[dst].IsEmpty)
        {
            Native.Call(world, "SetSellStatus", "That listing slot is occupied.", true);
            return;
        }
        if (dst < 0) dst = Array.FindIndex(stall, item => item.IsEmpty);
        if (dst < 0)
        {
            Native.Call(world, "SetSellStatus", $"The stall only holds {stall.Length} items.", true);
            return;
        }
        Native.Call(world, "AskStallPrice", inv[absSlot], absSlot, dst);
    }

    /// <summary>Sells to a buying stall; the full stack is offered and the payout cannot exceed the gold cap.</summary>
    public static void SellToWanted(World world, int gridIndex, int wantedSlot)
    {
        var layer = Native.Get<CanvasLayer>(world, "_amountLayer");
        var accept = Native.Get<Delegate>(world, "_amountAccept");
        Native.Call(world, "SellToWanted", gridIndex, wantedSlot);
        if (layer == null || !layer.Visible || ReferenceEquals(accept, Native.Get<Delegate>(world, "_amountAccept"))) return;
        var wanted = Native.Get<MerchantStallItem[]>(world, "_wantedItems");
        if (wanted == null || wantedSlot < 0 || wantedSlot >= wanted.Length || wanted[wantedSlot].Price < 1) return;
        int capacity = (CoinMax - Net.I.Sheet.Gold) / wanted[wantedSlot].Price;
        if (capacity < 1)
        {
            Native.Call(world, "CloseAmountPrompt");
            Native.Call(world, "SetWantedStatus", "You cannot carry any more gold.", true);
            return;
        }
        if (Native.Get<SpinBox>(world, "_amountCount") is { } count)
        {
            count.MaxValue = Math.Max(1, Math.Min(count.MaxValue, capacity));
            count.Value = count.MaxValue;
        }
    }

    private static void SellToFirstOrder(World world, int gridIndex)
    {
        if (Native.Get<Inventory>(world, "Inv") is not { } inv || Native.Get<MerchantStallItem[]>(world, "_wantedItems") is not { } wanted) return;
        int abs = Inventory.GridStart + gridIndex;
        if (abs >= inv.Length) return;
        int order = Array.FindIndex(wanted, item => !item.IsEmpty && item.ItemId == inv[abs].ItemId);
        if (order >= 0) SellToWanted(world, gridIndex, order);
    }

    /// <summary>Puts release activation over a merchant grid and routes its drops through the plugin.</summary>
    private static void Cells(World world, string field, Action<int>? activate = null, Action<int, int>? drop = null)
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
    private static void WatchSigns(World world)
    {
        if (Native.Get<Control>(world, "_stallSignLayer") is not { } signs) return;
        NativeWindows.OnDescendantAdded(signs, node =>
        {
            if (node is not PanelContainer sign || sign.GetParent() != signs) return;
            foreach (var stall in Stalls(world))
                if (ReferenceEquals(Native.Get<PanelContainer>(stall.Stall, "Sign"), sign)) Mark(sign, stall.Stall);
        });
        signs.AddChild(new NativeMerchantSync(() =>
        {
            foreach (var (charId, stall) in Stalls(world).ToArray())
            {
                if (Native.Get<PanelContainer>(stall, "Sign") is not { } sign || !GodotObject.IsInstanceValid(sign)) continue;
                if (!sign.HasMeta("merchant_buying")) Mark(sign, stall);
                if (sign.GetMeta("merchant_buying").AsBool() != Native.Get<bool>(stall, "IsBuying"))
                {
                    Native.Call(typeof(World), "FreeStallSign", stall);
                    Native.Call(world, "RefreshStallSign", charId, stall);
                    continue;
                }
                foreach (var cell in Native.Get<Array>(stall, "SignCells")?.OfType<Control>() ?? Enumerable.Empty<Control>())
                    NativeCountBadge.Apply(cell);
            }
        }) { Name = "native_merchant_signs" });
    }

    private static void Mark(PanelContainer sign, object stall)
    {
        sign.SetMeta("merchant_buying", Native.Get<bool>(stall, "IsBuying"));
        if (!sign.IsInGroup("merchant_signs")) sign.AddToGroup("merchant_signs");
    }

    private static IEnumerable<(int CharId, object Stall)> Stalls(World world)
    {
        if (Native.Get<IDictionary>(world, "_stalls") is not { } stalls) yield break;
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

/// <summary>Runs a merchant synchronisation every frame ahead of the Classic controls.</summary>
public partial class NativeMerchantSync : Node
{
    private readonly Action? _sync;

    public NativeMerchantSync() { }

    public NativeMerchantSync(Action sync)
    {
        _sync = sync;
        ProcessPriority = int.MinValue;
    }

    public override void _Process(double delta) => _sync?.Invoke();
}
