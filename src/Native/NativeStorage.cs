using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
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
/// builds the controls the Classic storage skin arranges (named stored and carried item cells, pager,
/// clan coin buttons and quantity prompt), pages normal storage in 24-slot pages, and implements the
/// Classic transfer rules: right-click picks a destination, drag uses the chosen cell, stacks larger
/// than one ask for an amount, and the vaults support quantity transfers and in-page rearrangement.
/// The client still validates, sends and applies every transfer through its own pending state.
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
    private const int CoinMax = 2_100_000_000;
    private const int NonStorableFirst = 900_000_001;
    private const int NonStorableLast = 999_999_999;
    private const byte OpInput = 2;
    private const byte OpOutput = 3;
    private const byte OpMove = 4;
    private const string VaultHint = "Right-click to store / withdraw";
    private const string ClanHint = "Deposit is open to all; only the chief / vice-chief may withdraw.";
    private const string WithdrawRefusal = "Only the chief or vice-chief may withdraw.";

    private static readonly ConditionalWeakTable<HudWindow, NativeStorage> _storages = new();

    public HudWindow Window { get; }
    public StorageKind Kind { get; }
    public int PageSize { get; }
    public int Pages { get; }
    public QuantityPrompt? Amount { get; }
    public VipVaultPinPrompt? PinPrompt { get; private set; }

    /// <summary>The visible storage page; the vaults keep the client's own page index in step.</summary>
    public int Page
    {
        get => Kind == StorageKind.Warehouse ? _page : Native.Get<int>(_world, Field("_vipWhPage", "_clanWhPage"));
        private set
        {
            if (Kind == StorageKind.Warehouse) _page = value;
            else Native.Set(_world, Field("_vipWhPage", "_clanWhPage"), value);
        }
    }

    private readonly World _world;
    private readonly Inventory _inventory;
    private readonly ItemSlot[] _slots;
    private readonly int _gridStart;
    private readonly ItemSlotView[] _stored;
    private readonly ItemSlotView[] _carried = new ItemSlotView[CarriedCells];
    private readonly Label _pageLabel;
    private readonly Label? _status;
    private readonly Button? _deposit, _withdraw;
    private readonly bool _partialTransfers;
    private int _page;
    private bool _moving;
    private Net? _net;
    private int _moveFrom, _moveTo;

    private bool Vip => Kind == StorageKind.Vip;
    private int Slots => _slots.Length;

    public static NativeStorage? Of(HudWindow window) => _storages.TryGetValue(window, out var storage) ? storage : null;

    /// <summary>Prepares a storage window once. Returns null when the client lacks a required member.</summary>
    public static NativeStorage? Prepare(HudWindow window, World world)
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
        string slotsField = kind switch { StorageKind.Warehouse => "_warehouse", StorageKind.Vip => "_vipWh", _ => "_clanWh" };
        if (!Native.TryGet<ItemSlot[]>(world, slotsField, out var slots) || !Native.TryGet<Inventory>(world, "Inv", out var inventory))
            return null;
        var storage = new NativeStorage(window, world, kind.Value, slots, inventory);
        _storages.Add(window, storage);
        return storage;
    }

    private NativeStorage(HudWindow window, World world, StorageKind kind, ItemSlot[] slots, Inventory inventory)
    {
        Window = window;
        Kind = kind;
        _world = world;
        _slots = slots;
        _inventory = inventory;
        _gridStart = Inventory.GridStart;
        PageSize = kind switch { StorageKind.Warehouse => WarehousePageSize, StorageKind.Vip => VipPageSize, _ => ClanPageSize };
        Pages = Math.Max(1, (kind == StorageKind.Warehouse ? WarehouseSlots : slots.Length) / PageSize);
        _partialTransfers = kind == StorageKind.Warehouse || PendingSupportsAmounts();

        var controls = new Control { Name = "storage_classic_controls", Visible = false };
        window.Body.AddChild(controls);
        _stored = new ItemSlotView[PageSize];

        if (kind == StorageKind.Warehouse)
        {
            HideNativeWarehouseGrid();
            _status = Native.Get<Label>(world, "_whStatus");
            _pageLabel = new Label { Name = "storage_page", Text = "1 / 4" };
            controls.AddChild(_pageLabel);
            foreach (int step in new[] { -1, 1 })
            {
                var button = new Button { Name = step < 0 ? "storage_prev" : "storage_next" };
                button.Pressed += () => TurnPage(step);
                controls.AddChild(button);
            }
            Amount = Native.Get<QuantityPrompt>(world, "_whAmount");
            window.VisibilityChanged += () =>
            {
                if (!window.Visible) return;
                _page = 0;
                Callable.From(KeepInventoryClosed).CallDeferred();
            };
        }
        else
        {
            _status = Native.Get<Label>(world, Field("_vipWhStatus", "_clanWhStatus"));
            _pageLabel = Native.Get<Label>(world, Field("_vipWhPageLbl", "_clanWhPageLbl"))
                ?? new Label { Text = $"1 / {Pages}" };
            if (_pageLabel.GetParent() == null) controls.AddChild(_pageLabel);
            _pageLabel.Name = "storage_page";
            var pager = new HBoxContainer { Name = "storage_pager" };
            controls.AddChild(pager);
            foreach (int step in new[] { -1, 1 })
            {
                var button = new Button { Name = step < 0 ? "storage_prev" : "storage_next", Text = step < 0 ? "◀" : "▶", FocusMode = Control.FocusModeEnum.None };
                button.Pressed += () => TurnPage(step);
                pager.AddChild(button);
            }
            Amount = new QuantityPrompt(76) { Name = Field("vipwarehouse_amount", "clanwarehouse_amount") };
            world.AddChild(Amount);
            window.VisibilityChanged += () => { if (!window.Visible) Amount.Close(); };
            if (kind == StorageKind.Vip)
            {
                NativeWindows.Name(Native.Get<Label>(world, "_vipWhExpiryLbl"), "storage_expiry");
                PreparePinPrompt();
            }
            else
            {
                _deposit = new Button { Name = "storage_deposit", Text = "Deposit", FocusMode = Control.FocusModeEnum.None };
                _withdraw = new Button { Name = "storage_withdraw", Text = "Withdraw", FocusMode = Control.FocusModeEnum.None };
                _deposit.Pressed += () => AskClanGold(true);
                _withdraw.Pressed += () => AskClanGold(false);
                controls.AddChild(_deposit);
                controls.AddChild(_withdraw);
            }
            SubscribeVaultResults();
        }
        NativeWindows.Name(_status, "storage_status");
        if (Amount != null) window.SetMeta("storage_amount", Amount);

        for (int i = 0; i < PageSize; i++)
        {
            _stored[i] = kind == StorageKind.Warehouse ? WarehouseCell(i) : VaultCell(i);
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
        if (Kind != StorageKind.Warehouse) Native.Call(_world, Field("RefreshVipWarehouse", "RefreshClanWarehouse"));
        Sync();
    }

    private string Field(string vip, string clan) => Vip ? vip : clan;

    private bool Shown => Native.Get<bool>(_world, Kind switch
    {
        StorageKind.Warehouse => "_whShown",
        StorageKind.Vip => "_vipWhShown",
        _ => "_clanWhShown",
    });

    private bool Busy
    {
        get
        {
            if (_moving) return true;
            if (Native.TryGet<bool>(_world, "StorageTransferBusy", out bool busy)) return busy;
            return Native.Get<bool>(_world, "_whInFlight") || Native.Get<bool>(_world, "_vipWhInFlight")
                || Native.Get<bool>(_world, "_clanWhInFlight") || Native.Get<bool>(_world, "_moveInFlight");
        }
    }

    private static bool Officer => Net.I?.MyClan.CanInvite ?? false;

    private bool InMainBag(int abs) => abs >= _gridStart && abs < _gridStart + CarriedCells && abs < _inventory.Length;

    private static bool IsStackable(int itemId) => ItemData.Get(itemId) is { Countable: not 0 };

    private void Sync()
    {
        int page = Page;
        _pageLabel.Text = $"{page + 1} / {Pages}";
        if (Kind == StorageKind.Warehouse) _pageLabel.TooltipText = "Storage page";
        Window.SetMeta("storage_page", page);
        Window.SetMeta("storage_pages", Pages);
        if (Kind != StorageKind.Vip)
            Window.SetMeta("storage_money", Native.Get<int>(_world, Kind == StorageKind.Warehouse ? "_whMoney" : "_clanWhMoney"));
        for (int i = 0; i < _stored.Length; i++)
        {
            int abs = page * PageSize + i;
            Show(_stored[i], abs < Slots ? _slots[abs] : default);
        }
        for (int i = 0; i < CarriedCells; i++)
            Show(_carried[i], _gridStart + i < _inventory.Length ? _inventory[_gridStart + i] : default);
        if (_status != null && _status.Text is VaultHint or ClanHint && Kind != StorageKind.Warehouse) _status.Text = "";
        if (_deposit != null && _withdraw != null)
        {
            bool loaded = Native.Get<bool>(_world, "_clanWhLoaded");
            bool officer = Officer;
            _deposit.Disabled = !loaded || (Net.I?.Sheet.Gold ?? 0) <= 0;
            _withdraw.Disabled = !loaded || !officer || Native.Get<int>(_world, "_clanWhMoney") <= 0;
            _withdraw.TooltipText = officer ? "Withdraw coins" : WithdrawRefusal;
        }
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
        if (cell.Item.IsEmpty) Native.Call(_world, "HideItemTooltip");
        else Native.Call(_world, "ShowItemTooltip", abs, cell.Item, "");
    }

    // Normal warehouse --------------------------------------------------------------------------

    private void HideNativeWarehouseGrid()
    {
        var cells = Native.Get<ItemSlotView[]>(_world, "_whCells");
        if (cells is not { Length: > 0 } || cells[0]?.GetParent()?.GetParent() is not Control well) return;
        var hidden = new Control { Name = "native_storage_grid", Visible = false };
        Window.AddChild(hidden);
        well.Reparent(hidden, false);
    }

    private ItemSlotView WarehouseCell(int index)
    {
        int Absolute() => _page * PageSize + index;
        var cell = new ItemSlotView(CellSize) { Index = index, Name = "storage_cell_" + index };
        cell.RightClicked += _ => Native.Call(_world, "WithdrawSlot", Absolute());
        cell.Hovered += c => Tooltip(-1, c);
        cell.Unhovered += _ => Native.Call(_world, "HideItemTooltip");
        cell.Wheeled += (_, step) => TurnPage(step);
        cell.DragOut = _ => new Godot.Collections.Dictionary { { "companionFrom", Absolute() } };
        cell.CanDrop = (_, data) => Native.Call(_world, "CanDropOnWarehouse", Absolute(), data) is true;
        cell.Dropped = (_, data) => Native.Call(_world, "DropOnWarehouse", Absolute(), data);
        return cell;
    }

    /// <summary>Keeps the carried inventory embedded: the warehouse does not open the inventory window.</summary>
    private void KeepInventoryClosed()
    {
        if (!Window.Visible || Native.Get<object>(_world, "_bagPairing") is not { } pairing) return;
        if (!Native.Get<bool>(pairing, "_openedByCompanion")) return;
        Native.Set(pairing, "_openedByCompanion", false);
        Native.Call(_world, "HideMainWindow", "Inventory");
    }

    // Carried inventory cells -------------------------------------------------------------------

    private ItemSlotView CarriedCell(int index)
    {
        var cell = new ItemSlotView(CellSize) { Index = _gridStart + index, Name = "storage_bag_" + index };
        cell.RightClicked += c => Store(c.Index);
        cell.Hovered += c => Tooltip(c.Index, c);
        cell.Unhovered += _ => Native.Call(_world, "HideItemTooltip");
        cell.DragOut = c => new Godot.Collections.Dictionary { { "invFrom", c.Index } };
        cell.CanDrop = (c, data) => !Busy && (BagMove(c.Index, data) || AcceptsIntoBag(data));
        cell.Dropped = (c, data) =>
        {
            if (BagMove(c.Index, data)) Native.Call(_world, "MoveBetween", data.AsGodotDictionary()["invFrom"].AsInt32(), c.Index);
            else if (!Busy) TakeInto(c.Index, data);
        };
        return cell;
    }

    private bool BagMove(int target, Variant data)
    {
        if (Busy || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        return d.ContainsKey("invFrom") && d["invFrom"].AsInt32() != target && InMainBag(d["invFrom"].AsInt32());
    }

    private bool AcceptsIntoBag(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (Kind == StorageKind.Warehouse)
            return !Native.Get<bool>(_world, "_whInFlight") && d.ContainsKey("companionFrom")
                && d["companionFrom"].AsInt32() is >= 0 and < WarehouseSlots;
        return VaultAccepts(true, data);
    }

    private void Store(int abs)
    {
        if (Kind == StorageKind.Warehouse) Native.Call(_world, "DepositSlot", abs, -1);
        else AskVaultTransfer(true, abs, -1);
    }

    private void TakeInto(int abs, Variant data)
    {
        var d = data.AsGodotDictionary();
        if (Kind == StorageKind.Warehouse) Native.Call(_world, "AskWithdraw", d["companionFrom"].AsInt32(), abs);
        else AskVaultTransfer(false, d["vaultFrom"].AsInt32(), abs);
    }

    // VIP vault and clan warehouse --------------------------------------------------------------

    private ItemSlotView VaultCell(int index)
    {
        int Absolute() => Page * PageSize + index;
        var cell = new ItemSlotView(CellSize) { Index = index, Name = "storage_cell_" + index };
        cell.RightClicked += _ => AskVaultTransfer(false, Absolute(), -1);
        cell.Hovered += c => Tooltip(-1, c);
        cell.Unhovered += _ => Native.Call(_world, "HideItemTooltip");
        cell.Wheeled += (_, step) => TurnPage(step);
        cell.DragOut = _ => new Godot.Collections.Dictionary { { "vaultFrom", Absolute() }, { "vipVault", Vip } };
        cell.CanDrop = (_, data) => VaultAccepts(false, data) || CanMoveVault(Absolute(), data);
        cell.Dropped = (_, data) =>
        {
            var d = data.AsGodotDictionary();
            if (d.ContainsKey("invFrom")) AskVaultTransfer(true, d["invFrom"].AsInt32(), Absolute());
            else if (CanMoveVault(Absolute(), data)) MoveVault(d["vaultFrom"].AsInt32(), Absolute());
        };
        return cell;
    }

    private bool VaultAccepts(bool intoBag, Variant data)
    {
        if (Busy || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (!intoBag) return d.ContainsKey("invFrom") && InMainBag(d["invFrom"].AsInt32());
        return d.ContainsKey("vaultFrom") && d.ContainsKey("vipVault") && d["vipVault"].AsBool() == Vip
            && d["vaultFrom"].AsInt32() >= 0 && d["vaultFrom"].AsInt32() < Slots;
    }

    private bool CanMoveVault(int target, Variant data)
    {
        if (!VaultAccepts(true, data) || !Vip && !Officer) return false;
        int source = data.AsGodotDictionary()["vaultFrom"].AsInt32();
        return source != target && source / PageSize == target / PageSize && !_slots[source].IsEmpty && _slots[target].IsEmpty;
    }

    private void SetStatus(string text)
    {
        if (_status != null) _status.Text = text;
    }

    private void AskVaultTransfer(bool deposit, int source, int target)
    {
        if (Busy || !Shown) return;
        if (Vip && Native.Get<int>(_world, "_vipWhExpirySec") <= 0) { SetStatus("Vault rental expired. Renew it with a vault key."); return; }
        if (!Vip && !Native.Get<bool>(_world, "_clanWhLoaded")) { SetStatus("Waiting for clan storage."); return; }
        if (!Vip && !deposit && !Officer) { SetStatus(WithdrawRefusal); return; }
        if (deposit ? !InMainBag(source) : source < 0 || source >= Slots) return;
        var slot = deposit ? _inventory[source] : _slots[source];
        if (slot.IsEmpty || slot.IsLinked) return;
        if (deposit && slot.ItemId is >= NonStorableFirst and <= NonStorableLast) { SetStatus("This item is non-storable."); return; }
        if (!_partialTransfers)
        {
            WholeStackTransfer(deposit, source);
            return;
        }
        void Transfer(long amount)
        {
            if (!Shown || !(deposit ? _inventory[source] : _slots[source]).Equals(slot))
            {
                SetStatus("The item changed. Please select it again.");
                return;
            }
            SendVaultTransfer(deposit, source, target, (int)amount);
        }
        if (IsStackable(slot.ItemId) && slot.Count > 1 && Amount != null)
            Amount.Open(ItemData.Icon(slot.ItemId), (deposit ? "Store " : "Take out ") + ItemData.DisplayName(slot.ItemId),
                $"Available {slot.Count:n0}", slot.Count, slot.Count, Transfer);
        else Transfer(1);
    }

    /// <summary>Clients without amount-aware vault replies transfer whole stacks through their own handlers.</summary>
    private void WholeStackTransfer(bool deposit, int source)
    {
        if (deposit) Native.Call(_world, Field("VipDepositSlot", "ClanWhDepositSlot"), source);
        else if (source / PageSize == Page) Native.Call(_world, Field("VipWithdrawSlot", "ClanWhWithdrawSlot"), source % PageSize);
    }

    private void SendVaultTransfer(bool deposit, int source, int target, int count)
    {
        if (Busy || count <= 0) return;
        if (!Vip && (!deposit && !Officer || !Native.Get<bool>(_world, "_clanWhLoaded"))) return;
        if (Vip && deposit && Native.Get<int>(_world, "_vipWhExpirySec") <= 0) { SetStatus("Vault rental expired. Renew it with a vault key."); return; }
        var slot = deposit ? _inventory[source] : _slots[source];
        if (slot.IsEmpty || count > slot.Count) return;
        bool stackable = IsStackable(slot.ItemId);
        int destination = target;
        bool merge;
        if (deposit)
        {
            if (destination < 0) destination = VaultDestination(slot.ItemId, count, stackable);
            if (destination < 0 || destination >= Slots || !FitsInVault(_slots[destination], slot.ItemId, count, stackable))
            { SetStatus("No room in that storage slot."); return; }
            merge = !_slots[destination].IsEmpty;
        }
        else
        {
            if (destination < 0) destination = BagDestination(slot.ItemId, count, stackable);
            if (!FitsInBag(destination, slot.ItemId, count, stackable)) { SetStatus("No room in that inventory slot."); return; }
            merge = !_inventory[destination].IsEmpty;
        }
        int inventory = deposit ? source : destination, stored = deposit ? destination : source;
        byte op = deposit ? OpInput : OpOutput;
        SetStatus("");
        if (!SetPending(op, inventory, stored, count, merge)) return;
        byte page = (byte)(stored / PageSize), cell = (byte)(stored % PageSize), bag = (byte)(inventory - _gridStart);
        if (Vip)
        {
            if (deposit) Net.I.SendVipWarehouseInput(slot.ItemId, page, bag, cell, count);
            else Net.I.SendVipWarehouseOutput(slot.ItemId, page, cell, bag, count);
        }
        else
        {
            if (deposit) Net.I.SendClanWhInput(slot.ItemId, page, bag, cell, count);
            else Net.I.SendClanWhOutput(slot.ItemId, page, cell, bag, count);
        }
    }

    private static bool FitsInVault(ItemSlot stored, int itemId, int count, bool stackable) =>
        stored.IsEmpty || stackable && stored.ItemId == itemId && !stored.IsLinked && stored.Count + count <= Inventory.StackMax;

    private int VaultDestination(int itemId, int count, bool stackable)
    {
        if (stackable)
            for (int i = 0; i < Slots; i++)
                if (!_slots[i].IsEmpty && FitsInVault(_slots[i], itemId, count, true)) return i;
        for (int i = 0; i < Slots; i++)
            if (_slots[i].IsEmpty) return i;
        return -1;
    }

    private bool FitsInBag(int abs, int itemId, int count, bool stackable) =>
        InMainBag(abs) && (_inventory[abs].IsEmpty || _inventory[abs].ItemId == itemId && stackable && _inventory[abs].Count + count <= Inventory.StackMax);

    private int BagDestination(int itemId, int count, bool stackable)
    {
        if (stackable)
            for (int abs = _gridStart; abs < _gridStart + CarriedCells && abs < _inventory.Length; abs++)
                if (_inventory[abs].ItemId == itemId && _inventory[abs].Count + count <= Inventory.StackMax) return abs;
        return _inventory.FirstFreeGridSlot();
    }

    private bool PendingSupportsAmounts()
    {
        var type = Native.ClientType(Vip ? "LibreKO.World+VipWhPending" : "LibreKO.World+ClanWhPending");
        if (type == null) return false;
        object pending = Activator.CreateInstance(type)!;
        return Native.Has(pending, "Count") && Native.Has(pending, "Merge");
    }

    private bool SetPending(byte op, int inventory, int stored, int count, bool merge)
    {
        var type = Native.ClientType(Vip ? "LibreKO.World+VipWhPending" : "LibreKO.World+ClanWhPending");
        if (type == null) return false;
        object pending = Activator.CreateInstance(type)!;
        bool set = Native.Set(pending, "Op", op) && Native.Set(pending, "InvAbs", inventory)
            && Native.Set(pending, Vip ? "VipIdx" : "WhIdx", stored) && Native.Set(pending, "Count", count)
            && Native.Set(pending, "Merge", merge);
        return set && Native.Set(_world, Field("_vipWhPending", "_clanWhPending"), pending)
            && Native.Set(_world, Field("_vipWhInFlight", "_clanWhInFlight"), true);
    }

    /// <summary>Rearranges the vault within one page. The client has no sender for this request.</summary>
    private void MoveVault(int source, int target)
    {
        SetStatus("");
        var packet = new Packet(Vip ? GameOpcodes.GS_VIP_WAREHOUSE : GameOpcodes.GS_CLAN_WAREHOUSE);
        packet.WriteByte(OpMove);
        packet.WriteInt(0);
        packet.WriteInt(_slots[source].ItemId);
        packet.WriteByte((byte)(source / PageSize));
        packet.WriteByte((byte)(source % PageSize));
        packet.WriteByte((byte)(target % PageSize));
        if (Native.Get<object>(Net.I, "_conn") is not { } connection || !Native.TryCall(connection, "Send", out _, packet)) return;
        _moving = true;
        _moveFrom = source;
        _moveTo = target;
    }

    private void SubscribeVaultResults()
    {
        _net = Net.I;
        if (_net == null) return;
        if (Vip) _net.VipWarehouseResultEvent += OnVaultResult;
        else _net.ClanWhResultEvent += OnVaultResult;
    }

    private void OnVaultResult(byte op, bool ok)
    {
        if (!GodotObject.IsInstanceValid(Window))
        {
            if (Vip) _net!.VipWarehouseResultEvent -= OnVaultResult;
            else _net!.ClanWhResultEvent -= OnVaultResult;
            return;
        }
        if (op != OpMove || !_moving) return;
        _moving = false;
        if (!ok) SetStatus("Transfer failed.");
        else
        {
            _slots[_moveTo] = _slots[_moveFrom];
            _slots[_moveFrom] = default;
        }
        if (Shown) Native.Call(_world, Field("RefreshVipWarehouse", "RefreshClanWarehouse"));
        Sync();
    }

    private void AskClanGold(bool deposit)
    {
        if (Native.Get<bool>(_world, "_clanWhInFlight") || !Native.Get<bool>(_world, "_clanWhLoaded")
            || !deposit && !Officer || Amount == null) return;
        int stored = Native.Get<int>(_world, "_clanWhMoney"), carried = Net.I.Sheet.Gold;
        int maximum = Math.Min(deposit ? carried : stored, CoinMax - (deposit ? stored : carried));
        if (maximum <= 0) return;
        Amount.Open(null, deposit ? "Deposit gold" : "Withdraw gold", $"Available {maximum:n0}", maximum, maximum, n =>
        {
            if (!Shown || Native.Get<LineEdit>(_world, "_clanWhGoldInput") is not { } input) return;
            input.Text = n.ToString();
            SetStatus("");
            Native.Call(_world, "ClanWhGoldTransfer", deposit);
        });
    }

    // VIP PIN -----------------------------------------------------------------------------------

    /// <summary>Shows the client's PIN requests in a named PIN window instead of its generic dialog.</summary>
    private void PreparePinPrompt()
    {
        var dialog = Native.Get<AcceptDialog>(_world, "_vipWhPinDlg");
        var message = Native.Get<Label>(_world, "_vipWhPinPrompt");
        var input = Native.Get<LineEdit>(_world, "_vipWhPinEdit");
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
            Native.Call(_world, "SubmitVipPin");
            if (!reopened) prompt.Hide();
        };
    }
}
