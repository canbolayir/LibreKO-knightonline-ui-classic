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
    private int _carried = -1;
    private readonly LibreKO.QuantityPrompt _movePrompt=new(220);

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
        SetProcess(false);
        SetCospreOpen(false);
        Refresh();
    }

    public override void _Ready()
    {
        _view.ApplyDeclaredBounds();
        _extras.ApplyDeclaredBounds();
    }

    private ItemSlot BindSlot(Control area, int slot)
    {
        var cell = new ItemSlot(slot)
        {
            Position = area.Position,
            Size = area.Size,
            Source = s => _game.Inventory.At(s),
            OnDrop = (from, to) => { CancelCarry(); RequestMove(from, to); },
            CanCompanionDrop = data => CompanionCell(slot)?._CanDropData(Vector2.Zero, data) == true,
            OnCompanionDrop = data => { CancelCarry(); CompanionCell(slot)?._DropData(Vector2.Zero, data); },
            OnClick = SlotClicked,
            OnActivate = SlotActivated,
            OnDoubleClick = s => { CancelCarry(); _game.Inventory.Use(s); },
            OnHover = (s, over) => { if (over) _game.Inventory.ShowTooltip(s); else _game.Inventory.HideTooltip(); },
        };
        area.GetParent().AddChild(cell);
        _bySlot[slot] = cell;
        return cell;
    }

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

    private void RequestMove(int from,int to)
    {
        var item=_game.Inventory.At(from);
        if(item.IsEmpty || from==to) return;
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
        var item=_game.Inventory.At(slot);
        if(item.IsEmpty || slot>=InventoryConstants.CospreStart) { _game.Inventory.Drop(slot);return; }
        _game.Inventory.HideTooltip();
        AddChild(new ClassicInventoryDestroy(_view,()=>
        {
            var current=_game.Inventory.At(slot);
            if(current.ItemId==item.ItemId && current.Count==item.Count) _game.Inventory.ConfirmDrop(slot,item.ItemId);
        }));
    }

    private void SlotActivated(int slot)
    {
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
            cell.OnDrop = (from, _) => { CancelCarry(); RequestMove(from, slot); };
            cell.OnClick = _ => SlotClicked(slot);
            cell.OnActivate = _ => SlotActivated(slot);
            cell.OnDoubleClick = _ => { CancelCarry(); SlotActivated(slot); };
            cell.OnHover = (_, over) => { if (over) _game.Inventory.ShowTooltip(slot); else _game.Inventory.HideTooltip(); };
            cell.Refresh();
        }
    }

    public override void _EnterTree()
    {
        _game.Inventory.Changed += Refresh;
        _game.Character.Changed += RefreshTotals;
        _game.BecameAvailable += Refresh;
        _host.Shown += Refresh;
        _host.Hidden += OnHidden;
        Refresh();
    }

    public override void _ExitTree()
    {
        _game.Inventory.Changed -= Refresh;
        _game.Character.Changed -= RefreshTotals;
        _game.BecameAvailable -= Refresh;
        _host.Shown -= Refresh;
        _host.Hidden -= OnHidden;
    }

    private void OnHidden()
    {
        _movePrompt.Close();
        foreach(var popup in GetChildren().OfType<ClassicInventoryDestroy>()) popup.QueueFree();
        CancelCarry();
        _game.Inventory.HideTooltip();
    }

    private void Refresh()
    {
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
        var c = _game.Character;
        _view.SetClippedText("text_gold", c.Gold.ToString("n0"));
        _view.SetClippedText("text_weight", c.MaxWeight > 0 ? $"Weight : {c.Weight / 10f:0.0}/{c.MaxWeight / 10f:0.0}" : "");
    }
}
