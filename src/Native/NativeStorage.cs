using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterStorage(PluginContext context)
    {
        foreach (string id in NativeStorage.WindowIds)
            NativeWindows.Prepare(id, (window, world) => NativeStorage.Prepare(window, world));
    }
}

/// <summary>
/// The Classic storage adapter for the client's normal warehouse, VIP vault and clan warehouse. It
/// builds the named stored and carried item cells and the pager the Classic storage skin arranges,
/// names the native coin, PIN and key controls, and shows storage in Classic pages (24 cells; the
/// 48-slot VIP vault in four pages of 12). Every transfer goes through the client's own handlers:
/// right-click picks a destination, drag uses the chosen cell, and the client validates, sends and
/// applies each transfer. A whole stack asks for the count in the client's prompt, as the original
/// storage did; a part picked with the mouse wheel during the drag moves at once.
/// </summary>
public sealed class NativeStorage
{
    public enum StorageKind { Warehouse, Vip, Clan }

    public static readonly string[] WindowIds = { "warehouse", "vipwarehouse", "clanwarehouse" };

    private const float CellSize = 44f;
    private const int CarriedCells = 28;
    private const int WarehouseSlots = 192;
    private const int WarehousePageSize = 24;
    private const int VipPageSize = 12;
    private const int ClanPageSize = 24;

    private static readonly ConditionalWeakTable<HudWindow, NativeStorage> _storages = new();

    public HudWindow Window { get; }
    public StorageKind Kind { get; }
    public int PageSize { get; }
    public int Pages { get; }
    public QuantityPrompt? Amount { get; }
    public VipVaultPinPrompt? PinPrompt { get; private set; }

    /// <summary>The visible storage page: the clan bank keeps the client's page, the others page in the skin.</summary>
    public int Page
    {
        get => _nativePage ? Native.Get<int>(_world, "ClanWarehouse.page") : _page;
        private set
        {
            if (_nativePage) Native.Call(_world, "ClanWarehouse.TurnPage", value - Page);
            else _page = value;
        }
    }

    private readonly WorldRoot _world;
    private readonly Inventory _inventory;
    private readonly ItemSlot[] _slots;
    private readonly int _gridStart;
    private readonly ItemSlotView[] _stored;
    private readonly ItemSlotView[] _carried = new ItemSlotView[CarriedCells];
    private readonly Label _pageLabel;
    private readonly Label? _status;
    private readonly bool _nativePage;
    private int _page;
    private object? _companion;

    private bool Vip => Kind == StorageKind.Vip;
    private int Slots => _slots.Length;

    public static NativeStorage? Of(HudWindow window) => _storages.TryGetValue(window, out var storage) ? storage : null;

    /// <summary>Prepares a storage window once. Returns null when the client lacks a required member.</summary>
    public static NativeStorage? Prepare(HudWindow window, WorldRoot world)
    {
        if (_storages.TryGetValue(window, out var existing)) return existing;
        StorageKind? kind = window.Id switch
        {
            "warehouse" => StorageKind.Warehouse,
            "vipwarehouse" => StorageKind.Vip,
            "clanwarehouse" => StorageKind.Clan,
            _ => null,
        };
        if (kind == null) return null;
        string slotsField = kind switch { StorageKind.Warehouse => "Warehouse.StoredItems", StorageKind.Vip => "VipWarehouse.StoredItems", _ => "ClanWarehouse.slots" };
        if (!Native.TryGet<ItemSlot[]>(world, slotsField, out var slots) || Game.Session.Self.Inventory is not { } inventory)
            return null;
        var storage = new NativeStorage(window, world, kind.Value, slots, inventory);
        _storages.Add(window, storage);
        return storage;
    }

    private NativeStorage(HudWindow window, WorldRoot world, StorageKind kind, ItemSlot[] slots, Inventory inventory)
    {
        Window = window;
        Kind = kind;
        _world = world;
        _slots = slots;
        _inventory = inventory;
        _gridStart = Inventory.GridStart;
        _nativePage = kind == StorageKind.Clan && Native.Has(world, "ClanWarehouse.page");
        PageSize = kind switch { StorageKind.Warehouse => WarehousePageSize, StorageKind.Vip => VipPageSize, _ => ClanPageSize };
        Pages = Math.Max(1, (kind == StorageKind.Warehouse ? WarehouseSlots : slots.Length) / PageSize);

        var controls = new Control { Name = "storage_classic_controls", Visible = false };
        window.Body.AddChild(controls);
        _stored = new ItemSlotView[PageSize];
        HideNativeGrid(Field("Warehouse.Cells", "VipWarehouse.cells", "ClanWarehouse.cells"));

        _status = Native.Get<Label>(world, Field("Warehouse.Status", "VipWarehouse.Status", "ClanWarehouse.status"));
        _pageLabel = new Label { Name = "storage_page", Text = $"1 / {Pages}" };
        controls.AddChild(_pageLabel);
        foreach (int step in new[] { -1, 1 })
        {
            var button = new Button { Name = step < 0 ? "storage_prev" : "storage_next", FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => TurnPage(step);
            controls.AddChild(button);
        }
        Amount = Native.Get<QuantityPrompt>(world, Field("Warehouse.AmountPrompt", "VipWarehouse.amountPrompt", "ClanWarehouse.amountPrompt"));
        if (Vip)
        {
            NativeWindows.Name(Native.Get<Label>(world, "VipWarehouse.expiryLbl"), "storage_expiry");
            NativeWindows.Name(Native.Get<Control>(world, "VipWarehouse.keyCell"), "storage_key");
            NativeWindows.Name(Native.Get<Button>(world, "VipWarehouse.setPinBtn"), "storage_pin_set");
            NativeWindows.Name(Native.Get<Button>(world, "VipWarehouse.clearPinBtn"), "storage_pin_clear");
            PreparePinPrompt();
        }
        else
        {
            NativeWindows.Name(Native.Get<Button>(world, Kind == StorageKind.Clan ? "ClanWarehouse.depositBtn" : "Warehouse.depositBtn"), "storage_deposit");
            NativeWindows.Name(Native.Get<Button>(world, Kind == StorageKind.Clan ? "ClanWarehouse.withdrawBtn" : "Warehouse.withdrawBtn"), "storage_withdraw");
        }
        window.VisibilityChanged += () =>
        {
            if (!window.Visible) return;
            if (!_nativePage) _page = 0;
            Callable.From(KeepInventoryClosed).CallDeferred();
        };
        NativeWindows.Name(_status, "storage_status");
        if (Amount != null) window.SetMeta("storage_amount", Amount);

        for (int i = 0; i < PageSize; i++)
        {
            _stored[i] = StoredCell(i);
            controls.AddChild(_stored[i]);
        }
        for (int i = 0; i < CarriedCells; i++)
        {
            _carried[i] = CarriedCell(i);
            controls.AddChild(_carried[i]);
        }
        NativeWindows.Sync(window, Sync);
    }

    /// <summary>Moves to the previous or next page, stopping at the first and last page.</summary>
    public void TurnPage(int step)
    {
        int next = Mathf.Clamp(Page + step, 0, Pages - 1);
        if (next == Page) return;
        Page = next;
        Sync();
    }

    private string Field(string warehouse, string vip, string clan) => Kind switch
    {
        StorageKind.Warehouse => warehouse,
        StorageKind.Vip => vip,
        _ => clan,
    };

    private bool Busy => Native.Get<bool>(_world, "Warehouse.StorageTransferBusy");

    /// <summary>Whether the VIP vault is unlocked (a key is active and not expired).</summary>
    private bool VaultOpen => !Vip || Native.Get<bool>(_world, "VipWarehouse.open");

    private bool InMainBag(int abs) => abs >= _gridStart && abs < _gridStart + CarriedCells && abs < _inventory.Length;

    private void Sync()
    {
        int page = Page;
        _pageLabel.Text = $"{page + 1} / {Pages}";
        _pageLabel.TooltipText = "Storage page";
        Window.SetMeta(HotNames.StoragePage, page);
        Window.SetMeta(HotNames.StoragePages, Pages);
        if (!Vip) Window.SetMeta(HotNames.StorageMoney, Native.Get<int>(_world, Kind == StorageKind.Clan ? "ClanWarehouse.money" : "Warehouse.Money"));
        RouteCompanion();
        bool locked = !VaultOpen;
        for (int i = 0; i < _stored.Length; i++)
        {
            int abs = page * PageSize + i;
            Show(_stored[i], abs < Slots ? _slots[abs] : default);
            // A locked vault marks its stored items; empty cells stay plain.
            bool itemLocked = locked && !_stored[i].Item.IsEmpty;
            if (Vip && _stored[i].Locked != itemLocked) _stored[i].Locked = itemLocked;
        }
        for (int i = 0; i < CarriedCells; i++)
        {
            int abs = _gridStart + i;
            Show(_carried[i], abs < _inventory.Length ? _inventory[abs] : default);
            var look = abs >= _inventory.Length || Fits(abs) ? SlotLook.Normal : SlotLook.Unfit;
            if (_carried[i].Look != look) _carried[i].Look = look;
        }
        // The small key plaque shows an expired key greyed, without the lock badge that would cover it.
        if (Vip && Native.Get<ItemSlotView>(_world, "VipWarehouse.keyCell") is { } key && Native.Get<Control>(key, "lockIcon") is { Visible: true } badge)
            badge.Visible = false;
        if (Vip && Native.Get<Button>(_world, "VipWarehouse.clearPinBtn") is { } clear)
        {
            // Both PIN actions keep their place; clearing is only offered once a PIN is set.
            clear.Visible = true;
            clear.Disabled = !Native.Get<bool>(_world, "VipWarehouse.pinSet");
        }
    }

    /// <summary>Whether the client lets this carried item into the storage (its own bag-fit rule).</summary>
    private bool Fits(int abs)
    {
        if (_inventory[abs].IsEmpty) return true;
        return Native.Call(_world, Field("Warehouse.WhBagFit", "VipWarehouse.VipBagFit", "ClanWarehouse.ClanBagFit"), abs) is not { } fit || fit.ToString() != "Unfit";
    }

    private static void Show(ItemSlotView cell, ItemSlot item)
    {
        if (cell.Item.Equals(item)) return;
        cell.Set(item);
        if (item.IsEmpty) return;
        var def = ItemData.Get(item.ItemId);
        cell.CountLabel.Text = NativeUi.CountBadge(def, ItemData.ShownCount(def, item));
    }

    private void Tooltip(int abs, ItemSlotView cell)
    {
        if (cell.Item.IsEmpty) Native.Call(_world, "ItemTooltip.Hide");
        else Native.Call(_world, "ItemTooltip.Show", abs, cell.Item, "");
    }

    /// <summary>Moves the client's own storage grid out of the window body; the Classic cells replace it.</summary>
    private void HideNativeGrid(string field)
    {
        var cells = Native.Get<ItemSlotView[]>(_world, field);
        if (cells is not { Length: > 0 } || cells[0]?.GetParent()?.GetParent() is not Control well) return;
        var hidden = new Control { Name = "native_storage_grid", Visible = false };
        Window.AddChild(hidden);
        well.Reparent(hidden, false);
    }

    /// <summary>Keeps the carried inventory embedded: opening storage does not open the inventory window.</summary>
    private void KeepInventoryClosed()
    {
        if (!Window.Visible || Native.Get<object>(_world, "BagCompanions.Pairing") is not { } pairing) return;
        if (!Native.Get<bool>(pairing, "openedByCompanion")) return;
        Native.Set(pairing, "openedByCompanion", false);
        Native.Call(_world, "MainWindows.Hide", "Inventory");
    }

    // Stored cells ------------------------------------------------------------------------------

    private ItemSlotView StoredCell(int index)
    {
        int Absolute() => Page * PageSize + index;
        // PicksAmount: a dragged stack arms the client's mouse-wheel dial, which wins over the page wheel while dragging.
        var cell = new ItemSlotView(CellSize) { Index = index, Name = "storage_cell_" + index, Veiled = true, PicksAmount = true };
        cell.RightClicked += _ => Withdraw(Absolute(), -1, null);
        cell.Hovered += c => Tooltip(-1, c);
        cell.Unhovered += _ => Native.Call(_world, "ItemTooltip.Hide");
        cell.Wheeled += (_, step) => TurnPage(step);
        cell.DragOut = c =>
        {
            if (!VaultOpen || _slots[Absolute()].IsEmpty) return default;
            KnightOnlineUiClassic.Layout.ClassicStackDial.StyleNativeCount(c);
            return new Godot.Collections.Dictionary { { "companionFrom", Absolute() } };
        };
        cell.CanDrop = (_, data) => Native.Call(_world, Field("Warehouse.CanDropOn", "VipWarehouse.CanDropOnVault", "ClanWarehouse.CanDropOn"), Absolute(), data) is true;
        cell.Dropped = (_, data) =>
        {
            int target = Absolute();
            void Drop() => Native.Call(_world, Field("Warehouse.DropOn", "VipWarehouse.DropOnVault", "ClanWarehouse.DropOn"), target, data);
            // A move inside the storage stays native: the client refuses a part of a stack there.
            var d = data.AsGodotDictionary();
            if (d.TryGetValue("invFrom", out var from)) Deposit(from.AsInt32(), target, DragLayer.ChosenAmount(d), Drop);
            else Drop();
        };
        return cell;
    }

    // Carried inventory cells -------------------------------------------------------------------

    private ItemSlotView CarriedCell(int index)
    {
        var cell = new ItemSlotView(CellSize) { Index = _gridStart + index, Name = "storage_bag_" + index, Veiled = true, PicksAmount = true };
        cell.RightClicked += c => Deposit(c.Index, -1, null, () => Native.Call(_world, Field("Warehouse.TakeFromBag", "VipWarehouse.TakeFromBag", "ClanWarehouse.TakeFromBag"), c.Index));
        cell.Hovered += c => Tooltip(c.Index, c);
        cell.Unhovered += _ => Native.Call(_world, "ItemTooltip.Hide");
        cell.DragOut = c =>
        {
            KnightOnlineUiClassic.Layout.ClassicStackDial.StyleNativeCount(c);
            return new Godot.Collections.Dictionary { { "invFrom", c.Index } };
        };
        cell.CanDrop = (c, data) => !Busy && (BagMove(c.Index, data) || TakesFromStorage(data));
        cell.Dropped = (c, data) =>
        {
            var d = data.AsGodotDictionary();
            int? amount = DragLayer.ChosenAmount(d);
            if (BagMove(c.Index, data))
            {
                // Bag to bag through the inventory API: a part picked with the wheel splits the stack, as the client's bag does.
                int from = d["invFrom"].AsInt32();
                if (KnightOnlineUiClassic.Layout.ClassicStackDial.Part(amount, _inventory[from].Count) is int part) Plugin.Kit.Game.Inventory.MoveAmount(from, c.Index, part);
                else Plugin.Kit.Game.Inventory.Move(from, c.Index);
            }
            else if (TakesFromStorage(data)) Withdraw(d["companionFrom"].AsInt32(), c.Index, amount);
        };
        return cell;
    }

    private bool BagMove(int target, Variant data)
    {
        if (Busy || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        return d.ContainsKey("invFrom") && d["invFrom"].AsInt32() != target && InMainBag(d["invFrom"].AsInt32());
    }

    private bool TakesFromStorage(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("companionFrom")) return false;
        int from = d["companionFrom"].AsInt32();
        return from >= 0 && from < Slots && !_slots[from].IsEmpty;
    }

    // Count prompt ------------------------------------------------------------------------------

    /// <summary>
    /// The original storage asks how many of a stack to move. Since ff78eef the client asks only on touch screens and
    /// otherwise moves the amount the drag picked with the mouse wheel, or the whole stack. The Classic storage asks
    /// for a whole stack (a right-click, or a drag without a part picked) in the client's prompt, prefilled with the
    /// whole stack; a part picked with the wheel moves at once.
    /// </summary>
    private static bool AsksCount(ItemSlot slot, int? amount) =>
        !slot.IsEmpty && slot.Count > 1 && ItemData.Get(slot.ItemId) is { Countable: not 0 } && (amount == null || amount >= slot.Count);

    private bool Shown => Native.Get<bool>(_world, Field("Warehouse.shown", "VipWarehouse.shown", "ClanWarehouse.shown"));

    /// <summary>
    /// Stores a carried item. A whole stack asks for the count first, then the client stores the chosen count through
    /// <c>DepositSlot</c>/<c>VipDepositSlot</c>/<c>ClanDepositSlot(abs, target, amount)</c>. Everything else, and every
    /// case the client refuses (busy, a locked vault, a vault key, an item the storage does not take), runs
    /// <paramref name="native"/>, the client's own handler for the gesture.
    /// </summary>
    private void Deposit(int abs, int target, int? amount, Action native)
    {
        var slot = InMainBag(abs) ? _inventory[abs] : default;
        if (Amount == null || !AsksCount(slot, amount) || Busy || !VaultOpen || !Fits(abs)
            || Vip && Native.Call(Native.StaticOwner("LibreKO.Features.Windows.VipWarehouseFeature"), "IsVaultKey", slot.ItemId) is true)
        {
            native();
            return;
        }
        Amount.Open(ItemIcons.Get(slot.ItemId), $"Store {ItemData.DisplayName(slot.ItemId)}", $"You carry {slot.Count:n0}", slot.Count, slot.Count, n =>
        {
            if (Shown && _inventory[abs].Equals(slot))
                Native.Call(_world, Field("Warehouse.DepositSlot", "VipWarehouse.DepositSlot", "ClanWarehouse.DepositSlot"), abs, target, (int?)(int)n);
        }, "Store");
    }

    /// <summary>
    /// Takes a stored item out into the bag (<paramref name="target"/>, or the client's choice for -1). A whole stack
    /// asks for the count first; the client then takes it out through <c>AskWithdraw</c>/<c>AskVipWithdraw</c>/
    /// <c>AskClanWithdraw(index, target, amount)</c>, which also refuses a busy storage, a locked vault or missing clan rights.
    /// </summary>
    private void Withdraw(int index, int target, int? amount)
    {
        string method = Field("Warehouse.AskWithdraw", "VipWarehouse.AskWithdraw", "ClanWarehouse.AskWithdraw");
        var slot = index >= 0 && index < Slots ? _slots[index] : default;
        if (Amount == null || !AsksCount(slot, amount) || Busy || !VaultOpen
            || Kind == StorageKind.Clan && !Native.Get<bool>(_world, "ClanWarehouse.CanWithdraw"))
        {
            Native.Call(_world, method, index, target, amount);
            return;
        }
        Amount.Open(ItemIcons.Get(slot.ItemId), $"Take out {ItemData.DisplayName(slot.ItemId)}", $"Stored {slot.Count:n0}", slot.Count, slot.Count, n =>
        {
            if (Shown && _slots[index].Equals(slot)) Native.Call(_world, method, index, target, (int?)(int)n);
        }, "Take out");
    }

    /// <summary>
    /// Routes the storage's bag companion (a right-click in the Classic bag, a stored item dropped on a Classic bag
    /// cell) through the same count prompt. The client creates the companion when the storage first opens.
    /// </summary>
    private void RouteCompanion()
    {
        if (Native.Get<object>(_world, Field("Warehouse.companion", "VipWarehouse.companion", "ClanWarehouse.companion")) is not { } companion
            || ReferenceEquals(companion, _companion)) return;
        _companion = companion;
        if (Native.Get<Func<int, bool>>(companion, "Take") is { } take)
            Native.Set(companion, "Take", new Func<int, bool>(abs =>
            {
                if (!InMainBag(abs)) return take(abs);
                Deposit(abs, -1, null, () => take(abs));
                return true;
            }));
        if (Native.MemberType(companion, "IntoBag") == typeof(Func<int, int, int?, bool>))
            Native.Set(companion, "IntoBag", new Func<int, int, int?, bool>((from, to, amount) =>
            {
                Withdraw(from, to, amount);
                return true;
            }));
    }

    // VIP PIN -----------------------------------------------------------------------------------

    /// <summary>Shows the client's PIN requests in a named PIN window instead of its generic dialog.</summary>
    private void PreparePinPrompt()
    {
        var dialog = Native.Get<AcceptDialog>(_world, "VipWarehouse.pinDlg");
        var message = Native.Get<Label>(_world, "VipWarehouse.pinPrompt");
        var input = Native.Get<LineEdit>(_world, "VipWarehouse.pinEdit");
        if (dialog == null || message == null || input == null) return;
        var prompt = new VipVaultPinPrompt { Name = "vipwarehouse_pin_layer" };
        _world.AddChild(prompt);
        PinPrompt = prompt;
        bool reopened = false;
        dialog.AboutToPopup += () =>
        {
            reopened = true;
            prompt.Message.Text = message.Text;
            prompt.Input.Text = input.Text;
            prompt.PopupCentered();
            Callable.From(() =>
            {
                dialog.Hide();
                if (prompt.Visible && prompt.Input.IsInsideTree()) prompt.Input.GrabFocus();
            }).CallDeferred();
        };
        prompt.Confirmed += () =>
        {
            input.Text = prompt.Input.Text;
            reopened = false;
            Native.Call(_world, "VipWarehouse.SubmitPin");
            if (!reopened) prompt.Hide();
        };
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName StorageMoney = "storage_money";
    internal static readonly StringName StoragePage = "storage_page";
    internal static readonly StringName StoragePages = "storage_pages";
}
