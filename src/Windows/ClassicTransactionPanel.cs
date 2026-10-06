using Godot;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic.Layout;
using BagSlot = KnightOnlineUiClassic.Layout.ItemSlot;
namespace KnightOnlineUiClassic.Windows;

/// <summary>OpenKO's two trade districts, composed with live LibreKO trade and inventory callbacks.</summary>
public partial class ClassicVendorPanel : Control
{
    private readonly HudWindow _window;
    private readonly DropWell _nativeWell;
    private readonly ServicePager _pager;
    private readonly FooterBand _footer;
    private readonly StatusLabel _status;
    private readonly Label _currency, _walletCaption, _capacity, _pageCaption;
    private readonly DetailStrip _detail;
    private readonly Button _nativeBuy, _close;
    private readonly Button[] _arrows;
    private readonly TextureRect _banner, _coin;
    private readonly List<ClassicVendorSlot> _frames = new();
    private readonly List<ClassicTradeBagSlot> _bag = new();
    private HudWindow? _inventory;
    private QuantityPrompt? _quantity;
    private ClassicTradeApproval? _confirmation;
    private bool _inventoryWasOpen, _wasVisible, _bagDirty = true;
    private string? _lastCurrency;
    private double _refreshClock = 1;
    private int _lastGroup = -1;
    private readonly int[] _shownIds = new int[24];
    public IReadOnlyList<ClassicTradeBagSlot> BagCells => _bag;
    public bool TransactionBlocked => _window.HasMeta("vendor_blocked") && _window.GetMeta("vendor_blocked").AsCallable().Call().AsBool();
    public bool TradeBlocked => TransactionBlocked || _window.HasMeta("vendor_dead") && _window.GetMeta("vendor_dead").AsCallable().Call().AsBool();
    public bool ModalOpen => TransactionBlocked || _quantity?.Visible == true || IsInstanceValid(_confirmation) && !_confirmation!.IsQueuedForDeletion();

    public ClassicVendorPanel(HudWindow window, Control source, PanelContainer header, Label title, Button close)
    {
        _window = window;
        Name = "classic_vendor"; Size = CustomMinimumSize = ClassicVendorLayout.Size; MouseFilter = MouseFilterEnum.Stop;
        var nodes = CharacterDetailsSkin.Tree(source).ToArray();
        _detail = nodes.OfType<DetailStrip>().Single(); _nativeBuy = _detail.Right.GetChildren().OfType<Button>().Single();
        _footer = nodes.OfType<FooterBand>().Single(); _status = CharacterDetailsSkin.Tree(_footer).OfType<StatusLabel>().Single();
        _nativeWell = nodes.OfType<DropWell>().Single(); var grid = nodes.OfType<GridContainer>().Single();
        _pager = nodes.OfType<ServicePager>().Single();
        var empty = nodes.OfType<Label>().Single(l => l.GetParent() is VBoxContainer v && v.GetParent() == _nativeWell);
        var walletLabels = CharacterDetailsSkin.Tree(nodes.OfType<MoneyPlaque>().Single()).OfType<Label>().ToArray(); _currency = walletLabels[0];
        AddChild(new ClassicVendorFrame { Size = Size });
        var banner = Plugin.Kit.Layout("{nation}_transaction_us").Find("img_store")!;
        _banner = new TextureRect { Position = ClassicVendorLayout.Banner.Position, Size = ClassicVendorLayout.Banner.Size,
            Texture = new AtlasTexture { Atlas = Plugin.Kit.Texture(banner.Texture!), Region = new Rect2(banner.SrcX, banner.SrcY, banner.SrcW, banner.SrcH) },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_banner);
        ClassicVendorSkin.Move(header, this, ClassicVendorLayout.Title);
        foreach (var child in header.GetChildren().OfType<Control>()) child.Visible = false;
        header.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        // Retain HudLayout's original connected drag handle when restyling the header.
        var drag = header.GetChildren().OfType<HBoxContainer>().Single();
        foreach (var child in drag.GetChildren().OfType<Control>()) child.Visible = false;
        ClassicVendorSkin.Move(drag, this, ClassicVendorLayout.DragHandle);
        ClassicVendorSkin.Move(title, this, ClassicVendorLayout.Title); ClassicVendorSkin.Font(title, 13);
        title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        title.MouseFilter = MouseFilterEnum.Ignore;
        _close = close; ClassicVendorSkin.Move(close, this, ClassicVendorLayout.Close);
        ClassicVendorSkin.Button(close, Plugin.Kit.Layout("{nation}_transaction_us").Find("btn_close")); close.Text = "";
        int i = 0;
        foreach (var cell in grid.GetChildren().OfType<ItemSlotView>().ToArray())
        {
            ClassicVendorSkin.Move(cell, this, ClassicVendorLayout.CatalogueCell(i));
            cell.MouseFilter = MouseFilterEnum.Ignore;
            var frame = new ClassicVendorSlot(cell); cell.AddChild(frame); _frames.Add(frame);
            var hit = new ClassicTradeCatalogueSlot(cell, this) { Name = $"trade_catalogue_{i}" }; cell.AddChild(hit);
            var countOverlay = new Control { Name = $"trade_count_overlay_{i}", MouseFilter = MouseFilterEnum.Ignore,
                Position = ClassicVendorLayout.CatalogueCell(i).Position, Size = ClassicVendorLayout.CatalogueCell(i).Size };
            AddChild(countOverlay);
            ItemCountStyle.Apply(cell.CountLabel, countOverlay, ClassicNpcLayout.BodyFont);
            hit.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); i++;
        }
        bool visible = empty.Visible; ClassicVendorSkin.Move(empty, this, ClassicVendorLayout.Empty); empty.Visible = visible;
        ClassicVendorSkin.Font(empty); empty.HorizontalAlignment = HorizontalAlignment.Center;
        var pageLabel = _pager.GetChildren().OfType<Label>().Single(); _pageCaption = pageLabel;
        ClassicVendorSkin.Move(pageLabel, this, ClassicVendorLayout.PageLabel); ClassicVendorSkin.Font(pageLabel, 11);
        pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        var arrows = _pager.GetChildren().OfType<Button>().ToArray();
        _arrows = arrows;
        for (i = 0; i < 2; i++)
        {
            ClassicVendorSkin.Move(arrows[i], this, ClassicVendorLayout.PageArrow(i));
            ClassicVendorSkin.Button(arrows[i], Plugin.Kit.Layout("{nation}_transaction_us").Find(i == 0 ? "btn_page_up" : "btn_page_down")); arrows[i].Text = "";
        }
        var coinArt = Plugin.Kit.Layout("{nation}_transaction_us").Find("btn_gold")!.Images.First(n => n.Tag == 0);
        _coin = new TextureRect { Name = "trade_coin", Texture = new AtlasTexture { Atlas = Plugin.Kit.Texture(coinArt.Texture!),
            Region = new Rect2(coinArt.SrcX, coinArt.SrcY, coinArt.SrcW, coinArt.SrcH) }, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore, Position = ClassicVendorLayout.Coin.Position, Size = ClassicVendorLayout.Coin.Size };
        AddChild(_coin);
        _walletCaption = ClassicNpcLayout.Text("NP", 11, false, ClassicDesign.Heading); AddChild(_walletCaption);
        ClassicNpcLayout.Place(_walletCaption, ClassicVendorLayout.WalletCaption);
        ClassicVendorSkin.Move(walletLabels[1], this, ClassicVendorLayout.WalletValue); ClassicVendorSkin.Font(walletLabels[1], 12);
        walletLabels[1].HorizontalAlignment = HorizontalAlignment.Right;
        _capacity = ClassicNpcLayout.Text("", 11, false, ClassicDesign.Heading); AddChild(_capacity);
        ClassicNpcLayout.Place(_capacity, ClassicVendorLayout.Capacity);
        AddChild(new ClassicSurface(true) { Position = ClassicVendorLayout.Divider.Position, Size = ClassicVendorLayout.Divider.Size });
        var bagHeading = ClassicNpcLayout.Text("Inventory", 12, true, ClassicDesign.Heading); AddChild(bagHeading);
        ClassicNpcLayout.Place(bagHeading, ClassicVendorLayout.InventoryHeading);
        for (i = 0; i < Plugin.Kit.Game.Inventory.GridCount; i++)
        {
            int slot = Plugin.Kit.Game.Inventory.GridStart + i;
            var cell = new ClassicTradeBagSlot(slot, this) { Name = $"trade_bag_{i}", Position = ClassicVendorLayout.BagCell(i).Position, Size = ClassicVendorLayout.BagCell(i).Size,
                Source = s => Plugin.Kit.Game.Inventory.At(s),
                OnDrop = (from, to) => { if (!ModalOpen) Plugin.Kit.Game.Inventory.Move(from, to); },
                CanCompanionDrop = data => !ModalOpen && !TradeBlocked && ValidCatalogueItem(data) && NativeBagCell(slot)?._CanDropData(Vector2.Zero, data) == true,
                OnCompanionDrop = data => BuyInto(data, slot),
                OnHover = (s, over) => { if (over) Plugin.Kit.Game.Inventory.ShowTooltip(s); else Plugin.Kit.Game.Inventory.HideTooltip(); } };
            AddChild(cell); _bag.Add(cell);
        }
        ClassicVendorSkin.Move(_status, this, ClassicVendorLayout.Status); ClassicVendorSkin.Font(_status, 10);
        _status.HorizontalAlignment = HorizontalAlignment.Center; _footer.Hint = "Right-click or drag to buy / sell";
        _window.SetMeta("vendor_confirmation", Callable.From<Godot.Collections.Dictionary, Callable, Callable>((details, accept, cancel) =>
        {
            CancelHeldItem(); Plugin.Kit.Game.Inventory.HideTooltip();
            _confirmation = new ClassicTradeApproval(_window, details, accept, cancel);
            AddChild(_confirmation);
        }));
        _window.VisibilityChanged += () => { if (_window.Visible) _inventoryWasOpen = Plugin.Kit.Game.Windows.IsOpen("inventory"); };
    }
    internal Control? NativeBagCell(int slot)
    {
        if (_inventory == null || !IsInstanceValid(_inventory))
            _inventory = CharacterDetailsSkin.Tree(GetTree().Root).OfType<HudWindow>().FirstOrDefault(w => w.Id == "inventory");
        var grid = _inventory == null ? null : CharacterDetailsSkin.Tree(_inventory.Body).OfType<GridContainer>()
            .FirstOrDefault(g => g.Columns == 7 && g.GetChildCount() >= Plugin.Kit.Game.Inventory.GridCount);
        return grid?.GetChild(slot - Plugin.Kit.Game.Inventory.GridStart) as Control;
    }
    public void BindQuantity(QuantityPrompt quantity) => _quantity = quantity;
    public bool CanSell(Variant data) => !ModalOpen && !TradeBlocked && _currency.Text.Length == 0 && ValidBagItem(data) && _nativeWell.CanDrop?.Invoke(data) == true;
    public bool ValidBagItem(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var payload = data.AsGodotDictionary();
        if (!payload.ContainsKey("invFrom")) return false;
        int slot = payload["invFrom"].AsInt32();
        if (slot < Plugin.Kit.Game.Inventory.GridStart || slot >= Plugin.Kit.Game.Inventory.GridStart + Plugin.Kit.Game.Inventory.GridCount) return false;
        var item = Plugin.Kit.Game.Inventory.At(slot);
        return !item.IsEmpty && (!payload.ContainsKey("id") || payload["id"].AsInt32() == item.ItemId);
    }
    private bool ValidCatalogueItem(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var payload = data.AsGodotDictionary();
        if (!payload.ContainsKey("companionFrom") || !payload.ContainsKey("id")) return false;
        int index = payload["companionFrom"].AsInt32();
        return index >= 0 && index < _frames.Count && _frames[index].OwnerCell.Item.ItemId == payload["id"].AsInt32() && payload["id"].AsInt32() != 0;
    }
    public void Sell(Variant data)
    {
        if (ModalOpen || TradeBlocked || !ValidBagItem(data) || _nativeWell.CanDrop?.Invoke(data) != true) return;
        CancelHeldItem(); Plugin.Kit.Game.Inventory.HideTooltip();
        _nativeWell.Dropped?.Invoke(data);
    }
    private void BuyInto(Variant data, int destination)
    {
        if (ModalOpen || TradeBlocked || !ValidCatalogueItem(data)) return;
        int index = data.AsGodotDictionary()["companionFrom"].AsInt32();
        var cell = CharacterDetailsSkin.Tree(this).OfType<ItemSlotView>().FirstOrDefault(c => c.Index == index);
        if (cell == null || cell.Item.IsEmpty) return;
        if (ItemData.Get(cell.Item.ItemId)?.Countable != 0)
        {
            // OpenKO merges a matching stack before using the drop destination.
            int stack = Enumerable.Range(Plugin.Kit.Game.Inventory.GridStart, Plugin.Kit.Game.Inventory.GridCount)
                .FirstOrDefault(s => Plugin.Kit.Game.Inventory.At(s) is var item && item.ItemId == cell.Item.ItemId && item.Count < Inventory.StackMax, -1);
            if (stack >= 0) destination = stack;
        }
        NativeBagCell(destination)?._DropData(Vector2.Zero, data);
    }
    public void CancelHeldItem()
    {
        if (IsInsideTree() && GetViewport().GuiIsDragging()) GetViewport().GuiCancelDrag();
        foreach (var frame in _frames) frame.OwnerCell.EmitSignal(Control.SignalName.MouseExited);
    }
    public void Page(int delta) { if (!ModalOpen) { CancelHeldItem(); _pager.Step(delta); } }
    public void BuyPrice(ItemSlotView source)
    {
        if (source.Item.IsEmpty) return;
        var layer = CharacterDetailsSkin.Tree(GetTree().Root).OfType<CanvasLayer>().FirstOrDefault(l => l.Layer == 85);
        var stack = layer == null ? null : CharacterDetailsSkin.Tree(layer).OfType<VBoxContainer>().FirstOrDefault();
        if (stack == null || !stack.IsVisibleInTree()) return;
        foreach (var old in stack.GetChildren().OfType<Label>().Where(l => l.Name == "trade_buy_price").ToArray()) { stack.RemoveChild(old); old.QueueFree(); }
        var price = ClassicNpcLayout.Text($"Buying Price : {ItemData.BuyPrice(source.Item.ItemId):n0} {(_currency.Text.Length == 0 ? "gold" : "NP")}", 11, true, ClassicDesign.Heading);
        price.Name = "trade_buy_price"; stack.AddChild(price); stack.MoveChild(price, Math.Min(3, stack.GetChildCount()-1));
    }
    private void InventoryChanged() => _bagDirty = true;
    public override void _EnterTree()
    {
        Plugin.Kit.Game.Inventory.Changed += InventoryChanged; Plugin.Kit.Game.Character.Changed += InventoryChanged;
    }
    public override void _ExitTree()
    {
        Plugin.Kit.Game.Inventory.Changed -= InventoryChanged; Plugin.Kit.Game.Character.Changed -= InventoryChanged;
    }
    public override void _Process(double delta)
    {
        bool shown = IsVisibleInTree();
        if (shown && !_wasVisible) _bagDirty = true;
        if (!shown && _wasVisible)
        {
            if (IsInstanceValid(_confirmation)) _confirmation!.Cancel(); _confirmation = null;
            if (_inventoryWasOpen) Callable.From(() => Plugin.Kit.Game.Windows.Open("inventory")).CallDeferred();
        }
        _wasVisible = shown; if (!shown) return;
        bool blocked = ModalOpen;
        _close.Disabled = blocked;
        _arrows[0].Disabled = blocked || _pager.Page == 0;
        _arrows[1].Disabled = blocked || _pager.Page >= _pager.Pages - 1;
        int group = _window.GetMeta("vendor_group", 0).AsInt32();
        if (_lastGroup != group)
        {
            _lastGroup = group;
            _footer.ResetStatus();
            var sign = Plugin.Kit.Layout("{nation}_transaction_us").Find(group / 1000 is 122 or 222 ? "img_blacksmith" : "img_store")!;
            _banner.Texture = new AtlasTexture { Atlas = Plugin.Kit.Texture(sign.Texture!), Region = new Rect2(sign.SrcX, sign.SrcY, sign.SrcW, sign.SrcH) };
            CancelHeldItem();
        }
        bool changed = false;
        for (int i = 0; i < _frames.Count; i++)
        {
            int id = _frames[i].OwnerCell.Item.ItemId;
            changed |= _shownIds[i] != id; _shownIds[i] = id;
        }
        if (changed) CancelHeldItem();
        _pageCaption.Text = $"{_pager.Page + 1}/{_pager.Pages}";
        _status.TooltipText = _status.Text;
        _coin.Visible = _currency.Text.Length == 0;
        _walletCaption.Visible = !_coin.Visible;
        if (_lastCurrency != _currency.Text) { _lastCurrency = _currency.Text; _footer.ResetStatus(); }
        if (Plugin.Kit.Game.Windows.IsOpen("inventory")) Plugin.Kit.Game.Windows.Close("inventory");
        foreach (var frame in _frames) { frame.OwnerCell.Look = SlotLook.Normal; frame.UpdateFrame(); }
        _refreshClock += delta;
        if (_bagDirty || _refreshClock >= .25)
        {
            _bagDirty = false; _refreshClock = 0;
            foreach (var cell in _bag) cell.Refresh();
            var who = Plugin.Kit.Game.Character;
            _capacity.Text = $"Weight {who.Weight / 10f:0.0} / {who.MaxWeight / 10f:0.0}";
        }
    }
    public override void _Input(InputEvent ev)
    {
        if (!IsVisibleInTree() || ModalOpen || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape) { if (GetViewport().GuiIsDragging()) CancelHeldItem(); else _close.EmitSignal(BaseButton.SignalName.Pressed); GetViewport().SetInputAsHandled(); }
        if (key.Keycode is Key.Pageup or Key.Pagedown) { Page(key.Keycode == Key.Pageup ? -1 : 1); GetViewport().SetInputAsHandled(); }
    }
}

public partial class ClassicTradeCatalogueSlot : Control
{
    private readonly ItemSlotView _source; private readonly ClassicVendorPanel _owner;
    public ClassicTradeCatalogueSlot(ItemSlotView source, ClassicVendorPanel owner)
    {
        _source = source; _owner = owner; MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { if (!_owner.ModalOpen) { _source.EmitSignal(Control.SignalName.MouseEntered); _owner.BuyPrice(_source); } };
        MouseExited += () => _source.EmitSignal(Control.SignalName.MouseExited);
    }
    public override void _GuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true } mb || _owner.ModalOpen || _owner.TradeBlocked) return;
        if (mb.ButtonIndex == MouseButton.Left && !_source.Item.IsEmpty)
        {
            var data = _source.DragOut!(_source); int id = _source.Item.ItemId;
            _source.EmitSignal(Control.SignalName.MouseExited);
            // Starting inside the press callback would also trigger Godot's immediate-drop path.
            Callable.From(() => { if (IsVisibleInTree() && !_owner.ModalOpen && !_owner.TradeBlocked && _source.Item.ItemId == id) ForceDrag(data, ClassicTradeDrag.Preview(id)); }).CallDeferred();
            AcceptEvent();
        }
        else if (mb.ButtonIndex == MouseButton.Right && !_source.Item.IsEmpty)
        {
            _owner.CancelHeldItem(); Plugin.Kit.Game.Inventory.HideTooltip();
            _source._GuiInput(mb); AcceptEvent();
        }
        else if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        { _owner.Page(mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1); AcceptEvent(); }
    }
    public override bool _CanDropData(Vector2 position, Variant data) => _owner.CanSell(data);
    public override void _DropData(Vector2 position, Variant data) => _owner.Sell(data);
}

public partial class ClassicTradeBagSlot : BagSlot
{
    private readonly ClassicVendorPanel _owner;
    private readonly StyleBoxTexture _socket;
    public ClassicTradeBagSlot(int slot, ClassicVendorPanel owner) : base(slot)
    {
        _owner = owner;
        _socket = new StyleBoxTexture { Texture = Plugin.Kit.Texture(ClassicDesign.Karus ? "ui_ka_transaction_person trade_us.png" : "ui_el_transaction_person trade_us.png"),
            RegionRect = ClassicDesign.Karus ? new Rect2(169,57,50,50) : new Rect2(170,58,50,50), DrawCenter = false };
        foreach(var side in new[]{Side.Left,Side.Top,Side.Right,Side.Bottom}) _socket.SetTextureMargin(side,1);
        foreach(var label in CharacterDetailsSkin.Tree(this).OfType<Label>()) label.AddThemeFontOverride("font",ClassicNpcLayout.BodyFont);
    }
    public override void _GuiInput(InputEvent ev)
    {
        if (_owner.ModalOpen || _owner.TradeBlocked || ev is not InputEventMouseButton { Pressed: true } mb || Current.IsEmpty) return;
        if (mb.ButtonIndex is not (MouseButton.Left or MouseButton.Right)) return;
        OnHover?.Invoke(Slot, false);
        var data = new Godot.Collections.Dictionary { {"invFrom",Slot},{"id",Current.ItemId} }; int id = Current.ItemId;
        if (mb.ButtonIndex == MouseButton.Right) { _owner.Sell(data); AcceptEvent(); return; }
        Callable.From(() => { if (IsVisibleInTree() && !_owner.ModalOpen && !_owner.TradeBlocked && Current.ItemId == id) ForceDrag(data, ClassicTradeDrag.Preview(id)); }).CallDeferred(); AcceptEvent();
    }
    public override bool _CanDropData(Vector2 position, Variant data) => !_owner.ModalOpen && !_owner.TradeBlocked &&
        (data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("companionFrom") || _owner.ValidBagItem(data)) && base._CanDropData(position, data);
    public override void _DropData(Vector2 position, Variant data) { if (_CanDropData(position, data)) base._DropData(position, data); }
    public override void _Draw()
    {
        DrawStyleBox(_socket, new Rect2(Vector2.Zero, Size));
    }
}

internal static class ClassicTradeDrag
{
    public static Control Preview(int itemId)
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        var content = new Control { Position = new Vector2(-22,-22), Size = new Vector2(44,44), MouseFilter = Control.MouseFilterEnum.Ignore }; root.AddChild(content);
        content.AddChild(new TextureRect { Texture = ItemData.Icon(itemId), Size = content.Size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore });
        LibreKO.UpgradeBadge.Show(content,itemId); return root;
    }
}
