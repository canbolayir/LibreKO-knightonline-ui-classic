using Godot;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Layout;

public partial class ItemSlot : Control
{
    public const string DragKeyFrom = "invFrom";
    public const string DragKeyItem = "id";
    private const int IconInset = 2;
    private static readonly Color HoverTint = new(1f, 1f, 0.8f, 0.12f);
    private static readonly Color CarriedTint = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Color GhostTint = new(1f, 1f, 1f, 0.35f);

    public int Slot { get; set; }
    public Func<int, GameItem> Source { get; set; } = s => GameItem.Empty(s);
    public Action<int, int>? OnDrop { get; set; }
    public Func<Variant, bool>? CanCompanionDrop { get; set; }
    public Action<Variant>? OnCompanionDrop { get; set; }
    public Action<int>? OnClick { get; set; }
    public Action<int>? OnActivate { get; set; }
    public Action<int>? OnDoubleClick { get; set; }
    public Action<int, bool>? OnHover { get; set; }
    public GameItem Current { get; private set; }
    public Texture2D? EmptyIcon { get; set; }
    public string EmptyHint { get; set; } = "";
    public bool InputEnabled { get; set; } = true;
    public Func<bool>? CanDrag { get; set; }

    private readonly TextureRect _icon;
    private readonly TextureRect _emptyIcon;
    private readonly Label _count;
    private readonly ColorRect _hover;
    private readonly UpgradeBadge _badge;
    private bool _suppressClick;
    private int _pressedSlot = -1;
    private Vector2 _grabPoint;

    public ItemSlot(int slot)
    {
        Slot = slot;
        MouseFilter = MouseFilterEnum.Stop;
        _hover = new ColorRect { Color = HoverTint, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _hover.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_hover);
        _emptyIcon=new TextureRect {ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore};
        _emptyIcon.SetAnchorsPreset(LayoutPreset.FullRect);
        _emptyIcon.OffsetLeft=5;_emptyIcon.OffsetTop=5;_emptyIcon.OffsetRight=-5;_emptyIcon.OffsetBottom=-5;
        AddChild(_emptyIcon);
        _icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _icon.SetAnchorsPreset(LayoutPreset.FullRect);
        _icon.OffsetLeft = IconInset; _icon.OffsetTop = IconInset;
        _icon.OffsetRight = -IconInset; _icon.OffsetBottom = -IconInset;
        AddChild(_icon);
        _count = new Label();
        AddChild(_count);
        ItemCountStyle.Apply(_count, this);
        _badge = UpgradeBadge.Attach(this);
        MouseEntered += () => { _hover.Visible = InputEnabled; if (InputEnabled && !Current.IsEmpty) OnHover?.Invoke(Slot, true); };
        MouseExited += () => { _hover.Visible = false; OnHover?.Invoke(Slot, false); };
    }

    public void Refresh()
    {
        Current = Source(Slot);
        _icon.Texture = Current.IsEmpty ? null : Current.Icon;
        _icon.Modulate = Colors.White;
        _emptyIcon.Texture=EmptyIcon;
        _emptyIcon.Visible=Current.IsEmpty && EmptyIcon!=null;
        TooltipText=Current.IsEmpty?EmptyHint:"";
        SelfModulate=InputEnabled?Colors.White:new Color(0.45f,0.45f,0.45f);
        _count.Text = Current.IsEmpty ? "" : LibreKO.Domain.ItemData.CountBadge(LibreKO.Domain.ItemData.Get(Current.ItemId), Current.Count);
        if (Current.IsEmpty) _badge.Clear(); else _badge.Set(Current.ItemId);
    }

    public void SetGhost(Texture2D? icon)
    {
        if (!Current.IsEmpty) return;
        _icon.Texture = icon;
        _icon.Modulate = GhostTint;
        _emptyIcon.Visible=false;
    }

    public void SetCarried(bool carried) => _icon.Modulate = carried ? CarriedTint : Colors.White;

    public override void _GuiInput(InputEvent ev)
    {
        if(!InputEnabled) return;
        if (ev is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
        {
            OnActivate?.Invoke(Slot);
            AcceptEvent();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        if (mb.Pressed)
        {
            _pressedSlot = Slot;
            _grabPoint=IconDragPreview.GrabPoint(this,_icon,mb.Position);
            if (mb.DoubleClick)
            {
                _suppressClick = true;
                OnDoubleClick?.Invoke(Slot);
            }
            AcceptEvent();
            return;
        }
        bool repairClick = CanDrag?.Invoke() == false;
        bool clickedSameItem = _pressedSlot == Slot && new Rect2(Vector2.Zero, Size).HasPoint(mb.Position);
        _pressedSlot = -1;
        if (_suppressClick) { _suppressClick = false; AcceptEvent(); return; }
        if (repairClick && !clickedSameItem) { AcceptEvent(); return; }
        OnClick?.Invoke(Slot);
        AcceptEvent();
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!InputEnabled || Current.IsEmpty || CanDrag?.Invoke() == false) return default;
        _suppressClick = false;
        OnHover?.Invoke(Slot, false);
        DragLayer.Show(this,IconDragPreview.Create(_icon,_grabPoint));
        return new Godot.Collections.Dictionary { { DragKeyItem, Current.ItemId }, { DragKeyFrom, Slot } };
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!InputEnabled || CanDrag?.Invoke() == false || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("companionFrom")) return CanCompanionDrop?.Invoke(data) == true;
        return OnDrop != null && d.ContainsKey(DragKeyFrom) && d[DragKeyFrom].AsInt32() != Slot;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if(!InputEnabled || CanDrag?.Invoke() == false) return;
        _hover.Visible = false;
        if (data.AsGodotDictionary().ContainsKey("companionFrom")) { OnCompanionDrop?.Invoke(data); return; }
        OnDrop?.Invoke(data.AsGodotDictionary()[DragKeyFrom].AsInt32(), Slot);
    }
}

public partial class DropTarget : Control
{
    public Action<int>? OnDrop { get; set; }
    public Action? OnClick { get; set; }

    public DropTarget() => MouseFilter = MouseFilterEnum.Stop;

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) return;
        OnClick?.Invoke();
        AcceptEvent();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        OnDrop != null && data.VariantType == Variant.Type.Dictionary
        && data.AsGodotDictionary().ContainsKey(ItemSlot.DragKeyFrom);

    public override void _DropData(Vector2 atPosition, Variant data) =>
        OnDrop?.Invoke(data.AsGodotDictionary()[ItemSlot.DragKeyFrom].AsInt32());
}
