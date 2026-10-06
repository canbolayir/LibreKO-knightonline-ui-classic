using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class WorldMapWindow : Control
{
    private const string LayoutName = "co_map1024_us";
    private const string FrameGroup = "Group_uiform";
    private const string ClientArea = "area_client";
    private const string ZoneNameText = "string_select";
    private const float TitleBarHeight = 37f;
    private static readonly string[] HiddenFrameIds =
    {
        "btn_togglefindlist", "btn_togglelist", "string_find_select", "btn_find_check", "btn_quest_check",
    };
    private static readonly string[] HiddenFrameCaptions = { "Scroll Close", "Quest" };

    private static readonly Dictionary<int, string> ZoneGroups = new()
    {
        [1] = "Group_karubase",
        [2] = "Group_elmobase",
        [11] = "Group_islant",
        [12] = "Group_islant",
        [13] = "Group_islant",
        [15] = "Group_islant",
        [21] = "Group_moradon",
        [22] = "Group_moradon",
        [30] = "Group_dellos",
        [66] = "Group_luna_F",
        [71] = "Group_cultivation_new_new",
        [72] = "Group_cultivation_old",
        [73] = "Group_ronak_base",
    };

    private readonly LayoutView _view;
    private readonly LayoutNode _layout;
    private readonly PluginGame _game;
    private readonly WindowHost _host;
    private readonly MapOverlay _overlay;
    private string _shownGroup = "";

    public WorldMapWindow(WindowHost host)
    {
        _host = host;
        var kit = Plugin.Kit;
        _game = kit.Game;
        _layout = kit.Layout(LayoutName);
        _view = new LayoutView(kit, _layout);
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = _layout.SizeVec;
        Size = _layout.SizeVec;
        AddChild(_view);
        foreach (var child in _layout.Children)
            if (child.Type == "base" && child.Id != FrameGroup && _view.ControlOf(child) is { } group)
                group.Visible = false;
        _view.Hide(HiddenFrameIds);
        if (_layout.Find(FrameGroup) is { } frame)
            foreach (var node in frame.Children)
                if (node.IsString && HiddenFrameCaptions.Contains(node.Text) && _view.ControlOf(node) is { } caption)
                    caption.Visible = false;
        host.SetDragHandle(_view.MakeDragHandle(new Rect2(0, 0, _layout.W, TitleBarHeight)));
        _view.OnPressed("btn_close", host.Close);

        _overlay = new MapOverlay(_game);
        _view.AddChild(_overlay);
        Refresh();
    }

    public override void _EnterTree()
    {
        _game.Map.Changed += Refresh;
        _game.BecameAvailable += Refresh;
        _host.Shown += Refresh;
    }

    public override void _ExitTree()
    {
        _game.Map.Changed -= Refresh;
        _game.BecameAvailable -= Refresh;
        _host.Shown -= Refresh;
    }

    private void Refresh()
    {
        var m = _game.Map;
        _view.SetText(ZoneNameText, m.ZoneName);
        string groupId = ZoneGroups.TryGetValue(m.Zone, out var g) ? g : "";
        if (groupId != _shownGroup)
        {
            if (_shownGroup.Length > 0 && _view.Get(_shownGroup) is { } old) old.Visible = false;
            _shownGroup = groupId;
            LayoutNode? area = null;
            if (groupId.Length > 0 && _layout.Find(groupId) is { } group && _view.ControlOf(group) is { } control)
            {
                control.Visible = true;
                area = group.Children.FirstOrDefault(c => c.IsArea);
            }
            area ??= _layout.Find(ClientArea);
            _overlay.Painted = groupId.Length > 0;
            _overlay.Position = area != null ? new Vector2(area.X - _layout.X, area.Y - _layout.Y) : Vector2.Zero;
            _overlay.Size = area?.SizeVec ?? _layout.SizeVec;
        }
        _overlay.QueueRedraw();
    }
}

public partial class MapOverlay : Control
{
    private static readonly Color PlayerColor = new(0.35f, 1f, 0.35f);
    private static readonly Color Background = new(0.05f, 0.05f, 0.06f);
    private const float PlayerArrow = 9f;
    private const float BlipScale = 1.4f;

    public bool Painted { get; set; }

    private readonly PluginGame _game;

    public MapOverlay(PluginGame game)
    {
        _game = game;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var m = _game.Map;
        if (!Painted)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), Background);
            if (m.MapTexture is { } tex) DrawTextureRect(tex, new Rect2(Vector2.Zero, Size), false);
        }
        if (m.WorldExtent <= 0f) return;
        var scale = new Vector2(Size.X / m.WorldExtent, Size.Y / m.WorldExtent);
        foreach (var b in m.Blips)
        {
            var p = new Vector2(b.X * scale.X, (m.WorldExtent - b.Z) * scale.Y);
            if (b.Hollow) DrawArc(p, b.Radius * BlipScale, 0f, Mathf.Tau, 16, b.Color, 1.5f, true);
            else DrawCircle(p, b.Radius * BlipScale, b.Color);
        }
        var mid = new Vector2(m.X * scale.X, (m.WorldExtent - m.Z) * scale.Y);
        float heading = Mathf.DegToRad(m.HeadingDegrees);
        var dir = new Vector2(Mathf.Sin(heading), -Mathf.Cos(heading));
        var side = new Vector2(-dir.Y, dir.X);
        DrawColoredPolygon(new[]
        {
            mid + dir * PlayerArrow,
            mid - dir * PlayerArrow * 0.7f + side * PlayerArrow * 0.6f,
            mid - dir * PlayerArrow * 0.7f - side * PlayerArrow * 0.6f,
        }, PlayerColor);
    }
}
