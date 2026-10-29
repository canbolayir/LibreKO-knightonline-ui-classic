using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterTrade(PluginContext context) => NativeWindows.Prepare("exchange", NativeTrade.Prepare);
}

/// <summary>
/// The Classic player trade on top of the client's exchange: the offers and the carried items the skin
/// shows in its own grids, the <c>ex_*</c> state metas it reads, named modal layers, the original request
/// message, a coin amount prompt, and a mouse-only final approval before the client's own confirmation is
/// sent. The client docks the inventory beside its trade window; the Classic trade carries its own
/// inventory grid, so the inventory the trade opened is closed again.
/// </summary>
public static class NativeTrade
{
    public const int OfferSlots = 12;
    public const string FinalQuestion = "Are you sure you want to trade?";

    private sealed class State
    {
        public CanvasLayer? Request, Final;
        public Label? RequestLabel, FinalLabel;
        public bool FinalPending, Gold;
        public (int Mine, int Theirs, int MyGold, int TheirGold) Offers;
    }

    private static readonly ConditionalWeakTable<WorldRoot, State> _states = new();

    private static State StateOf(WorldRoot world) => _states.GetOrCreateValue(world);

    private static readonly ConditionalWeakTable<HudWindow, WorldRoot> _worlds = new();

    /// <summary>The world whose exchange the trade window shows (also for a window kept outside the world).</summary>
    public static WorldRoot? WorldOf(HudWindow window) => _worlds.TryGetValue(window, out var world) && GodotObject.IsInstanceValid(world) ? world : Native.WorldOf(window);

    public static CanvasLayer? RequestLayer(WorldRoot world) => StateOf(world).Request;

    public static CanvasLayer? FinalLayer(WorldRoot world) => StateOf(world).Final;

    public static bool FinalPending(WorldRoot world) => StateOf(world).FinalPending;

    /// <summary>An item in an offer grid or in the carried inventory; <c>Slot</c> is the bag slot it came from.</summary>
    public readonly record struct TradeItem(int Slot, int ItemId, int Count, short Durability);

    /// <summary>The offered items in offer order (mine or the partner's).</summary>
    public static IReadOnlyList<TradeItem> Offers(WorldRoot world, bool mine)
    {
        var result = new List<TradeItem>();
        foreach (var offer in Native.Get<IEnumerable>(world, mine ? "Exchange.MyOffer" : "Exchange.TheirOffer") ?? Array.Empty<object>())
            result.Add(new TradeItem(Native.Get<int>(offer!, "SourceAbs"), Native.Get<int>(offer!, "ItemId"), Native.Get<int>(offer!, "Count"), Native.Get<short>(offer!, "Dura")));
        return result;
    }

    /// <summary>The carried inventory grid (bag slots), with the items still in the bag.</summary>
    public static IReadOnlyList<TradeItem> Bag(WorldRoot world)
    {
        var result = new List<TradeItem>();
        if (Game.Session.Self.Inventory is not { } inv) return result;
        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < inv.Length; abs++)
            if (!inv[abs].IsEmpty) result.Add(new TradeItem(abs, inv[abs].ItemId, inv[abs].Count, inv[abs].Durability));
        return result;
    }

    /// <summary>
    /// Offers a bag slot through the client (quantity prompt, refusals and pending state stay native). A part of the
    /// stack picked with the mouse wheel is offered without the prompt (ff78eef: <c>OfferSlot(int, int? amount)</c>).
    /// </summary>
    public static void Offer(WorldRoot world, int abs, int? part = null)
    {
        if (part is int amount && Native.TryCall(world, "Exchange.OfferSlot", out _, abs, (int?)amount)) return;
        Native.Call(world, "Exchange.OfferSlot", abs);
    }

    public static void Tooltip(WorldRoot world, TradeItem item, bool bag) =>
        Native.Call(world, "ItemTooltip.Show", bag ? item.Slot : -1, new ItemSlot { ItemId = item.ItemId, Count = (short)item.Count, Durability = item.Durability }, "");

    public static void HideTooltip(WorldRoot world) => Native.Call(world, "ItemTooltip.Hide");

    /// <summary>Closes the inventory window the client opened as the trade's bag companion.</summary>
    private static void KeepInventoryClosed(WorldRoot world)
    {
        if (!Native.Get<bool>(world, "Exchange.shown") || Native.Get<object>(world, "BagCompanions.Pairing") is not { } pairing) return;
        if (!Native.Get<bool>(pairing, "openedByCompanion")) return;
        Native.Set(pairing, "openedByCompanion", false);
        Native.Call(world, "MainWindows.Hide", "Inventory");
    }

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("native_exchange")) return;
        window.SetMeta("native_exchange", true);
        _worlds.AddOrUpdate(window, world);
        var state = StateOf(world);
        NativeWindows.Name(Native.Get<Label>(world, "Exchange.status"), "exchange_status");
        window.VisibilityChanged += () =>
        {
            if (window.Visible) Callable.From(() => { if (GodotObject.IsInstanceValid(world)) KeepInventoryClosed(world); }).CallDeferred();
            // The per-frame tick mirrors the window metas only while the window is shown; catch up before it draws.
            if (window.IsVisibleInTree()) Callable.From(() => { if (GodotObject.IsInstanceValid(world)) Sync(world); }).CallDeferred();
        };
        if (Native.Get<CanvasLayer>(world, "Exchange.waitLayer") is { } wait) { wait.Name = "exchange_wait"; wait.SetMeta("exchange_wait", true); }
        if (Native.Get<CanvasLayer>(world, "Exchange.amountLayer") is { } amount)
        {
            amount.Name = "exchange_amount";
            amount.SetMeta("exchange_amount", true);
            amount.SetMeta("ex_amount_error", "");
            amount.VisibilityChanged += () =>
            {
                if (!amount.Visible) { state.Gold = false; return; }
                amount.SetMeta("ex_amount_error", "");
                amount.SetMeta("ex_gold", state.Gold);
            };
            if (Native.Descendants(amount).OfType<Button>().FirstOrDefault(b => b.Text == "Offer") is { } offer)
                NativeVendor.Rewire(offer, () => ConfirmAmount(world));
        }

        state.Request = Layer(world, "exchange_request", 76, out state.RequestLabel,
            ("exchange_accept", () => Answer(world, true)), ("exchange_decline", () => Answer(world, false)));
        state.Final = Layer(world, "exchange_final", 77, out state.FinalLabel,
            ("exchange_final_accept", () => AcceptFinal(world)), ("exchange_final_decline", () => CloseFinal(world)));
        state.Final.AddChild(new NativeTradeSync(world) { Name = "native_trade_sync" });

        if (Native.Get<Button>(world, "Exchange.confirmBtn") is { } confirm) NativeVendor.Rewire(confirm, () => RequestFinal(world));
        if (Native.Descendants(window.Body).OfType<Button>().FirstOrDefault(b => b.Text == "Add gold") is { } gold)
            NativeVendor.Rewire(gold, () => OpenGold(world));

        // The original request message replaces the desktop dialog in the same call that opened it.
        Action<int> request = _ => Sync(world);
        NativeHooks.Listen("Net.ExchangeRequestEvent", () => Game.Session.Exchange.ExchangeRequestEvent += request, () => Game.Session.Exchange.ExchangeRequestEvent -= request, world);
        // A changed offer withdraws a pending final approval in the same reply that changed it.
        Action<int, int, short, int> partnerAdded = (_, _, _, _) => Sync(world);
        NativeHooks.Listen("Net.ExchangeOtherAddEvent", () => Game.Session.Exchange.ExchangeOtherAddEvent += partnerAdded, () => Game.Session.Exchange.ExchangeOtherAddEvent -= partnerAdded, world);
        Sync(world);
    }

    private static CanvasLayer Layer(WorldRoot world, string name, int layer, out Label label, (string Name, Action Pressed) accept, (string Name, Action Pressed) decline)
    {
        var canvas = new CanvasLayer { Name = name, Layer = layer, Visible = false };
        canvas.SetMeta(name, true);
        world.AddChild(canvas);
        var blocker = new ColorRect { Color = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Stop };
        blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(blocker);
        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(centre);
        var box = new VBoxContainer();
        centre.AddChild(box);
        label = UiTheme.Text("", 13);
        box.AddChild(label);
        var buttons = new HBoxContainer();
        box.AddChild(buttons);
        foreach (var (id, text, pressed) in new[] { (accept.Name, "Accept", accept.Pressed), (decline.Name, "Decline", decline.Pressed) })
        {
            var button = new Button { Name = id, Text = text, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += pressed;
            buttons.AddChild(button);
        }
        return canvas;
    }

    /// <summary>
    /// Mirrors the trade state into the window metas and keeps the Classic modals in step. The per-frame
    /// <paramref name="tick"/> skips the window metas and the status wording while the trade window is hidden: only
    /// the window reads them, and the window's show catches up before it draws.
    /// </summary>
    internal static void Sync(WorldRoot world, bool tick = false)
    {
        var state = StateOf(world);
        if (Native.Get<HudWindow>(world, "Exchange.Panel") is not { } panel) return;
        bool shown = Native.Get<bool>(world, "Exchange.shown"), amountShown = Native.Get<bool>(world, "Exchange.amountShown");
        if (state.FinalPending && (!shown || Native.Get<bool>(world, "Exchange.ConfirmedByMe") || Offers(world) != state.Offers)) CloseFinal(world);
        if (!amountShown) state.Gold = false;

        if (!tick || panel.IsVisibleInTree()) Mirror(world, panel, state, amountShown);
        else Native.Get<CanvasLayer>(world, "Exchange.amountLayer")?.SetMeta(HotNames.ExGold, state.Gold);

        bool pending = Native.Get<bool>(world, "Exchange.requestPending");
        if (state.Request is { } request && panel.HasMeta(HotNames.ClassicExchange))
        {
            if (pending && !request.Visible)
            {
                state.RequestLabel!.Text = $"{Native.Get<string>(world, "Exchange.PartnerName")} wants to trade with you.\nDo you agree?";
                request.Visible = true;
            }
            else if (!pending && request.Visible) request.Visible = false;
            // The Classic request replaces the desktop dialog; hiding it does not answer the request.
            if (Native.Get<Window>(world, "Exchange.askDialog") is { Visible: true } dialog) dialog.Hide();
        }
    }

    private static void Mirror(WorldRoot world, HudWindow panel, State state, bool amountShown)
    {
        panel.SetMeta(HotNames.ExLocked, Native.Get<bool>(world, "Exchange.ConfirmedByMe"));
        panel.SetMeta(HotNames.ExPartnerLocked, Native.Get<bool>(world, "Exchange.ConfirmedByPartner"));
        panel.SetMeta(HotNames.ExPending, Native.Get<bool>(world, "Exchange.addInFlight"));
        panel.SetMeta(HotNames.ExModal, amountShown || state.FinalPending);
        panel.SetMeta(HotNames.ExWallet, Game.Session.Self.Sheet.Gold);
        panel.SetMeta(HotNames.ExMyGold, Native.Get<int>(world, "Exchange.MyGoldOffer"));
        panel.SetMeta(HotNames.ExOtherGold, Native.Get<int>(world, "Exchange.TheirGoldOffer"));
        panel.SetMeta(HotNames.ExPartner, Native.Get<string>(world, "Exchange.PartnerName") ?? "Player");
        panel.SetMeta(HotNames.ExSelf, Game.Session.Self.LastEnter.Name ?? "You");
        Native.Get<CanvasLayer>(world, "Exchange.amountLayer")?.SetMeta(HotNames.ExGold, state.Gold);

        if (Native.Get<Label>(world, "Exchange.status") is { } status)
        {
            string text = status.Text switch
            {
                "Waiting for partner…" => "Waiting for partner...",
                "Finalising…" => "Finalising...",
                "Partner confirmed — press Confirm." => "Partner confirmed",
                var other => other,
            };
            if (text != status.Text) status.Text = text;
        }
    }

    private static void Answer(WorldRoot world, bool accept)
    {
        if (StateOf(world).Request is { } request) request.Visible = false;
        if (Native.Get<Window>(world, "Exchange.askDialog") is { Visible: true } dialog) dialog.Hide();
        Native.Call(world, "Exchange.AnswerRequest", accept);
        Sync(world);
    }

    private static (int, int, int, int) Offers(WorldRoot world) => (
        Native.Get<ICollection>(world, "Exchange.MyOffer")?.Count ?? 0, Native.Get<ICollection>(world, "Exchange.TheirOffer")?.Count ?? 0,
        Native.Get<int>(world, "Exchange.MyGoldOffer"), Native.Get<int>(world, "Exchange.TheirGoldOffer"));

    /// <summary>The Trade button asks first; Enter never reaches this decision.</summary>
    public static void RequestFinal(WorldRoot world)
    {
        var state = StateOf(world);
        if (!Native.Get<bool>(world, "Exchange.shown") || Native.Get<bool>(world, "Exchange.ConfirmedByMe") || state.FinalPending) return;
        if (Native.Get<bool>(world, "Exchange.addInFlight") || Native.Get<bool>(world, "Exchange.amountShown"))
        {
            Native.Call(world, "Exchange.SetStatus", "Finish adding your offer before confirming.", true);
            return;
        }
        Native.Call(world, "ItemTooltip.Hide");
        state.FinalLabel!.Text = FinalQuestion;
        state.Offers = Offers(world);
        state.FinalPending = true;
        state.Final!.Visible = true;
        Sync(world);
    }

    public static void CloseFinal(WorldRoot world)
    {
        var state = StateOf(world);
        state.FinalPending = false;
        if (state.Final != null) state.Final.Visible = false;
    }

    private static void AcceptFinal(WorldRoot world)
    {
        var state = StateOf(world);
        if (!state.FinalPending) return;
        bool unchanged = Offers(world) == state.Offers;
        CloseFinal(world);
        if (!unchanged || !Native.Get<bool>(world, "Exchange.shown") || Native.Get<bool>(world, "Exchange.ConfirmedByMe")
            || Native.Get<bool>(world, "Exchange.addInFlight") || Native.Get<bool>(world, "Exchange.amountShown")) return;
        Native.Call(world, "Exchange.OnConfirm");
        Sync(world);
    }

    /// <summary>Opens the original amount frame for coins instead of the client's gold text field.</summary>
    public static void OpenGold(WorldRoot world)
    {
        var state = StateOf(world);
        int gold = Game.Session.Self.Sheet.Gold;
        if (!Native.Get<bool>(world, "Exchange.shown") || Native.Get<bool>(world, "Exchange.ConfirmedByMe") || Native.Get<bool>(world, "Exchange.ConfirmedByPartner")
            || Native.Get<bool>(world, "Exchange.addInFlight") || state.FinalPending || gold <= 0) return;
        if (Native.Get<CanvasLayer>(world, "Exchange.amountLayer") is not { } layer || Native.Get<SpinBox>(world, "Exchange.amountSpin") is not { } spin) return;
        Native.Call(world, "ItemTooltip.Hide");
        state.Gold = true;
        Native.Set(world, "Exchange.amountSlot", -1);
        Native.Set(world, "Exchange.amountMax", gold);
        if (Native.Get<TextureRect>(world, "Exchange.AmountIcon") is { } icon) icon.Texture = UiIcons.Get("system/coin");
        if (Native.Get<Label>(world, "Exchange.amountName") is { } name) name.Text = "Offer coins";
        if (Native.Get<Label>(world, "Exchange.amountHint") is { } hint) hint.Text = $"You have {gold:n0}";
        spin.MaxValue = gold;
        spin.Value = 1;
        spin.GetLineEdit().Text = "1";
        layer.SetMeta("ex_gold", true);
        layer.Visible = true;
        Native.Set(world, "Exchange.amountShown", true);
        spin.GetLineEdit().GrabFocus();
        spin.GetLineEdit().SelectAll();
        Sync(world);
    }

    private static void ConfirmAmount(WorldRoot world)
    {
        var state = StateOf(world);
        if (!Native.Get<bool>(world, "Exchange.amountShown") || Native.Get<CanvasLayer>(world, "Exchange.amountLayer") is not { } layer) return;
        var hint = Native.Get<Label>(world, "Exchange.amountHint");
        if (state.Gold)
        {
            var edit = Native.Get<SpinBox>(world, "Exchange.amountSpin")!.GetLineEdit();
            int max = Native.Get<int>(world, "Exchange.amountMax");
            if (!int.TryParse(edit.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1 || value > max)
            {
                if (hint != null) hint.Text = $"Valid quantity: 1–{max:n0}.";
                layer.SetMeta("ex_amount_error", hint?.Text ?? "");
                edit.GrabFocus();
                edit.SelectAll();
                return;
            }
            Native.Call(world, "Exchange.CloseAmount");
            state.Gold = false;
            if (Native.Get<LineEdit>(world, "Exchange.goldEdit") is { } goldEdit) goldEdit.Text = value.ToString(CultureInfo.InvariantCulture);
            Native.Call(world, "Exchange.OnAddGold");
        }
        else
        {
            Native.Call(world, "Exchange.ConfirmAmount");
            if (Native.Get<bool>(world, "Exchange.amountShown")) layer.SetMeta("ex_amount_error", hint?.Text ?? "");
        }
        Sync(world);
    }

    /// <summary>Offer capacity as the server counts it: repeated countable items share a slot.</summary>
    public static int OfferCount(WorldRoot world)
    {
        var counted = new HashSet<int>();
        int count = 0;
        foreach (var item in Native.Get<IEnumerable>(world, "Exchange.MyOffer") ?? Array.Empty<object>())
        {
            int id = Native.Get<int>(item!, "ItemId");
            if ((ItemData.Get(id)?.Countable ?? 0) == 0 || counted.Add(id)) count++;
        }
        return count;
    }
}

/// <summary>
/// Runs the trade synchronisation every frame, including while the trade window is hidden: a request, a closed trade
/// or a changed offer must reach the Classic modals at once. The window metas follow only while the window is shown.
/// </summary>
public partial class NativeTradeSync : Node
{
    private readonly WorldRoot? _world;

    public NativeTradeSync() { }

    public NativeTradeSync(WorldRoot world)
    {
        _world = world;
        ProcessPriority = int.MinValue;
    }

    public override void _Process(double delta)
    {
        if (_world != null && IsInstanceValid(_world)) NativeTrade.Sync(_world, tick: true);
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName ClassicExchange = "classic_exchange";
    internal static readonly StringName ExGold = "ex_gold";
    internal static readonly StringName ExLocked = "ex_locked";
    internal static readonly StringName ExModal = "ex_modal";
    internal static readonly StringName ExMyGold = "ex_my_gold";
    internal static readonly StringName ExOtherGold = "ex_other_gold";
    internal static readonly StringName ExPartner = "ex_partner";
    internal static readonly StringName ExPartnerLocked = "ex_partner_locked";
    internal static readonly StringName ExPending = "ex_pending";
    internal static readonly StringName ExSelf = "ex_self";
    internal static readonly StringName ExWallet = "ex_wallet";
}
