using Expression = System.Linq.Expressions.Expression;
using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterAnvil(PluginContext context) =>
        NativeWindows.Prepare("anvil", (window, world) => NativeAnvil.Prepare(window, world));
}

/// <summary>
/// The Classic Magic Anvil adapter. It names the client's bench sockets, adds the retail Cancel and
/// Back actions, the anvil selection window and the carried inventory embedded in the anvil, mirrors
/// the bench state into the metas the Classic anvil skin reads, and applies the Classic placement
/// rules: a socket only accepts a material that can still complete a recipe for the staged origin,
/// staged materials can move between compatible sockets, and a staged material can be returned to a
/// chosen bag cell. The client still owns the selection, previews, confirmation and upgrade.
/// </summary>
public sealed class NativeAnvil
{
    private const int UpgradeSlotCount = 10;
    private const int AccessorySocketCount = 3;
    private const int AccessoryBenchSlots = AccessorySocketCount + 2;
    private const int CompoundBench = 1;
    private const int BagColumns = 7;
    private const float BagCellSize = 48f;
    private const int ChoiceTextWidth = 245;
    private const int ChoiceFontSize = 12;
    private const int ChoiceOutline = 4;
    private const byte ResultSucceeded = 1;
    private const string ChoiceWindowId = "anvil_choice";
    private const string Title = "Magic Anvil";
    private const string SelectText = "You can upgrade your item at the magic anvil. Please select what you want to upgrade.";
    private const string UntradableText = "That item is sealed, rented or in use.";
    private const string MisfitText = "That item or material does not fit this upgrade selection.";
    private const string MaterialsFullText = "All material sockets are full.";
    private const string AccessoryPairText = "Compounding needs three identical accessories.";
    private const string AccessoriesFullText = "All three accessory slots are full.";
    private const string AccessoryOnlyText = "Only accessories can be compounded.";
    private const int AccessoryScrollFirst = 379159000;
    private const int AccessoryScrollLast = 379164000;
    private const int AccessoryTrina = 354000000;
    private const int SlotEarring = 10;
    private const int SlotNecklace = 11;
    private const int SlotRing = 12;
    private const int SlotBelt = 14;

    private static readonly ConditionalWeakTable<World, NativeAnvil> _anvils = new();

    private readonly World _world;
    private readonly List<ItemSlotView> _bag = new();
    private int[] _errorSelection = Array.Empty<int>();

    public HudWindow Window { get; }
    public HudWindow Choice { get; }
    public Button Cancel { get; }
    public Button Back { get; }

    public static NativeAnvil? Of(World world) => _anvils.TryGetValue(world, out var anvil) ? anvil : null;

    public static NativeAnvil? Prepare(HudWindow window, World world)
    {
        if (Of(world) is { } existing) return existing;
        if (!Native.Has(world, "_upgradeItemIds") || !Native.Has(world, "_anvilFooter")) return null;
        var anvil = new NativeAnvil(window, world);
        _anvils.Add(world, anvil);
        return anvil;
    }

    private NativeAnvil(HudWindow window, World world)
    {
        Window = window;
        _world = world;
        NameSockets();
        foreach (string field in new[] { "_itemSockets", "_accessorySockets" })
            if (Native.Get<ItemSlotView?[]>(world, field) is { } sockets)
                for (int i = 0; i < sockets.Length; i++)
                    if (sockets[i] is { } socket)
                    {
                        int index = i;
                        socket.CanDrop = (_, data) => CanStageDrop(index, data);
                        socket.Dropped = (_, data) => StageDrop(index, data);
                    }

        var footer = Native.Get<FooterBand>(world, "_anvilFooter")!;
        Cancel = new Button { Name = "anvil_cancel", Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        Cancel.Pressed += Reset;
        footer.Right.AddChild(Cancel);
        Back = new Button { Name = "anvil_back", Text = "Back", FocusMode = Control.FocusModeEnum.None };
        Back.Pressed += () =>
        {
            if (Locked) return;
            Native.Call(_world, "CloseUpgrade");
            ShowChoice();
        };
        footer.Right.AddChild(Back);
        BuildBag(window.Body);
        Choice = BuildChoice();
        InstallCompanion();
        ReplaceOpenHandler();
        Net.I.ItemMoveResultEvent += OnItemMoveResult;
        world.TreeExiting += () => Net.I.ItemMoveResultEvent -= OnItemMoveResult;
        window.VisibilityChanged += () =>
        {
            if (window.Visible && window.HasMeta("embedded_inventory")) Callable.From(KeepInventoryClosed).CallDeferred();
        };
        NativeWindows.Sync(window, Sync);
        RefreshBag();
    }

    // Native state ------------------------------------------------------------------------------

    private Inventory Inv => Native.Get<Inventory>(_world, "Inv")!;
    private int[] Ids => Native.Get<int[]>(_world, "_upgradeItemIds")!;
    private int[] Positions => Native.Get<int[]>(_world, "_upgradePositions")!;
    private ItemSlotView?[] Sockets => Native.Get<ItemSlotView?[]>(_world, "_upgradeSockets") ?? Array.Empty<ItemSlotView?>();
    private bool Compound => Native.Get<object>(_world, "_anvilBench") is { } bench && Convert.ToInt32(bench) == CompoundBench;
    private int MaterialEnd => Compound ? AccessoryBenchSlots : UpgradeSlotCount;
    private bool Shown => Native.Get<bool>(_world, "_upgradeShown");
    private bool Awaiting => Native.Get<UpgradeSession>(_world, "_upgradeSession") is { Awaiting: true };

    /// <summary>Whether a request, its answer or its result animation currently owns the bench.</summary>
    public bool Locked => Native.Has(_world, "UpgradeInteractionLocked")
        ? Native.Get<bool>(_world, "UpgradeInteractionLocked")
        : Awaiting || Native.Get<object>(_world, "_upgradePendingResult") != null || Native.Get<object>(_world, "_upgradeScanTween") != null;

    private static bool InMainBag(int abs, Inventory inventory) =>
        abs >= Inventory.GridStart && abs < Inventory.GridStart + Inventory.GridCount && abs < inventory.Length;

    private bool IsStaged(int abs) => InMainBag(abs, Inv) && Positions.Contains(abs - Inventory.GridStart);

    /// <summary>Mirrors the server's tradable check: linked, sealed, bound and rented items stay off the anvil.</summary>
    private static bool Tradable(ItemSlot item) => item.UniqueId == 0
        && (ItemFlag)item.Flag is not (ItemFlag.Rented or ItemFlag.CharacterSeal or ItemFlag.Duplicate or ItemFlag.Sealed or ItemFlag.Bound);

    private static bool IsAccessory(int itemId) =>
        ItemData.Get(itemId) is { Countable: 0 } def && def.Slot is SlotEarring or SlotNecklace or SlotRing or SlotBelt;

    private static bool IsAccessoryMaterial(int itemId) =>
        itemId is >= AccessoryScrollFirst and <= AccessoryScrollLast or AccessoryTrina;

    private bool IsUpgradeTarget(int itemId) => Native.Call(_world, "IsUpgradeTarget", itemId) is true;

    // Classic placement -------------------------------------------------------------------------

    /// <summary>Right-click or double-click on a carried item: stage it in the socket it belongs to.</summary>
    public bool Take(int abs)
    {
        if (!InMainBag(abs, Inv)) return false;
        Place(abs);
        return true;
    }

    private void Place(int abs)
    {
        var inventory = Inv;
        if (Locked || !InMainBag(abs, inventory) || inventory[abs].IsEmpty) return;
        if (!Tradable(inventory[abs])) { Error(UntradableText); return; }
        var positions = Positions;
        for (int i = 0; i < positions.Length; i++)
            if (positions[i] == abs - Inventory.GridStart)
            {
                Native.Call(_world, "ClearUpgradeSocket", i);
                RefreshBag();
                return;
            }
        int itemId = inventory[abs].ItemId;
        string problem;
        int target = Compound ? AccessorySocketFor(itemId, out problem) : ItemSocketFor(itemId, out problem);
        if (target < 0) { Error(problem); return; }
        StageInSocket(target, abs);
    }

    private int ItemSocketFor(int itemId, out string problem)
    {
        problem = "";
        if (Ids[0] == 0 && IsUpgradeTarget(itemId)) return 0;
        if (AnvilPlacementRules.IsMaterial(itemId, false))
        {
            int free = FirstEmptySocket(1, UpgradeSlotCount);
            if (free < 0) problem = MaterialsFullText;
            return free;
        }
        problem = Native.Call(_world, "UpgradePlacementError", itemId) as string ?? MisfitText;
        return -1;
    }

    private int AccessorySocketFor(int itemId, out string problem)
    {
        problem = "";
        if (IsAccessory(itemId))
        {
            if (!AccessoryMatches(itemId, -1)) { problem = AccessoryPairText; return -1; }
            int free = FirstEmptySocket(0, AccessorySocketCount);
            if (free < 0) problem = AccessoriesFullText;
            return free;
        }
        if (IsAccessoryMaterial(itemId))
        {
            int free = FirstEmptySocket(AccessorySocketCount, AccessoryBenchSlots);
            if (free < 0) problem = MaterialsFullText;
            return free;
        }
        problem = AccessoryOnlyText;
        return -1;
    }

    private bool AccessoryMatches(int itemId, int except)
    {
        var ids = Ids;
        for (int i = 0; i < AccessorySocketCount; i++)
            if (i != except && ids[i] != 0 && ids[i] != itemId) return false;
        return true;
    }

    private int FirstEmptySocket(int from, int to)
    {
        var ids = Ids;
        for (int i = from; i < to; i++)
            if (ids[i] == 0) return i;
        return -1;
    }

    private bool SocketAccepts(int socket, int itemId)
    {
        if (Compound)
            return socket < AccessorySocketCount
                ? IsAccessory(itemId) && AccessoryMatches(itemId, socket)
                : socket < AccessoryBenchSlots && AnvilPlacementRules.IsMaterial(itemId, true);
        if (socket == 0) return IsUpgradeTarget(itemId) && !IsAccessory(itemId);
        return AnvilPlacementRules.IsMaterial(itemId, false);
    }

    private bool SelectionAllowed(int[] items) => AnvilPlacementRules.Allows(items, Compound);

    private bool CanStageDrop(int socket, Variant data)
    {
        if (Locked || data.VariantType != Variant.Type.Dictionary) return false;
        if (socket < 0 || socket >= MaterialEnd) return false;
        var d = data.AsGodotDictionary();
        var ids = Ids;
        if (d.ContainsKey("companionFrom"))
        {
            int from = d["companionFrom"].AsInt32();
            if (!(from >= 0 && from < MaterialEnd && from != socket && ids[from] != 0
                  && SocketAccepts(socket, ids[from])
                  && (ids[socket] == 0 || SocketAccepts(from, ids[socket])))) return false;
            var swapped = (int[])ids.Clone();
            (swapped[from], swapped[socket]) = (swapped[socket], swapped[from]);
            return SelectionAllowed(swapped);
        }
        if (!d.ContainsKey("invFrom")) return false;
        int abs = d["invFrom"].AsInt32();
        var inventory = Inv;
        if (!(InMainBag(abs, inventory) && !inventory[abs].IsEmpty && Tradable(inventory[abs]) && !IsStaged(abs)
              && SocketAccepts(socket, inventory[abs].ItemId))) return false;
        var proposed = (int[])ids.Clone();
        proposed[socket] = inventory[abs].ItemId;
        return SelectionAllowed(proposed);
    }

    private void StageDrop(int socket, Variant data)
    {
        if (!CanStageDrop(socket, data)) return;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("companionFrom"))
        {
            StageInSocket(socket, d["invFrom"].AsInt32());
            return;
        }
        int from = d["companionFrom"].AsInt32();
        var ids = Ids;
        var positions = Positions;
        var sockets = Sockets;
        var inventory = Inv;
        (ids[from], ids[socket]) = (ids[socket], ids[from]);
        (positions[from], positions[socket]) = (positions[socket], positions[from]);
        foreach (int index in new[] { from, socket })
        {
            if (index >= sockets.Length) continue;
            if (ids[index] == 0) sockets[index]?.Clear();
            else
            {
                var item = inventory[Inventory.GridStart + positions[index]];
                item.Count = 1;
                sockets[index]?.Set(item);
            }
        }
        BenchChanged();
    }

    private void StageInSocket(int socket, int abs)
    {
        var item = Inv[abs];
        var proposed = (int[])Ids.Clone();
        proposed[socket] = item.ItemId;
        if (!Tradable(item) || !SocketAccepts(socket, item.ItemId) || !SelectionAllowed(proposed))
        {
            Error(MisfitText);
            return;
        }
        Ids[socket] = item.ItemId;
        Positions[socket] = abs - Inventory.GridStart;
        item.Count = 1;
        var sockets = Sockets;
        if (socket < sockets.Length) sockets[socket]?.Set(item);
        BenchChanged();
    }

    private void BenchChanged()
    {
        Window.RemoveMeta("anvil_error");
        Native.Call(_world, "RefreshBagFit");
        Native.Call(_world, "OnUpgradeBenchChanged");
        RefreshBag();
    }

    /// <summary>
    /// Drops a staged material on a bag cell: the socket is released and a single item moves to that
    /// cell. A stack stays where it is, because the server does not split stacks within the bag.
    /// </summary>
    private bool IntoBag(int socket, int abs)
    {
        var positions = Positions;
        if (Locked || socket < 0 || socket >= MaterialEnd || !InMainBag(abs, Inv) || positions[socket] < 0) return false;
        int from = Inventory.GridStart + positions[socket];
        Native.Call(_world, "ClearUpgradeSocket", socket);
        if (from != abs)
        {
            for (int other = 0; other < positions.Length; other++)
                if (positions[other] == abs - Inventory.GridStart) Native.Call(_world, "ClearUpgradeSocket", other);
            if (Inv[from].Count <= 1) Native.Call(_world, "MoveBetween", from, abs);
        }
        RefreshBag();
        return true;
    }

    /// <summary>Retail Cancel: clears the bench and keeps the anvil open.</summary>
    public void Reset()
    {
        if (Locked) return;
        Window.RemoveMeta("anvil_error");
        Native.Call(_world, "DismissUpgradeConfirm");
        Native.Call(_world, "ClearUpgradeSockets");
        Native.Call(_world, "ShowAnvilPrompt");
        Native.Call(_world, "RefreshBagFit");
        RefreshBag();
    }

    private void Error(string text)
    {
        Native.Get<FooterBand>(_world, "_anvilFooter")?.Status(text, bad: true);
        Window.SetMeta("anvil_error", text);
        _errorSelection = (int[])Ids.Clone();
    }

    // Embedded inventory ------------------------------------------------------------------------

    private void BuildBag(Control root)
    {
        var grid = new GridContainer { Name = "anvil_inventory", Columns = BagColumns, Visible = false };
        root.AddChild(grid);
        for (int i = 0; i < Inventory.GridCount; i++)
        {
            int abs = Inventory.GridStart + i;
            var cell = new ItemSlotView(BagCellSize) { Name = "anvil_bag_" + i, Index = abs };
            cell.RightClicked += _ => Take(abs);
            cell.DoubleClicked += _ => Take(abs);
            cell.Hovered += s => Native.Call(_world, "ShowItemTooltip", abs, s.Item, "");
            cell.Unhovered += _ => Native.Call(_world, "HideItemTooltip");
            cell.DragOut = _ => Locked ? default(Variant) : new Godot.Collections.Dictionary { { "invFrom", abs } };
            cell.CanDrop = (_, data) => CanDropInBag(abs, data);
            cell.Dropped = (_, data) => DropInBag(abs, data);
            cell.SetDragForwarding(Callable.From<Vector2, Variant>(at => DragFromBag(cell, at)),
                Callable.From<Vector2, Variant, bool>((at, data) => cell._CanDropData(at, data)),
                Callable.From<Vector2, Variant>((at, data) => cell._DropData(at, data)));
            grid.AddChild(cell);
            _bag.Add(cell);
        }
    }

    /// <summary>Starts a bag drag with the Classic preview: the icon keeps its size and grab point.</summary>
    private static Variant DragFromBag(ItemSlotView cell, Vector2 at)
    {
        if (cell.Item.IsEmpty || cell.DragOut?.Invoke(cell) is not { VariantType: not Variant.Type.Nil } data) return default;
        DragLayer.Show(cell, DragPreview(cell, at));
        return data;
    }

    /// <summary>The drag preview for an embedded bag cell pressed at <paramref name="pressed"/>.</summary>
    public static Control DragPreview(ItemSlotView cell, Vector2 pressed)
    {
        var icon = Native.Get<TextureRect>(cell, "_icon")!;
        return KnightOnlineUiClassic.Layout.IconDragPreview.Create(icon, KnightOnlineUiClassic.Layout.IconDragPreview.GrabPoint(cell, icon, pressed));
    }

    private bool CanDropInBag(int abs, Variant data)
    {
        if (Locked || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("companionFrom")) return true;
        if (!d.ContainsKey("invFrom")) return false;
        int from = d["invFrom"].AsInt32();
        return InMainBag(from, Inv) && from != abs;
    }

    private void DropInBag(int abs, Variant data)
    {
        if (!CanDropInBag(abs, data)) return;
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("companionFrom"))
        {
            IntoBag(d["companionFrom"].AsInt32(), abs);
            return;
        }
        int from = d["invFrom"].AsInt32();
        var positions = Positions;
        for (int socket = 0; socket < positions.Length; socket++)
            if (positions[socket] == from - Inventory.GridStart || positions[socket] == abs - Inventory.GridStart)
                Native.Call(_world, "ClearUpgradeSocket", socket);
        Native.Call(_world, "MoveBetween", from, abs);
        RefreshBag();
    }

    /// <summary>Shows the carried items, less the units staged on the bench.</summary>
    public void RefreshBag()
    {
        foreach (var cell in _bag)
        {
            var item = Native.Call(_world, "SlotAt", cell.Index) is ItemSlot shown ? shown : default;
            if (IsStaged(cell.Index))
            {
                if (item.Count > 1) item.Count--;
                else item = default;
            }
            if (!cell.Item.Equals(item)) cell.Set(item);
            if (cell.Look != SlotLook.Normal) cell.Look = SlotLook.Normal;
        }
    }

    private void OnItemMoveResult(bool ok)
    {
        if (Shown) RefreshBag();
    }

    private void KeepInventoryClosed()
    {
        if (!Window.Visible || Native.Get<object>(_world, "_bagPairing") is not { } pairing) return;
        if (!Native.Get<bool>(pairing, "_openedByCompanion")) return;
        Native.Set(pairing, "_openedByCompanion", false);
        Native.Call(_world, "HideMainWindow", "Inventory");
    }

    /// <summary>
    /// Replaces the anvil's bag companion before it is first created, so the Classic rules also apply
    /// to the main inventory: right-click stages, unrelated items keep their normal look.
    /// </summary>
    private void InstallCompanion()
    {
        if (Native.Get<object>(_world, "_anvilCompanion") != null) return;
        var companionType = Native.ClientType("LibreKO.World+BagCompanion");
        var fitType = Native.ClientType("LibreKO.World+BagFit");
        if (companionType == null || fitType == null) return;
        int normal = Convert.ToInt32(Enum.Parse(fitType, "Normal"));
        int staged = Convert.ToInt32(Enum.Parse(fitType, "Staged"));
        Func<int, int> fitOf = abs => abs >= Inventory.GridStart && IsStaged(abs) ? staged : normal;
        var slot = Expression.Parameter(typeof(int), "abs");
        var fit = Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(int), fitType),
            Expression.Convert(Expression.Invoke(Expression.Constant(fitOf), slot), fitType), slot).Compile();
        Func<int, bool> take = Take;
        Func<int, string> note = _ => "";
        Action close = () => Native.Call(_world, "CloseUpgrade");
        Func<int, int, bool> intoBag = IntoBag;
        Native.Set(_world, "_anvilCompanion", Activator.CreateInstance(companionType, take, fit, note, close, intoBag));
    }

    // Selection window --------------------------------------------------------------------------

    private HudWindow BuildChoice()
    {
        var choice = new HudWindow(ChoiceWindowId, Title) { Visible = false };
        choice.Closed += () => choice.Visible = false;
        var description = new Label
        {
            Text = SelectText,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(ChoiceTextWidth, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        description.AddThemeFontSizeOverride("font_size", ChoiceFontSize);
        description.AddThemeColorOverride("font_color", Colors.White);
        description.AddThemeColorOverride("font_outline_color", Colors.Black);
        description.AddThemeConstantOverride("outline_size", ChoiceOutline);
        choice.Body.AddChild(description);
        foreach (var (text, bench) in new[] { ("Upgrade Item", 0), ("Compound Accessory", CompoundBench) })
        {
            var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => OpenBench(bench);
            choice.Body.AddChild(button);
        }
        var leave = new Button { Text = "Walk away", FocusMode = Control.FocusModeEnum.None };
        leave.Pressed += () => choice.Visible = false;
        choice.Body.AddChild(leave);
        Native.Get<CanvasLayer>(_world, "_upgradeLayer")?.AddChild(choice);
        return choice;
    }

    public void ShowChoice() => Choice.Visible = true;

    private void OpenBench(int bench)
    {
        Choice.Visible = false;
        if (Native.ClientType("LibreKO.World+AnvilBench") is { } type)
            Native.Call(_world, "OpenAnvilBench", Enum.ToObject(type, bench));
    }

    /// <summary>The anvil NPC opens the Classic selection window instead of the generic NPC menu.</summary>
    private void ReplaceOpenHandler()
    {
        var net = Net.I;
        if (Delegate.CreateDelegate(typeof(Action<int>), _world, "OnUpgradeOpen", false, false) is not Action<int> native) return;
        net.UpgradeOpenEvent -= native;
        net.UpgradeOpenEvent += Open;
        _world.TreeExiting += () => net.UpgradeOpenEvent -= Open;
    }

    private void Open(int anvilId)
    {
        Native.Set(_world, "_upgradeAnvilId", anvilId);
        Native.Call(_world, "CloseVendor");
        ShowChoice();
    }

    // Per-frame state ---------------------------------------------------------------------------

    private void Sync()
    {
        Window.SetMeta("anvil_busy", Locked);
        Window.SetMeta("anvil_compound", Compound);
        if (Awaiting) Window.RemoveMeta("anvil_scan_success");
        else if (Native.Get<object>(_world, "_upgradePendingResult") is UpgradeResult result)
            Window.SetMeta("anvil_scan_success", result.ResultCode == ResultSucceeded);
        if (Window.HasMeta("anvil_error") && !Ids.AsSpan().SequenceEqual(_errorSelection)) Window.RemoveMeta("anvil_error");
        if (Window.HasMeta("embedded_inventory") && Native.Get<Button>(_world, "_upgradeBtn") is { } action && action.Text != "OK")
            action.Text = "OK";
        RefreshBag();
        foreach (var view in CountedCells()) NativeServices.CountBadge(view);
    }

    private IEnumerable<ItemSlotView> CountedCells()
    {
        foreach (string field in new[] { "_itemSockets", "_accessorySockets" })
            if (Native.Get<ItemSlotView?[]>(_world, field) is { } sockets)
                foreach (var socket in sockets)
                    if (socket != null) yield return socket;
        foreach (string field in new[] { "_itemResultSocket", "_accessoryResultSocket" })
            if (Native.Get<ItemSlotView>(_world, field) is { } socket) yield return socket;
        foreach (var cell in _bag) yield return cell;
    }

    private void NameSockets()
    {
        foreach (var (field, prefix) in new[] { ("_itemSockets", "anvil_item_"), ("_accessorySockets", "anvil_accessory_") })
            if (Native.Get<ItemSlotView?[]>(_world, field) is { } sockets)
                for (int i = 0; i < sockets.Length; i++) NativeWindows.Name(sockets[i], prefix + i);
        NativeWindows.Name(Native.Get<ItemSlotView>(_world, "_itemResultSocket"), "anvil_result_item");
        NativeWindows.Name(Native.Get<ItemSlotView>(_world, "_accessoryResultSocket"), "anvil_result_accessory");
    }
}
