using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO.Domain;
using LibreKO.Plugins;
using ItemSlot = KnightOnlineUiClassic.Layout.ItemSlot;

namespace KnightOnlineUiClassic.Windows;

public partial class InventoryWindow : Control
{
    private const string LayoutName = "{nation}_inventory_us";
    private const int EquipAreaType = 1;
    private const int GridAreaType = 2;
    private const int TitleBarHeight = 30;
    private const string TrashArea = "area_samma";
    private static readonly Vector2 CarrySize = new(36, 36);

    private readonly LayoutView _view;
    private readonly PluginGame _game;
    private readonly WindowHost _host;
    private readonly List<ItemSlot> _slots = new();
    private readonly List<ItemSlot> _bagSlots = new();
    private readonly Dictionary<int, ItemSlot> _bySlot = new();
    private readonly TextureRect _carry;
    private readonly LayoutView _extras;
    private readonly List<BaseButton> _bagButtons = new();
    private InventoryCornerButton _cospreToggle = null!;
    private bool _cospreOpen;
    private int _bag;
    private LineEdit? _search;
    private int _carried = -1;
    private readonly LibreKO.QuantityPrompt _movePrompt=new(220);
    private readonly ClassicInventoryRepair _repair;
    private bool RepairMode => _host.Window.GetMeta("classic_repair_mode", false).AsBool();

    public InventoryWindow(WindowHost host)
    {
        _host = host;
        var kit = Plugin.Kit;
        _game = kit.Game;
        var layout = InventoryLayout.Build(kit.Layout(LayoutName));
        _view = new LayoutView(kit, layout);
        _extras = new LayoutView(kit,InventoryLayout.Drawer()) {Position=new Vector2(0,InventoryLayout.DrawerTop)};
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_view);
        AddChild(_movePrompt);
        ClassicTradeQuantity.Apply(_movePrompt,this);
        if(_view.Get("area_char") is {} portraitArea)
            portraitArea.GetParent().AddChild(new InventoryPortrait(portraitArea.Size) { Position=portraitArea.Position });

        host.SetDragHandle(_view.MakeDragHandle(new Rect2(0, 0, 330, TitleBarHeight)));
        _view.OnPressed("btn_close", host.Close);

        foreach (var (node, control) in _view.Areas(EquipAreaType))
            if (int.TryParse(node.Id, out int equip) && equip < InventoryConstants.SlotMax)
            {
                var cell=BindSlot(control,equip);
                cell.EmptyIcon=InventoryLayout.EquipmentIcon(equip);
                cell.EmptyHint=new[]{"Right earring","Helmet","Left earring","Necklace","Pauldron","Pet","Right hand","Belt","Left hand","Right ring","Pants","Left ring","Gloves","Boots"}[equip];
                _slots.Add(cell);
            }
        foreach (var (node, control) in _view.Areas(GridAreaType))
            _slots.Add(BindSlot(control, InventoryConstants.InventoryStart + int.Parse(node.Id)));

        BuildExtras();

        if (_view.Get(TrashArea) is { } trash)
        {
            var target = new DropTarget
            {
                Position = trash.Position,
                Size = trash.Size,
                OnDrop = RequestDestroy,
                OnClick = () => { if (_carried < 0) return; int from = _carried; CancelCarry(); RequestDestroy(from); },
            };
            trash.GetParent().AddChild(target);
        }

        _carry = new TextureRect
        {
            Visible = false,
            Size = CarrySize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 10,
        };
        AddChild(_carry);
        _repair = new ClassicInventoryRepair(host, _game, _view, CancelCarry);
        AddChild(_repair);
        SetProcess(false);
        SetCospreOpen(false);
        Refresh();
    }

    public override void _Ready()
    {
        _view.ApplyDeclaredBounds();
        _extras.ApplyDeclaredBounds();
        // The field enters the tree with the default font's taller minimum; settle it on the drawer band.
        if(_search!=null) Callable.From(()=>_search.Size=InventoryLayout.BagSearch.Size).CallDeferred();
        if(_extras.GetNodeOrNull<Button>("vip_vault") is {} vault) Callable.From(()=>vault.Size=InventoryLayout.VipVault.Size).CallDeferred();
    }

    private ItemSlot BindSlot(Control area, int slot)
    {
        var cell = new ItemSlot(slot)
        {
            Position = area.Position,
            Size = area.Size,
            Source = s => _game.Inventory.At(s),
            CanDrag = () => !RepairMode,
            PicksAmount = true,
            OnDropAmount = (from, to, amount) => { CancelCarry(); RequestMove(from, to, amount); },
            CanCompanionDrop = data => CompanionCell(slot)?._CanDropData(Vector2.Zero, data) == true,
            OnCompanionDrop = data => { CancelCarry(); CompanionCell(slot)?._DropData(Vector2.Zero, data); },
            OnClick = SlotClicked,
            OnActivate = SlotActivated,
            OnDoubleClick = s => { CancelCarry(); if (!RepairMode && !SkillBarConsumable(s)) _game.Inventory.Use(s); },
            OnHover = HoverSlot,
        };
        area.GetParent().AddChild(cell);
        _bySlot[slot] = cell;
        return cell;
    }

    /// <summary>
    /// A bag consumable the client's right-click puts on the skill bar. The client's desktop bag ignores a double-click,
    /// so a Classic double-click leaves these alone instead of adding another skill-bar copy each time.
    /// </summary>
    private bool SkillBarConsumable(int slot) =>
        slot >= _game.Inventory.GridStart && slot < _game.Inventory.GridStart + _game.Inventory.GridCount
        && Native.NativeSkills.BagClickAddsToHotbar(_game.Inventory.At(slot).ItemId);

    private Control? CompanionCell(int slot)
    {
        if (slot < _game.Inventory.GridStart || slot >= _game.Inventory.GridStart + _game.Inventory.GridCount
            || _host.Window is not LibreKO.HudWindow window) return null;
        // Keep the native service callback and preferred destination, including its current bag pairing.
        var grid = CharacterDetailsSkin.Tree(window.Body).OfType<GridContainer>()
            .FirstOrDefault(g => g.Columns == 7 && g.GetChildCount() >= _game.Inventory.GridCount);
        return grid?.GetChild(slot - _game.Inventory.GridStart) as Control;
    }

    private void SlotClicked(int slot)
    {
        if (RepairMode)
        {
            CancelCarry();
            if (slot < _game.Inventory.GridStart + _game.Inventory.GridCount) _repair.Take(slot);
            return;
        }
        if (_carried < 0)
        {
            if (_game.Inventory.At(slot).IsEmpty) return;
            _carried = slot;
            _carry.Texture = _game.Inventory.At(slot).Icon;
            _carry.Visible = true;
            _carry.Position = GetLocalMousePosition() - CarrySize * 0.5f;
            if (_bySlot.TryGetValue(slot, out var cell)) cell.SetCarried(true);
            _game.Inventory.HideTooltip();
            SetProcess(true);
            return;
        }
        int from = _carried;
        CancelCarry();
        if (from != slot) RequestMove(from, slot);
    }

    private void RequestMove(int from,int to,int? amount=null)
    {
        if (RepairMode) return;
        var item=_game.Inventory.At(from);
        if(item.IsEmpty || from==to) return;
        // A part picked with the mouse wheel moves at once, bag to bag as well (the client splits the stack and
        // refuses a split it cannot make); the whole stack keeps the count prompt of the magic bags.
        if(ClassicStackDial.Part(amount,item.Count) is int part) { _game.Inventory.MoveAmount(from,to,part); return; }
        if((from>=InventoryConstants.MagicBagStart || to>=InventoryConstants.MagicBagStart)
            && LibreKO.Domain.ItemData.Get(item.ItemId)?.Countable>0)
        {
            _game.Inventory.HideTooltip();
            _movePrompt.Open(item.Icon,item.Name,$"Up to {item.Count:n0}",item.Count,item.Count,n=>
            {
                var current=_game.Inventory.At(from);
                if(current.ItemId==item.ItemId && current.Count>=n) _game.Inventory.MoveAmount(from,to,(int)n);
            },"OK");
        }
        else _game.Inventory.Move(from,to);
    }
    private void RequestDestroy(int slot)
    {
        if (RepairMode) return;
        var item=_game.Inventory.At(slot);
        if(item.IsEmpty || slot>=InventoryConstants.CospreStart) { _game.Inventory.Drop(slot);return; }
        _game.Inventory.HideTooltip();
        // The client's bulk destroy queue: an open prompt takes further items until it is confirmed.
        if (NativeInventory.HasDestroyQueue)
        {
            var open=GetChildren().OfType<ClassicInventoryDestroy>().FirstOrDefault(p=>!p.IsQueuedForDeletion());
            if (open!=null) { open.Add(slot); return; }
            if (!NativeInventory.QueueDestroy(slot)) return;
            AddChild(new ClassicInventoryDestroy(_view,DestroyQueueSource));
            return;
        }
        AddChild(new ClassicInventoryDestroy(_view,()=>
        {
            var current=_game.Inventory.At(slot);
            if(current.ItemId==item.ItemId && current.Count==item.Count) _game.Inventory.ConfirmDrop(slot,item.ItemId);
        }));
    }

    private static readonly ClassicDestroyQueue DestroyQueueSource=new(
        ()=>NativeInventory.DestroyEntries.Select(e=>e.Slot).ToArray(),
        NativeInventory.QueueDestroy,NativeInventory.UnqueueDestroy,NativeInventory.ConfirmDestroy,NativeInventory.CancelDestroy,
        NativeInventory.HideDestroyCard);

    private void SlotActivated(int slot)
    {
        if (RepairMode) { CancelCarry(); return; }
        if (_carried >= 0) { CancelCarry(); return; }
        if (_game.Chat.LinkInventoryItem(slot)) return;
        if (slot >= InventoryConstants.MagicBagStart)
        {
            int to = _game.Inventory.TransferToInventorySlot(slot);
            if (to >= 0) RequestMove(slot, to);
            return;
        }
        _game.Inventory.Use(slot);
    }

    private void CancelCarry()
    {
        if (_carried >= 0 && _bySlot.TryGetValue(_carried, out var cell)) cell.SetCarried(false);
        _carried = -1;
        _carry.Visible = false;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (_carried < 0) return;
        _carry.Position = GetLocalMousePosition() - CarrySize * 0.5f;
    }

    public override void _Input(InputEvent ev)
    {
        if (_carried < 0 || ev is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
        CancelCarry();
        GetViewport().SetInputAsHandled();
    }

    private void SetCospreOpen(bool open)
    {
        bool changed=_cospreOpen!=open;
        _cospreOpen = open;
        _extras.Visible = open;
        // The search field lives in the drawer; closing it ends the search.
        if(!open && _search is {Text.Length:>0}) { _search.Text=""; ApplyBagSearch(); }
        _cospreToggle.Expanded=open;
        if(!open && _carried>=InventoryConstants.CospreStart) CancelCarry();
        float shift=InventoryLayout.DrawerWidth+InventoryLayout.DrawerGap;
        _view.Position=new Vector2(open?shift:0,0);
        CustomMinimumSize = new Vector2(_view.Size.X + (open ? shift : 0), _view.Size.Y);
        Size = CustomMinimumSize;
        if(changed && IsInsideTree())
        {
            var window=_host.Window;
            var position=window.Position+new Vector2(open?-shift:shift,0);
            var bounds=GetViewport().GetVisibleRect().Size;
            window.Position=new Vector2(Mathf.Clamp(position.X,0,Math.Max(0,bounds.X-Size.X)),
                Mathf.Clamp(position.Y,0,Math.Max(0,bounds.Y-Size.Y))).Round();
        }
    }
    internal void ShowExtrasForCheck() => SetCospreOpen(true);

    private void BuildExtras()
    {
        _cospreToggle=_view.Get<InventoryCornerButton>("costume_toggle")!;
        _cospreToggle.GetParent().MoveChild(_cospreToggle,_cospreToggle.GetParent().GetChildCount()-1);
        _cospreToggle.Pressed+=()=>SetCospreOpen(!_cospreOpen);
        AddChild(_extras);
        foreach(var slot in InventoryLayout.CostumeSlots)
        {
            var area=_extras.Areas(InventoryLayout.CostumeArea).Single(p=>p.Node.Id==slot.Position.ToString()).Control;
            var cell=BindSlot(area,InventoryConstants.CospreStart+slot.Position);
            cell.EmptyIcon=InventoryLayout.EmptyIcon(slot.IconX,slot.IconY);
            cell.EmptyHint=slot.Caption;
            _slots.Add(cell);
        }
        var tabs=new ButtonGroup();
        for (int bag=0; bag<InventoryConstants.BagSlotMax; bag++)
        {
            int page=bag;
            var area=_extras.Areas(InventoryLayout.BagItemArea).Single(p=>p.Node.Id==bag.ToString()).Control;
            var cell=BindSlot(area,InventoryConstants.BagSlotStart+bag);
            cell.EmptyIcon=InventoryLayout.EmptyIcon(231+bag*59,254);
            cell.EmptyHint=$"Magic bag {bag+1}";
            _slots.Add(cell);
            var tab=_extras.Get<BaseButton>("bag_tab_"+bag)!;
            tab.Pressed+=()=>SelectBag(page);
            tab.ToggleMode=true; tab.ButtonGroup=tabs; tab.ButtonPressed=bag==0;
            tab.TooltipText=$"Magic bag {bag+1}";
            _bagButtons.Add(tab);
        }
        foreach(var (node,control) in _extras.Areas(InventoryLayout.BagContentArea))
            _bagSlots.Add(BindSlot(control,InventoryConstants.MagicBagStart+int.Parse(node.Id)));
        BuildBagSearch();
    }

    /// <summary>
    /// The client's bag search in the drawer's free band between the costume cells and the bag row: hits in
    /// the carried grid and the shown magic bag are outlined, misses dimmed, and a hidden bag holding a hit
    /// is outlined on its bag cell.
    /// </summary>
    private void BuildBagSearch()
    {
        _search=new LineEdit {Name="bag_search",PlaceholderText="Search the bag",
            Position=InventoryLayout.BagSearch.Position,Size=InventoryLayout.BagSearch.Size,FocusMode=FocusModeEnum.Click};
        _search.AddThemeFontOverride("font",Plugin.Kit.Bold);_search.AddThemeFontSizeOverride("font_size",12);
        _search.AddThemeColorOverride("font_color",ClassicReportDesign.Value);
        _search.AddThemeColorOverride("font_placeholder_color",ClassicDesign.Muted);
        foreach(var state in new[]{"normal","focus","read_only"}) _search.AddThemeStyleboxOverride(state,ClassicDesign.InputBox());
        _search.TextChanged+=_=>ApplyBagSearch();
        _extras.AddChild(_search);
        // The client's VIP Vault shortcut from the bag, shown with the vault key's own item art.
        bool karus=Plugin.Kit.Nation==1;
        var cell=new StyleBoxTexture {Texture=Plugin.Kit.Texture(karus?"ui_ka_inven_us.png":"ui_el_inven_us.png"),
            RegionRect=new Rect2(karus?159:157,karus?326:333,51,52)};
        foreach(var side in new[]{Side.Left,Side.Top,Side.Right,Side.Bottom}) {cell.SetTextureMargin(side,4);cell.SetContentMargin(side,2);}
        var vault=new Button {Name="vip_vault",TooltipText="VIP Vault",FocusMode=FocusModeEnum.None,ExpandIcon=true,
            Icon=ItemIcons.Get(NativeInventory.VipVaultKey),Position=InventoryLayout.VipVault.Position,Size=InventoryLayout.VipVault.Size,
            IconAlignment=HorizontalAlignment.Center,MouseDefaultCursorShape=CursorShape.PointingHand};
        foreach(var state in new[]{"normal","hover","pressed","hover_pressed","focus","disabled"}) vault.AddThemeStyleboxOverride(state,cell);
        vault.AddThemeColorOverride("icon_hover_color",new Color(1.15f,1.15f,1f));
        vault.Pressed+=NativeInventory.OpenVipVault;
        _extras.AddChild(vault);
    }

    private void ApplyBagSearch()
    {
        if(_search==null) return;
        var inventory=_game.Inventory;
        var ids=new int[inventory.Length];
        for(int i=0;i<ids.Length;i++) ids[i]=LibreKO.Domain.BagSearch.Searchable(i)?inventory.At(i).ItemId:0;
        bool searching=!new ItemQuery(_search.Text).IsEmpty;
        var hits=searching?WarehouseView.Matches(ids,_search.Text,ItemData.DisplayName):new HashSet<int>();
        foreach(var cell in _slots.Concat(_bagSlots))
        {
            bool searchable=LibreKO.Domain.BagSearch.Searchable(cell.Slot);
            cell.Search=!searching || !searchable ? SearchMark.None : hits.Contains(cell.Slot) ? SearchMark.Hit : SearchMark.Miss;
        }
        var bags=LibreKO.Domain.BagSearch.BagsHoldingHits(hits);
        for(int bag=0;bag<InventoryConstants.BagSlotMax;bag++)
            if(_bySlot.TryGetValue(InventoryConstants.BagSlotFor(bag),out var bagCell))
                bagCell.Search=bags[bag] && bag!=_bag ? SearchMark.Hit : SearchMark.None;
    }

    private void SelectBag(int bag)
    {
        if(bag<0 || bag>=InventoryConstants.BagSlotMax || _game.Inventory.At(InventoryConstants.BagSlotFor(bag)).IsEmpty) return;
        CancelCarry();
        _bag = Mathf.Clamp(bag, 0, InventoryConstants.BagSlotMax - 1);
        for(int i=0;i<_bagButtons.Count;i++) _bagButtons[i].ButtonPressed=i==_bag;
        RefreshBags();
    }

    private void RefreshBags()
    {
        if(_game.Inventory.At(InventoryConstants.BagSlotFor(_bag)).IsEmpty)
        {
            int first=Enumerable.Range(0,InventoryConstants.BagSlotMax).FirstOrDefault(b=>!_game.Inventory.At(InventoryConstants.BagSlotFor(b)).IsEmpty,-1);
            _bag=Math.Max(0,first);
        }
        bool equipped=!_game.Inventory.At(InventoryConstants.BagSlotFor(_bag)).IsEmpty;
        for(int bag=0;bag<_bagButtons.Count;bag++)
        {
            _bagButtons[bag].Disabled=_game.Inventory.At(InventoryConstants.BagSlotFor(bag)).IsEmpty;
            _bagButtons[bag].ButtonPressed=equipped && bag==_bag;
        }
        for (int i = 0; i < _bagSlots.Count; i++)
        {
            var cell = _bagSlots[i];
            int slot = InventoryConstants.MagicBagStart + _bag * InventoryConstants.MagicBagMax + i;
            _bySlot.Remove(cell.Slot);
            cell.Slot=slot;
            cell.InputEnabled=equipped;
            cell.EmptyHint=equipped?"":"Equip a magic bag to use these slots.";
            _bySlot[slot]=cell;
            cell.Source = _ => _game.Inventory.At(slot);
            cell.OnDropAmount = (from, _, amount) => { CancelCarry(); RequestMove(from, slot, amount); };
            cell.OnClick = _ => SlotClicked(slot);
            cell.OnActivate = _ => SlotActivated(slot);
            cell.OnDoubleClick = _ => { CancelCarry(); SlotActivated(slot); };
            cell.OnHover = (_, over) => HoverSlot(slot, over);
            cell.Refresh();
        }
        ApplyBagSearch();
    }

    public override void _EnterTree()
    {
        _game.Inventory.Changed += OnInventoryChanged;
        _game.Character.Changed += OnCharacterChanged;
        _game.BecameAvailable += Refresh;
        _host.Shown += Refresh;
        _host.Hidden += OnHidden;
        Refresh();
    }

    // A closed inventory only notes the client's change events and refreshes when it is shown again.
    private bool _stale, _totalsStale;

    private void OnInventoryChanged()
    {
        if (IsVisibleInTree()) Refresh(); else _stale = true;
    }

    private void OnCharacterChanged()
    {
        if (IsVisibleInTree()) RefreshTotals(); else _totalsStale = true;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationVisibilityChanged || !(_stale || _totalsStale) || !IsVisibleInTree()) return;
        Callable.From(() =>
        {
            if (!Plugin.Active || !IsInstanceValid(this) || !IsVisibleInTree()) return;
            if (_stale) Refresh(); else if (_totalsStale) RefreshTotals();
        }).CallDeferred();
    }

    public override void _ExitTree()
    {
        _game.Inventory.Changed -= OnInventoryChanged;
        _game.Character.Changed -= OnCharacterChanged;
        _game.BecameAvailable -= Refresh;
        _host.Shown -= Refresh;
        _host.Hidden -= OnHidden;
    }

    private void OnHidden()
    {
        _repair.HideTip();
        if (RepairMode && _host.Window.HasMeta("classic_repair_close")) _host.Window.GetMeta("classic_repair_close").AsCallable().Call();
        _movePrompt.Close();
        foreach(var popup in GetChildren().OfType<ClassicInventoryDestroy>()) popup.QueueFree();
        CancelCarry();
        _game.Inventory.HideTooltip();
    }

    private void HoverSlot(int slot, bool over)
    {
        _repair.HideTip();
        if (!over) { _game.Inventory.HideTooltip(); return; }
        if (RepairMode) { _game.Inventory.HideTooltip(); _repair.ShowTip(slot); }
        else _game.Inventory.ShowTooltip(slot);
    }

    private void Refresh()
    {
        _stale = false;
        foreach (var cell in _slots) cell.Refresh();
        GhostOtherHand(InventoryConstants.RightHand, InventoryConstants.LeftHand);
        GhostOtherHand(InventoryConstants.LeftHand, InventoryConstants.RightHand);
        RefreshBags();
        RefreshTotals();
        if (_carried >= 0 && _game.Inventory.At(_carried).IsEmpty) CancelCarry();
    }

    private void GhostOtherHand(int hand, int other)
    {
        var item = _game.Inventory.At(hand);
        if (!item.TwoHanded || !_bySlot.TryGetValue(other, out var cell)) return;
        cell.SetGhost(item.Icon);
    }

    private void RefreshTotals()
    {
        _totalsStale = false;
        var c = _game.Character;
        _view.SetClippedText("text_gold", c.Gold.ToString("n0"));
        _view.SetClippedText("text_weight", c.MaxWeight > 0 ? $"Weight : {c.Weight / 10f:0.0}/{c.MaxWeight / 10f:0.0}" : "");
    }
}
