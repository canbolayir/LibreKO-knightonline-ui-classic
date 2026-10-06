using Godot;
using LibreKO;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

public partial class HotkeyBar : Control
{
    private const string LayoutId="classic_hotbar";
    private const string ExtraSetting="hotbar.extras";
    private const string TransparencySetting="hotbar.transparency_step";
    private const int MaxBars=8;
    private sealed class Strip
    {
        public Control View=null!;
        public Func<int> PageOf=null!;
        public Label Page=null!;
        public Vector2 PageSize;
        public Control Drag=null!;
        public readonly List<HotSlot> Slots=new();
    }
    private readonly PluginGame _game=Plugin.Kit.Game;
    private readonly PluginSettings _settings=Plugin.Kit.Context.Settings;
    private readonly List<Strip> _strips=new();
    private readonly List<int> _extraPages=new();
    private readonly Control _grip=new() { Name="grip",MouseFilter=MouseFilterEnum.Stop };
    private readonly Control _canvas=new() { Name="canvas",MouseFilter=MouseFilterEnum.Ignore };
    private bool _horizontal;
    private float _mainTop;
    private int _transparencyStep;
    private HotbarButton _lockButton=null!;

    public HotkeyBar()
    {
        MouseFilter=MouseFilterEnum.Ignore;
        _horizontal=_settings.GetBool("hotbar.horizontal",false);
        _transparencyStep=Mathf.Clamp(_settings.GetInt(TransparencySetting,0),0,3);
        foreach(var value in _settings.Get(ExtraSetting).Split(',',StringSplitOptions.RemoveEmptyEntries))
            if(int.TryParse(value,out int page) && page>=0 && page<_game.Hotbar.Pages && _extraPages.Count<MaxBars-1)
                _extraPages.Add(page);
        AddChild(_grip);
        AddChild(_canvas);
        ApplyTransparency();
        Build();
    }
    private Vector2 DefaultPosition()
    {
        var room=GetViewport().GetVisibleRect().Size;
        return _horizontal ? new Vector2(Mathf.Round((room.X-Size.X)/2),room.Y-Taskbar.BarHeight-Size.Y-2) : new Vector2(8,110);
    }
    private void Build()
    {
        _game.Hotbar.HideTooltip();
        foreach(var strip in _strips) { _canvas.RemoveChild(strip.View); strip.View.QueueFree(); }
        _strips.Clear();
        var source=Plugin.Kit.Layout("{nation}_hotkey_us");
        int count=_game.Hotbar.SlotsPerPage;
        var main=new HotbarGeometry(count,true);
        var extra=new HotbarGeometry(count,false);
        float previousMainTop=_mainTop;
        _mainTop=_horizontal ? _extraPages.Count*extra.Size.X : 0;
        AddStrip(source,main,Vector2.Zero,()=>_game.Hotbar.Page,-1);
        for(int i=0;i<_extraPages.Count;i++) {
            int index=i;
            var at=new Vector2(main.Size.X+i*extra.Size.X,0);
            AddStrip(source,extra,at,()=>_extraPages[index],index);
        }
        var verticalSize=new Vector2(main.Size.X+_extraPages.Count*extra.Size.X,main.Size.Y);
        _canvas.Size=verticalSize;
        _canvas.Rotation=_horizontal ? -Mathf.Pi/2 : 0;
        _canvas.Position=_horizontal ? new Vector2(0,verticalSize.X) : Vector2.Zero;
        Size=_horizontal ? new Vector2(verticalSize.Y,verticalSize.X) : verticalSize;
        // The outer artwork turns as one group; text and slot contents remain upright.
        foreach(var label in _strips.SelectMany(strip=>strip.View.GetChildren().OfType<Label>())) {
            label.PivotOffset=label.Size/2;
            label.Rotation=_horizontal ? Mathf.Pi/2 : 0;
            label.Resized+=()=>label.PivotOffset=label.Size/2;
        }
        foreach(var strip in _strips) foreach(var slot in strip.Slots) slot.SetHorizontal(_horizontal);
        if(IsInsideTree()) {
            if(!Config.HasWindowPos(LayoutId)) Position=DefaultPosition();
            else Position+=new Vector2(0,previousMainTop-_mainTop);
            ClampPosition();
        }
        _settings.SetBool("hotbar.horizontal",_horizontal);
        _settings.Set(ExtraSetting,string.Join(",",_extraPages));
        Refresh();
    }
    private void ClampPosition()
    {
        var room=GetViewport().GetVisibleRect().Size;
        Position=new Vector2(Mathf.Clamp(Position.X,0,Mathf.Max(0,room.X-Size.X)),Mathf.Clamp(Position.Y,0,Mathf.Max(0,room.Y-Size.Y)));
    }
    private void AddStrip(LayoutNode source,HotbarGeometry geometry,Vector2 at,Func<int> pageOf,int extraIndex)
    {
        var body=new Control { Name=extraIndex<0 ? "main_bar" : $"extra_bar_{extraIndex}",Position=at,Size=geometry.Size,MouseFilter=MouseFilterEnum.Ignore };
        _canvas.AddChild(body);
        var strip=new Strip { View=body,PageOf=pageOf,PageSize=geometry.PageRect.Size };
        _strips.Add(strip);
        body.AddChild(new OriginalHotbarArtwork(source,geometry) { Name="artwork",Size=geometry.Size });
        var drag=new Control { Name="page_drag",Position=geometry.PageGrip.Position,Size=geometry.PageGrip.Size,
            MouseFilter=MouseFilterEnum.Stop,MouseDefaultCursorShape=CursorShape.Move,TooltipText="Drag to move the skill bars" };
        drag.GuiInput+=ev=> {
            if(ev is InputEventMouseButton { ButtonIndex:MouseButton.Left } press) {
                _grip.EmitSignal(Control.SignalName.GuiInput,press); drag.AcceptEvent();
            }
        };
        body.AddChild(drag);
        strip.Drag=drag;
        for(int i=0;i<geometry.Count;i++) {
            var cell=new HotSlot(i) {
                Name=$"slot_{i}",Position=geometry.Slot(i),Size=new Vector2(32,32),AbsOf=s=>pageOf()*geometry.Count+s,
                Source=a=>_game.Hotbar.Slot(a),OnActivate=a=>_game.Hotbar.ActivateAbs(a),
                OnSelect=a=>_game.Hotbar.Select(a),IsSelected=a=>_game.Hotbar.SelectedAbs==a,
                IsLocked=()=>_game.Hotbar.Locked,
                OnDrop=(a,id,from)=>_game.Hotbar.Drop(a,id,from),OnClear=a=>_game.Hotbar.Clear(a),
                OnHover=(a,on)=> { if(on) _game.Hotbar.ShowTooltip(a); else _game.Hotbar.HideTooltip(); }
            };
            body.AddChild(cell);strip.Slots.Add(cell);
            if(geometry.SharedKeys) {
                var key=HotbarVisuals.KeyLabel(((i+1)%10).ToString());
                key.Name=$"key_{i}"; key.Position=geometry.Key(i).Position; key.Size=geometry.Key(i).Size;
                body.AddChild(key);
            }
        }
        foreach(var id in new[]{"btn_up","btn_down"}) {
            int direction=id=="btn_up" ? -1:1;
            var button=new NativePageButton(source.Find(id)!) { Name=id,
                Position=geometry.PageArrow(direction).Position,Size=geometry.PageArrow(direction).Size,
                TooltipText=direction<0 ? "Previous skill page":"Next skill page" };
            button.Pressed+=()=> { if(extraIndex<0) _game.Hotbar.ChangePage(direction); else TurnExtra(extraIndex,direction); };
            body.AddChild(button);
        }
        void ActionButton(int icon,string name,string tooltip,Rect2 rect,Action action) {
            var button=new HotbarButton(icon) { Name=name,Size=rect.Size,Position=rect.Position,TooltipText=tooltip };
            button.Pressed+=action;body.AddChild(button);
        }
        if(extraIndex<0) {
            _lockButton=new HotbarButton { Name="lock",Position=geometry.Lock.Position,Size=geometry.Lock.Size,ToggleMode=true,
                GlyphRotation=_horizontal ? Mathf.Pi/2:0 };
            _lockButton.Pressed+=()=> { _game.Hotbar.SetLocked(!_game.Hotbar.Locked); Refresh(); };
            body.AddChild(_lockButton);
            ActionButton(0,"rotate","Switch skill bars between horizontal and vertical",geometry.Rotate,ToggleOrientation);
            if(_extraPages.Count<MaxBars-1) ActionButton(1,"add_bar","Add a skill bar (up to 8)",geometry.Add,AddBar);
        } else ActionButton(2,"remove_bar","Close this skill bar; assigned skills are preserved",geometry.Remove,()=>RemoveBar(extraIndex));
        strip.Page=HotbarVisuals.Label("",13);
        strip.Page.Name="page_label";strip.Page.Position=geometry.PageRect.Position;strip.Page.Size=geometry.PageRect.Size;
        body.AddChild(strip.Page);
    }
    private void AddBar()
    {
        if(_extraPages.Count>=MaxBars-1) return;
        int previous=_extraPages.Count>0 ? _extraPages[^1] : _game.Hotbar.Page;
        _extraPages.Add((previous+1)%_game.Hotbar.Pages);
        Build();
    }
    private void RemoveBar(int index)
    {
        if(index<0 || index>=_extraPages.Count) return;
        _extraPages.RemoveAt(index); Build();
    }
    private void TurnExtra(int index,int delta)
    {
        int pages=_game.Hotbar.Pages;
        _extraPages[index]=((_extraPages[index]+delta)%pages+pages)%pages;
        _settings.Set(ExtraSetting,string.Join(",",_extraPages)); Refresh();
    }
    private void ToggleOrientation() { _horizontal=!_horizontal; Build(); }
    private void ApplyTransparency()
    {
        _canvas.Modulate=new Color(1f,1f,1f,1f-_transparencyStep*0.25f);
    }
    private void ClickOrnament(float _)
    {
        // HudLayout calls this only after a click without dragging. Only the
        // single main ornament cycles opacity, including in horizontal mode.
        var ornament=_strips[0].Drag;
        if (!new Rect2(Vector2.Zero,ornament.Size).HasPoint(ornament.GetLocalMousePosition())) return;
        _transparencyStep=(_transparencyStep+1)%4;
        _settings.SetInt(TransparencySetting,_transparencyStep);
        ApplyTransparency();
        Refresh();
    }
    // Explicit diagnostic only: render three real pages without saving temporary layouts.
    public async System.Threading.Tasks.Task CaptureBarsForCheck(Func<string,System.Threading.Tasks.Task> capture)
    {
        var pages=_extraPages.ToArray();bool horizontal=_horizontal,enabled=PluginSettings.Enabled;var position=Position;var scale=Scale;
        bool detail=OS.GetCmdlineUserArgs().Contains("hotbar-detail");
        try {
            PluginSettings.Enabled=false;
            _extraPages.Clear();
            for(int i=1;i<=2;i++) _extraPages.Add((_game.Hotbar.Page+i)%_game.Hotbar.Pages);
            _horizontal=false;Build();
            if(detail) { Scale=new Vector2(2,2);Position=new Vector2(600,110); }
            await ToSignal(GetTree().CreateTimer(0.5),SceneTreeTimer.SignalName.Timeout);
            await capture("hotbar_vertical");
            _horizontal=true;Build();
            if(detail) Position=new Vector2(600,110);
            var icon=_strips[0].Slots[0].GetNode<TextureRect>("content/icon");
            GD.Print($"[classic-check] horizontal skill icon angle: {Mathf.RadToDeg(icon.GetGlobalTransform().Rotation):F1} degrees");
            await ToSignal(GetTree().CreateTimer(0.5),SceneTreeTimer.SignalName.Timeout);
            await capture("hotbar_horizontal");
        } finally {
            _extraPages.Clear();_extraPages.AddRange(pages);_horizontal=horizontal;Scale=scale;Build();Position=position;
            PluginSettings.Enabled=enabled;
        }
    }
    public override void _Ready()
    {
        _game.Hotbar.Changed+=Refresh;
        HudLayout.Attach(this,LayoutId,_grip,DefaultPosition,backgroundOpacityChanged:ClickOrnament);
        if(_game.Hotbar.Pages>1) _game.Hotbar.SetPage(1);
        ClampPosition(); Refresh();
    }
    public override void _ExitTree() { _game.Hotbar.Changed-=Refresh; }
    public override void _Process(double delta) { foreach(var strip in _strips) foreach(var slot in strip.Slots) slot.TickCooldown(); }
    private void Refresh()
    {
        bool locked=_game.Hotbar.Locked;
        _lockButton.Glyph=locked ? 3:4;
        _lockButton.SetPressedNoSignal(locked);
        _lockButton.TooltipText=locked ? "Unlock the skill bar to move or remove shortcuts":"Lock the skill bar to protect shortcuts";
        foreach(var strip in _strips) {
            strip.Page.Text=(strip.PageOf()+1).ToString();
            // Reapply the footer size after theme/text shaping invalidates Label's cached minimum.
            strip.Page.UpdateMinimumSize();strip.Page.Size=strip.PageSize;
            strip.Drag.TooltipText=$"Page {strip.PageOf()+1} ({KeyBinds.Get(KeyAction.HotPage1+strip.PageOf()).Text}); drag to move the bars";
            if (strip==_strips[0]) strip.Drag.TooltipText+=$"; click to change transparency (currently {_transparencyStep*25}%)";
            foreach(var slot in strip.Slots) slot.Refresh();
        }
    }
}

public partial class HotSlot : Control
{
    private const string DragKeyItem = "id";
    private const string DragKeyBar = "barAbs";
    private const int CountFontSize = 11;
    private const int CountInset = 2;
    private const int CountOutline = 3;
    private static readonly Color CooldownShade = new(0f, 0f, 0f, 0.62f);
    private static readonly Color MissingTint = new(0.45f, 0.45f, 0.45f, 1f);

    public int SlotInPage { get; }
    public Func<int, int> AbsOf { get; set; } = s => s;
    public Func<int, HotSlotInfo> Source { get; set; } = abs => HotSlotInfo.Empty(abs);
    public Action<int>? OnActivate { get; set; }
    public Action<int>? OnSelect { get; set; }
    public Func<int,bool> IsSelected { get; set; } = abs => false;
    public Func<bool> IsLocked { get; set; } = () => false;
    public Action<int, int, int>? OnDrop { get; set; }
    public Action<int>? OnClear { get; set; }
    public Action<int, bool>? OnHover { get; set; }

    private readonly TextureRect _icon;
    private readonly ColorRect _shade;
    private readonly Label _count;
    private readonly Control _content;
    private readonly Panel _selection;
    private HotSlotInfo _current;
    private bool _pressed,_dragging,_selected;
    private int _dragAbs,_dragId;
    private Vector2 _grabPoint;

    private int Abs => AbsOf(SlotInPage);

    public HotSlot(int slotInPage)
    {
        SlotInPage = slotInPage;
        MouseFilter = MouseFilterEnum.Stop;
        _content = new Control { Name = "content", MouseFilter = MouseFilterEnum.Ignore };
        _content.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_content);
        _content.Resized += () => _content.PivotOffset = _content.Size / 2;
        _icon = new TextureRect
        {
            Name = "icon",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _icon.SetAnchorsPreset(LayoutPreset.FullRect);
        _content.AddChild(_icon);
        _shade = new ColorRect { Name = "cooldown", Color = CooldownShade, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _shade.SetAnchorsPreset(LayoutPreset.FullRect);
        _content.AddChild(_shade);
        _count = new Label
        {
            Name = "count",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _count.SetAnchorsPreset(LayoutPreset.FullRect);
        _count.OffsetRight = -CountInset;
        _count.OffsetBottom = -CountInset;
        _count.AddThemeFontSizeOverride("font_size", CountFontSize);
        _count.AddThemeFontOverride("font",HotbarVisuals.Font);
        _count.AddThemeColorOverride("font_color", Colors.White);
        _count.AddThemeColorOverride("font_outline_color", Colors.Black);
        _count.AddThemeConstantOverride("outline_size", CountOutline);
        _content.AddChild(_count);
        _selection = new Panel { Name="selection",MouseFilter=MouseFilterEnum.Ignore,Visible=false };
        _selection.SetAnchorsPreset(LayoutPreset.FullRect);
        var selectionStyle = new StyleBoxFlat { BgColor=Colors.Transparent,BorderColor=new Color(0.55f,1f,0.45f) };
        selectionStyle.SetBorderWidthAll(2);
        _selection.AddThemeStyleboxOverride("panel",selectionStyle);
        AddChild(_selection);
        MouseEntered += () => { if (!_current.IsEmpty) OnHover?.Invoke(Abs, true); };
        MouseExited += () => OnHover?.Invoke(Abs, false);
    }

    public void SetHorizontal(bool horizontal)
    {
        _content.PivotOffset = _content.Size / 2;
        _content.Rotation = horizontal ? Mathf.Pi / 2 : 0;
    }

    public void Refresh()
    {
        _current = Source(Abs);
        bool selected = !_current.IsEmpty && IsSelected(Abs);
        if (_selected != selected) { _selected = selected; _selection.Visible = selected; }
        _icon.Texture = _current.IsEmpty ? null : _current.Icon;
        _icon.Modulate = _current.Enough ? Colors.White : MissingTint;
        _count.Text = _current.Count >= 0 ? _current.Count.ToString() : "";
        TooltipText = _current.Tooltip.Length > 0 ? _current.Tooltip : _current.Name;
        TickCooldown();
    }

    public void TickCooldown()
    {
        if (_current.IsEmpty) { _shade.Visible = false; return; }
        var live = Source(Abs);
        if (live.Id != _current.Id) { Refresh(); return; }
        if (live.Count != _current.Count || live.Enough != _current.Enough)
        {
            _current = live;
            _icon.Modulate = live.Enough ? Colors.White : MissingTint;
            _count.Text = live.Count >= 0 ? live.Count.ToString() : "";
        }
        float frac = live.Cooldown;
        _shade.Visible = frac > 0.001f;
        if (_shade.Visible)
        {
            _shade.AnchorTop = 1f - Mathf.Clamp(frac, 0f, 1f);
            _shade.OffsetTop = 0f;
        }
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex == MouseButton.Right) {
            if (mb.Pressed) {
                if (mb.DoubleClick && IsSelected(Abs)) OnActivate?.Invoke(Abs);
                else OnSelect?.Invoke(Abs);
            }
            AcceptEvent();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        if (mb.Pressed) {
            _grabPoint=IconDragPreview.GrabPoint(this,_icon,mb.Position);
            _pressed = true;
        }
        else if (_pressed) { _pressed = false; OnActivate?.Invoke(Abs); }
        AcceptEvent();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) _pressed = false;
        if (what != NotificationDragEnd || !_dragging) return;
        _dragging = false;
        // Keep the original source even when a page changes during the drag.
        if (!IsDragSuccessful() && !IsLocked() && Source(_dragAbs).Id == _dragId)
            OnClear?.Invoke(_dragAbs);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_current.IsEmpty || IsLocked()) return default;
        _pressed = false;
        _dragging = true;
        _dragAbs = Abs;
        _dragId = _current.Id;
        DragLayer.Show(this,IconDragPreview.Create(_icon,_grabPoint));
        return new Godot.Collections.Dictionary { { DragKeyItem, _dragId }, { DragKeyBar, _dragAbs } };
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if(OnDrop==null || data.VariantType!=Variant.Type.Dictionary) return false;
        var d=data.AsGodotDictionary();
        return d.ContainsKey(DragKeyItem) && (!d.ContainsKey(DragKeyBar) || !IsLocked());
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var d = data.AsGodotDictionary();
        int from = d.ContainsKey(DragKeyBar) ? d[DragKeyBar].AsInt32() : -1;
        OnDrop?.Invoke(Abs, d[DragKeyItem].AsInt32(), from);
    }
}

