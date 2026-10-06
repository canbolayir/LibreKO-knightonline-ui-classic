using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class QuestTracker : Control
{
    private const string LayoutName = "co_quest_mini_tip_us";
    private const string TitleBarId = "MiniBar_Title";
    private const string ListBarId = "MiniBar_List";
    private const string PageBarId = "MiniBar_Page";
    private const string RowBackgroundId = "img_middle";
    private const string RowLineId = "img_bottom";
    private const string TitleCountId = "str_title";
    private const string QuestTitleId = "szText";
    private const string PageNumberId = "str_pagenum";
    private const string ObjectivesToggle = "btn_list_title";
    private const string QuestsWindow = "quests";
    private const string LayoutId = "theme_quest_tip";
    private const float ScreenMargin = 8f;
    private const int ObjectiveFontSize = 11;
    private const float ObjectiveRowHeight = 16f;
    private const float ObjectiveLeft = 26f;
    private const float ObjectiveWidth = 187f;
    private static readonly Color ObjectiveColor = Colors.White;
    private static readonly Color DoneColor = new(0.6f, 1f, 0.6f);
    private static readonly Color ShadowColor = new(0, 0, 0, 0.75f);

    private readonly LayoutView _view;
    private readonly LayoutNode _layout;
    private readonly PluginGame _game;
    private readonly Control? _rowBackground;
    private readonly Control? _rowLine;
    private readonly Control? _pageBar;
    private readonly Control? _listBar;
    private readonly List<Label> _objectives = new();
    private readonly float _listTop;
    private readonly float _listBottom;
    private readonly float _pageBarHeight;
    private readonly float _titleHeight;
    private HudLayout? _hudLayout;
    private int _page;
    private bool _closed;
    private bool _minimised;
    private bool _objectivesOpen = true;

    public QuestTracker()
    {
        var kit = Plugin.Kit;
        _game = kit.Game;
        _layout = kit.Layout(LayoutName);
        _view = new LayoutView(kit, _layout);
        MouseFilter = MouseFilterEnum.Ignore;
        Size = _layout.SizeVec;
        AddChild(_view);
        _rowBackground = _view.Get(RowBackgroundId);
        _rowLine = _view.Get(RowLineId);
        _pageBar = _view.Get(PageBarId);
        _listBar = _view.Get(ListBarId);
        var list = _layout.Find(ListBarId);
        var page = _layout.Find(PageBarId);
        var title = _layout.Find(TitleBarId);
        _listTop = list != null ? list.Y - _layout.Y : 0f;
        _listBottom = list != null ? list.Y + list.H - _layout.Y : 0f;
        _pageBarHeight = page?.H ?? 0f;
        _titleHeight = title != null ? title.Y + title.H - _layout.Y : 0f;

        _view.OnPressed("btn_close", () => { _closed = true; Visible = false; });
        _view.OnPressed("btn_mini", () => SetMinimised(true));
        _view.OnPressed("btn_max", () => SetMinimised(false));
        _view.OnPressed(ObjectivesToggle, () => SetObjectivesOpen(!_objectivesOpen));
        _view.OnPressed("btn_prevpage", () => Turn(-1));
        _view.OnPressed("btn_nextpage", () => Turn(1));
        _view.OnPressed("btn_seedopen", () => _game.Windows.Toggle(QuestsWindow));
        SetMinimised(false);
    }

    public override void _EnterTree()
    {
        _game.Quests.Changed += Refresh;
        _game.BecameAvailable += Refresh;
        _hudLayout ??= HudLayout.Attach(this, LayoutId, _view.Get(TitleBarId), DefaultPosition);
        Refresh();
    }

    public override void _ExitTree()
    {
        _game.Quests.Changed -= Refresh;
        _game.BecameAvailable -= Refresh;
    }

    private Vector2 DefaultPosition()
    {
        var room = GetViewport().GetVisibleRect().Size;
        return new Vector2(room.X - _layout.W - ScreenMargin, MiniMapFrame.FrameHeight + 2 * ScreenMargin);
    }

    private void Turn(int delta)
    {
        int count = _game.Quests.Tracked.Count;
        if (count == 0) return;
        _page = ((_page + delta) % count + count) % count;
        Refresh();
    }

    private void SetMinimised(bool minimised)
    {
        _minimised = minimised;
        _view.Hide(minimised ? "btn_mini" : "btn_max");
        if (_view.Get(minimised ? "btn_max" : "btn_mini") is { } shown) shown.Visible = true;
        Refresh();
    }

    private void SetObjectivesOpen(bool open)
    {
        _objectivesOpen = open;
        Refresh();
    }

    private void Refresh()
    {
        var quests = _game.Quests.Tracked;
        Visible = !_closed && quests.Count > 0;
        foreach (var label in _objectives) label.QueueFree();
        _objectives.Clear();
        if (quests.Count == 0) return;
        _page = Mathf.Clamp(_page, 0, quests.Count - 1);
        var quest = quests[_page];
        _view.SetText(TitleCountId, quests.Count.ToString());
        _view.SetClippedText(QuestTitleId, quest.Title);
        _view.SetText(PageNumberId, (_page + 1).ToString());

        float bottom = _listBottom;
        if (_objectivesOpen && !_minimised)
        {
            foreach (var line in quest.Objectives)
            {
                var label = new Label
                {
                    Text = line,
                    Position = new Vector2(ObjectiveLeft, bottom),
                    Size = new Vector2(ObjectiveWidth, ObjectiveRowHeight),
                    ClipText = true,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                label.AddThemeFontSizeOverride("font_size", ObjectiveFontSize);
                label.AddThemeFontOverride("font", Plugin.Kit.Regular);
                label.AddThemeColorOverride("font_color", quest.ReadyToTurnIn ? DoneColor : ObjectiveColor);
                label.AddThemeColorOverride("font_shadow_color", ShadowColor);
                label.AddThemeConstantOverride("shadow_offset_x", 1);
                label.AddThemeConstantOverride("shadow_offset_y", 1);
                _view.AddChild(label);
                _objectives.Add(label);
                bottom += ObjectiveRowHeight;
            }
        }
        Layout(bottom);
    }

    private void Layout(float contentBottom)
    {
        bool body = !_minimised;
        if (_pageBar != null) _pageBar.Visible = body;
        if (_listBar != null) _listBar.Visible = body;
        if (!body)
        {
            Size = new Vector2(_layout.W, _titleHeight);
            return;
        }
        float rowBottom = contentBottom - _listTop - 1f;
        if (_pageBar != null) _pageBar.Position = new Vector2(_pageBar.Position.X, contentBottom);
        if (_rowBackground != null) _rowBackground.Size = new Vector2(_rowBackground.Size.X, rowBottom);
        if (_rowLine != null) _rowLine.Position = new Vector2(_rowLine.Position.X, rowBottom);
        Size = new Vector2(_layout.W, contentBottom + _pageBarHeight);
    }
}
