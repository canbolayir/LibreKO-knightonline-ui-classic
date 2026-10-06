using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class StatusHud : Control
{
    public const string LayoutName = "{nation}_statebar_us";
    public static readonly Vector2 ScreenPosition = new(8, 8);

    private readonly LayoutView _view;
    private readonly PluginGame _game;
    private readonly MapCanvas _map;
    private readonly Control _mapGroup;
    private readonly Control _dragHandle;
    private readonly Vector2 _expandedSize,_collapsedSize;
    private readonly Label _coordinates,_zoneName;

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
        float shortcutRight=power.X+shortcutPitch*2+power.W;
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
        _view.OnPressed("Btn_ZoomIn",()=>_map.Zoom(-1));
        _view.OnPressed("Btn_ZoomOut",()=>_map.Zoom(1));
        CentreInBar("Text_HP","Progress_HP");
        CentreInBar("Text_MSP","Progress_MSP");
        CentreInBar("Text_ExpP","Progress_ExpP");
        // Use the two existing parchment fields; do not add another frame above the HUD.
        AlignText("Text_Position",new Rect2(4,43,116,20),true);
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
        foreach(var (letter,window,title,column) in new[]{
            ("Q","quests","Quests",-1),
            ("P","shoppingmall","Power-Up Store",0),
            ("A","achievements","Achievements",1),
            ("D","attendance","Daily Attendance",2),
        })
            _view.AddChild(new ClassicStatusShortcut(kit,quest,letter,window,title) {
                Position=new Vector2(power.X+shortcutPitch*column,power.Y),
            });
        _view.AddChild(new ClassicStatusShortcut(kit,quest,"","mail","Mail") {
            Position=new Vector2(quest.X,quest.Y+quest.H+5),
        });
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
        _view.SetText("Text_HP", $"{c.Hp}/{c.MaxHp}");
        _view.SetText("Text_MSP", $"{c.Mp}/{c.MaxMp}");
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
