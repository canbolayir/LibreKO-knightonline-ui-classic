using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class StatusHud : Control
{
    public const string LayoutName = "{nation}_statebar_us";
    public static readonly Vector2 ScreenPosition = new(8, 8);

    // Screen rectangle of the status bar with its minimap at the default position.
    public static Rect2 DefaultBounds
    {
        get
        {
            var source = Plugin.Kit.Layout(LayoutName);
            var bar = source.Find("Img_StateBar")!;
            var background = source.Find("Group_MiniMap")!.Find("Img_MiniMapBackground")!;
            return new Rect2(ScreenPosition, new Vector2(bar.X + bar.W, background.Y + background.H));
        }
    }

    private static Func<string> Key(KeyAction action) => () => KeyBinds.Get(action) is { Assigned: true } chord ? chord.Text : "";

    /// <summary>
    /// The two icon rows (plate-less <see cref="ClassicHudIcons"/>; the letters name the buttons): column -1 is the
    /// original btn_quest position, the rest follow at the btn_power pitch. Row 1 is Q P A D G; row 2, <see cref="RowGap"/>
    /// pixels below it, is E R, then the hourglass (with the 2.x menu art) and mail.
    /// </summary>
    private static readonly (string Letter,string Window,string Title,int Column,int Row,Func<PluginGame,Action>? Press,Func<string>? Key,string? Alert,string Icon)[] Shortcuts =
    {
        ("Q","quests","Quests",-1,0,game=>()=>CharacterWindow.ToggleQuestList(game),Key(KeyAction.Quests),null,"quests"),
        ("P","shoppingmall","Power-Up Store",0,0,null,Key(KeyAction.PowerUpStore),null,"store"),
        ("A","achievements","Achievements",1,0,null,Key(KeyAction.Achievements),null,"achievements"),
        ("D","attendance","Daily Attendance",2,0,null,Key(KeyAction.Attendance),null,"attendance"),
        ("G","genie","Genie",3,0,null,Key(KeyAction.Genie),null,"genie"),
        ("E","events","Events",-1,1,null,null,"an event starts soon","events"),
        ("R","daily_rank","Daily Rank",0,1,null,null,"new rankings today","daily-rank"),
    };
    /// <summary>Vertical gap between the two icon rows.</summary>
    private const int RowGap = 2;

    private readonly LayoutView _view;
    private readonly PluginGame _game;
    private readonly MapCanvas _map;
    private readonly ClassicNpcList _npcList;
    private readonly Control _mapGroup;
    private readonly Control _dragHandle;
    private readonly Vector2 _expandedSize,_collapsedSize;
    private readonly Label _coordinates,_zoneName;
    private readonly List<Control> _shortcuts=new();

    /// <summary>
    /// The bar with its letter row and envelope: what a docked window must leave free (the minimap below may sit
    /// under one).
    /// </summary>
    /// <summary>The minimap's screen rectangle while it is shown.</summary>
    public Rect2? MiniMapBounds => _mapGroup.IsVisibleInTree() ? _mapGroup.GetGlobalRect() : null;

    /// <summary>The minimap's NPC finder list.</summary>
    public ClassicNpcList NpcList => _npcList;

    public Rect2 BarBounds => _shortcuts.Aggregate(_view.Get("Img_StateBar")!.GetGlobalRect(),(bounds,shortcut)=>bounds.Merge(shortcut.GetGlobalRect()));

    public StatusHud()
    {
        var kit = Plugin.Kit;
        _game = kit.Game;
        var source=kit.Layout(LayoutName);
        _view = new LayoutView(kit, source);
        Position = ScreenPosition;
        // The native minimap shares the status bar's parchment row. Keep the
        // original UIF coordinates so the header is drawn once, as one HUD.
        var group=source.Find("Group_MiniMap")!;
        var background=group.Find("Img_MiniMapBackground")!;
        var quest=source.Find("btn_quest")!;
        var power=source.Find("btn_power")!;
        int shortcutPitch=power.X-quest.X;
        int rowPitch=power.H+RowGap;
        Vector2 Cell(int column,int row) => new(power.X+shortcutPitch*column,power.Y+rowPitch*row);
        // With the 2.x menu art the hourglass (the art's alarm clock spot) follows R and opens the Events window;
        // mail ends the second row.
        var clockArt=ClassicStatusClock.Art;
        int clockColumn=Shortcuts[^1].Column+1,mailColumn=clockColumn+(clockArt!=null ? 1:0);
        int lastColumn=Math.Max(Shortcuts.Max(s=>s.Column),mailColumn);
        float shortcutRight=power.X+shortcutPitch*lastColumn+power.W;
        Size = new Vector2(Mathf.Max(shortcutRight,Mathf.Max(_view.Size.X,background.X+background.W)),background.Y+background.H);
        _expandedSize=Size;
        _collapsedSize=new Vector2(Size.X,source.Find("Img_StateBar")!.H);
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_view);
        var statebar=source.Find("Img_StateBar")!;
        _dragHandle=_view.MakeDragHandle(new Rect2(statebar.X,0,statebar.W,Size.Y));
        _dragHandle.TooltipText="Drag to move the HP bar and minimap together";
        _view.Hide("Progress_HP_slow", "Progress_HP_drop", "Progress_HP_undead", "Progress_HP_lasting",
            "img_mail_on", "img_mail_normal", "img_Reporter");
        _view.Hide("string_fps", "SystemTime");
        var area=group.Find("Img_MiniMap")!;
        _mapGroup=_view.Get("Group_MiniMap")!;
        _map=new MapCanvas(_game) { Position=new Vector2(area.X-group.X,area.Y-group.Y),Size=area.SizeVec };
        _mapGroup.AddChild(_map);
        _mapGroup.MoveChild(_map,0);
        // The NPC finder over the map (above the parchment hole's ragged edge): its diamonds, "N" in the lower left
        // corner and the list over the map.
        var markers=new Control { Name="npc_markers",Position=_map.Position,Size=_map.Size,MouseFilter=MouseFilterEnum.Ignore };
        _mapGroup.AddChild(markers);
        _map.Markers=markers;
        _npcList=new ClassicNpcList(_game);
        _npcList.Place(new Rect2(_map.Position,_map.Size));
        _mapGroup.AddChild(_npcList);
        _mapGroup.AddChild(_npcList.Button);
        _view.OnPressed("Btn_ZoomIn",()=>_map.Zoom(-1));
        _view.OnPressed("Btn_ZoomOut",()=>_map.Zoom(1));
        CentreInBar("Text_HP","Progress_HP");
        CentreInBar("Text_MSP","Progress_MSP");
        CentreInBar("Text_ExpP","Progress_ExpP");
        // Use the two existing parchment fields; do not add another frame above the HUD.
        // The name field ends at the native 4..112 column, before the minimap zoom button.
        AlignText("Text_Position",new Rect2(4,43,108,20),true);
        AlignText("Text_Version",new Rect2(155,41,117,16),true);
        _zoneName=_view.Get<Label>("Text_Version")!;
        _zoneName.Name="Text_ZoneName";
        _zoneName.AddThemeFontSizeOverride("font_size",10);
        _coordinates=new Label {Name="Text_Coordinates",MouseFilter=MouseFilterEnum.Ignore,
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,ClipText=true};
        _coordinates.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());
        _coordinates.AddThemeFontOverride("font",kit.ChatStrong);
        _coordinates.AddThemeFontSizeOverride("font_size",10);
        _coordinates.AddThemeColorOverride("font_color",new Color(1f,.94f,.77f));
        _coordinates.AddThemeColorOverride("font_outline_color",Colors.Black);
        _coordinates.AddThemeConstantOverride("outline_size",1);
        _view.AddChild(_coordinates);
        _view.Hide("btn_quest","btn_power");
        // The original row (Q on btn_quest, then the btn_power pitch) and a second row under it carry the client's
        // Genie, Events and Daily Rank, which replace the client's own top-right icons. Both rows lie right of the
        // status bar and above the bar's lower edge, clear of the minimap.
        foreach(var (letter,window,title,column,row,press,key,alert,icon) in Shortcuts)
            _shortcuts.Add(new ClassicStatusShortcut(kit,quest,letter,window,title,press?.Invoke(_game),key,alert,icon) {
                Position=Cell(column,row),
            });
        if(clockArt!=null)
            _shortcuts.Add(new ClassicStatusClock(new Vector2(quest.W,quest.H),()=>_game.Windows.Toggle("events")) {
                Position=Cell(clockColumn,1),
            });
        _shortcuts.Add(new ClassicStatusShortcut(kit,quest,"","mail","Mail",icon:"mail") {
            Position=Cell(mailColumn,1),
        });
        foreach(var shortcut in _shortcuts) _view.AddChild(shortcut);
        Refresh();
    }

    public override void _Ready()
    {
        _zoneName.Position=new Vector2(155,41);_zoneName.Size=new Vector2(117,16);
        _coordinates.Position=new Vector2(155,58);_coordinates.Size=new Vector2(117,16);
    }

    public override void _EnterTree()
    {
        _game.Character.Changed += Refresh;
        _game.BecameAvailable += Refresh;
        _game.Map.Changed += Refresh;
        LibreKO.HudLayout.Attach(this, "classic_status", _dragHandle, () => ScreenPosition);
        Refresh();
    }

    public override void _ExitTree()
    {
        _game.Character.Changed -= Refresh;
        _game.BecameAvailable -= Refresh;
        _game.Map.Changed -= Refresh;
    }

    private void Refresh()
    {
        var c = _game.Character;
        _mapGroup.Visible=_game.Map.MiniMapVisible;
        Size=_mapGroup.Visible ? _expandedSize : _collapsedSize;
        _dragHandle.Size=new Vector2(_dragHandle.Size.X,Size.Y);
        _view.SetProgress("Progress_HP", Fraction(c.Hp, c.MaxHp));
        _view.SetProgress("Progress_MSP", Fraction(c.Mp, c.MaxMp));
        _view.SetProgress("Progress_ExpP", (float)(c.ExpPercent / 100));
        _view.SetProgress("Progress_ExpC", (float)(c.ExpPercent % 10 / 10));
        _view.SetText("Text_ExpP", $"{c.ExpPercent:0.00}%");
        _view.SetText("Text_Position", $"Lv.{c.Level} {c.Name}");
        if (_view.Get("Text_Position") is Label identity)
            identity.TooltipText=$"Lv.{c.Level} {c.Name}\n{_game.Map.X:0}, {_game.Map.Z:0}";
        _view.SetText("Text_Version", _game.Map.ZoneName);
        _coordinates.Text=$"{(int)_game.Map.X}, {(int)_game.Map.Z}";
        _coordinates.TooltipText=_game.Map.ZoneName;
        if (_view.Get("Text_Version") is Label zone) zone.TooltipText=_game.Map.ZoneName;
        // Zones that show health ten times over scale the text only; the bar keeps its fraction.
        long hpScale = c.HpTextScale;
        _view.SetText("Text_HP", $"{c.Hp * hpScale}/{c.MaxHp * hpScale}");
        _view.SetText("Text_MSP", $"{c.Mp}/{c.MaxMp}");
        _npcList.Refresh();
        _map.QueueRedraw();
    }

    private void CentreInBar(string text,string bar)
    {
        var rect=Plugin.Kit.Layout(LayoutName).Find(bar)!;
        AlignText(text,new Rect2(rect.X,rect.Y-1,rect.W,rect.H+2));
    }

    private void AlignText(string id, Rect2 rect, bool parchment=false)
    {
        if (_view.Get(id) is not Label text) return;
        text.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());
        text.Position=rect.Position;
        text.AddThemeFontOverride("font",Plugin.Kit.ChatStrong);
        text.AddThemeFontSizeOverride("font_size",11);
        text.HorizontalAlignment=HorizontalAlignment.Center;
        text.VerticalAlignment=VerticalAlignment.Center;
        text.AutowrapMode=TextServer.AutowrapMode.Off;
        text.ClipText=true;
        text.Size=rect.Size;
        text.AddThemeColorOverride("font_color",parchment ? new Color(1f,0.94f,0.77f) : Colors.White);
        text.AddThemeColorOverride("font_shadow_color",Colors.Black);
        text.AddThemeConstantOverride("outline_size",1);
        text.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        text.AddThemeColorOverride("font_outline_color",Colors.Black);
    }

    private static float Fraction(int value, int max) => max > 0 ? Mathf.Clamp(value / (float)max, 0f, 1f) : 0f;
}
