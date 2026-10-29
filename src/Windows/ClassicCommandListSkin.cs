using Godot;
using LibreKO;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;
using Row = KnightOnlineUiClassic.Native.NativeCommandList.Row;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The command list on the original 1.298 command window (<c>{nation}_cmdlist_us</c>): the translucent panel
/// with the close ornament, the "Command" caption, the categories in the upper box and the commands in the
/// lower box, original scroll bars, yellow categories, white commands, the green outline on the selected row
/// and the yellow outline around the list in use. The client's command list stays the list that runs the
/// commands; the categories are the Classic grouping (<see cref="ClassicCommandListLayout.Groups"/>).
/// </summary>
public static class ClassicCommandListSkin
{
    public static readonly string[] WindowIds = { ClassicCommandListLayout.WindowId };

    public static void Register(PluginUi ui) => ui.FrameWindow(ClassicCommandListLayout.WindowId, frame => new ClassicCommandListFrame(frame));

    /// <summary>The Classic groups filled with the client's rows; client rows the table does not name join their client group.</summary>
    public static List<(string Title, List<Row> Rows)> Group(IReadOnlyList<Row> rows)
    {
        var groups = ClassicCommandListLayout.Groups.Select(g => (g.Title, Rows: new List<Row>())).ToList();
        foreach (var row in rows)
        {
            int at = Array.FindIndex(ClassicCommandListLayout.Groups, g => g.Commands.Contains(row.Id));
            if (at < 0) at = Math.Max(0, Array.FindIndex(ClassicCommandListLayout.Groups, g => g.ClientGroup == row.ClientGroup));
            groups[at].Rows.Add(row);
        }
        foreach (var (group, index) in ClassicCommandListLayout.Groups.Select((g, i) => (g, i)))
        {
            // Table order first, then commands the table does not know; commands the client has not built go last.
            // Client actions (the Genie rows) come after the client's entries and keep their order: their state changes while the list is open.
            int Rank(Row row) { int i = Array.IndexOf(group.Commands, row.Id); return i < 0 ? group.Commands.Length : i; }
            var ordered = groups[index].Rows.OrderBy(r => r.Entry == null ? 2 : r.Available ? 0 : 1).ThenBy(Rank).ToList();
            groups[index] = (group.Title, ordered);
        }
        return groups.Where(g => g.Rows.Count > 0).ToList();
    }
}

/// <summary>The original command window around the client's command list.</summary>
public partial class ClassicCommandListFrame : Control
{
    private readonly WindowFrame? _frame;
    private readonly ClassicCommandListLayout.Nation _n = ClassicCommandListLayout.ElMorad;
    private readonly Control _content = null!;
    private readonly ItemList _groups = null!;
    private readonly Label _title = null!;
    private readonly Button _close = null!;
    private readonly ClassicCommandListFocus _focus = null!;
    private readonly ClassicCommandScroll _groupScroll = null!;
    private ClassicCommandScroll? _commandScroll;
    private WorldRoot? _world;
    private ItemList? _entries;
    private List<(string Title, List<Row> Rows)> _model = new();
    private IReadOnlyList<Row> _shown = Array.Empty<Row>();
    private readonly Dictionary<int, NativeGenieCommands.State> _actionStates = new();

    public ClassicCommandListFrame() { }

    public ClassicCommandListFrame(WindowFrame frame)
    {
        _frame = frame;
        _n = ClassicCommandListLayout.For(ClassicDesign.Karus);
        Name = "classic_command_list"; MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest;
        var uif = Plugin.Kit.Layout(ClassicCommandListLayout.Uif);
        Art(_n.Panel, _n.PanelSource, "command_panel");
        Art(_n.Foot, _n.FootSource, "command_foot");
        var grip = new Control { Name = "command_drag", MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Move };
        Place(grip, ClassicCommandListLayout.Drag(_n));
        frame.SetDragHandle(grip);
        _title = new Label { Name = "command_title", Text = frame.Title.Trim(), MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipText = true };
        ClassicSocialStyle.Font(_title, true, ClassicCommandListLayout.TitleFontSize);
        _title.AddThemeColorOverride("font_color", _n.TitleInk);
        // The original caption is centred in a 16 px band; Arial Bold 16 px needs a taller line, centred on the band.
        float line = Mathf.Max(_n.Title.Size.Y, _title.GetMinimumSize().Y);
        Place(_title, new Rect2(_n.Title.Position.X, Mathf.Round(_n.Title.GetCenter().Y - line / 2), _n.Title.Size.X, line));
        frame.TitleChanged += title => { if (IsInstanceValid(_title)) _title.Text = title.Trim(); };
        _close = new Button { Name = "command_close", TooltipText = "Close", FocusMode = FocusModeEnum.None };
        ClassicReportDesign.StyleButton(_close, false, uif.Find("btn_cancel"));
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            if (_close.GetThemeStylebox(state) is StyleBoxTexture box) box.SetContentMarginAll(0);
        _close.Pressed += () => _frame?.Close();
        Place(_close, _n.Close);
        _close.Visible = frame.Closable;

        _groups = new ItemList { Name = "command_groups", FocusMode = FocusModeEnum.None, SelectMode = ItemList.SelectModeEnum.Single, AllowReselect = true };
        Style(_groups, ClassicCommandListLayout.GroupInk);
        Place(_groups, _n.Groups);
        _groups.ItemSelected += index => { _focus.Commands = false; ShowGroup((int)index); };
        _groupScroll = new ClassicCommandScroll(_groups, _n.GroupScroll, uif.Find("list_curtailment")) { Name = "command_group_scroll" };
        AddChild(_groupScroll);

        // The client's flow (its own two lists until the window binds) waits hidden in the window.
        _content = frame.Content;
        _content.GetParent()?.RemoveChild(_content);
        _content.Visible = false;
        AddChild(_content);
        _focus = new ClassicCommandListFocus(_n) { Name = "command_focus" };
        AddChild(_focus);
        frame.Shown += () => Callable.From(Refresh).CallDeferred();
    }

    public ItemList GroupList => _groups;
    public ItemList? CommandList => _entries;
    public bool Bound => _world != null;
    public ClassicCommandListLayout.Nation Geometry => _n;
    public IReadOnlyList<(string Title, List<Row> Rows)> Model => _model;
    public bool CommandsFocused => _focus.Commands;
    public IReadOnlyList<Row> ShownRows => _shown;

    public override Vector2 _GetMinimumSize() => _n.Size;

    private int _unboundFrames;

    public override void _Process(double delta)
    {
        if (_world != null) { RefreshActions(); return; }
        if (GetParent() is not HudWindow window) return;
        if (window.HasMeta(NativeCommandList.Gate))
        {
            if (window.GetMeta(NativeCommandList.WorldMeta).AsGodotObject() is WorldRoot world && IsInstanceValid(world)) Bind(world);
            return;
        }
        // Without the client members the preparer needs, the client's own two lists stay usable on the original art.
        if (!window.IsVisibleInTree() || ++_unboundFrames < 30) return;
        _groups.Visible = _focus.Visible = false;
        var lists = ClassicSocialStyle.Descendants(_content).OfType<ItemList>().Take(2).ToArray();
        for (int i = 0; i < lists.Length; i++)
        {
            lists[i].Reparent(this, false);
            Place(lists[i], i == 0 ? _n.Groups : _n.Commands);
        }
        SetProcess(false);
    }

    /// <summary>Takes the client's command list into the lower box and lists the Classic groups in the upper box.</summary>
    private void Bind(WorldRoot world)
    {
        if (NativeCommandList.Entries(world) is not { } entries) return;
        _world = world;
        _entries = entries;
        entries.Reparent(this, false);
        entries.Visible = true;
        Style(entries, ClassicCommandListLayout.CommandInk);
        Place(entries, _n.Commands);
        entries.ItemSelected += _ => _focus.Commands = true;
        entries.ItemClicked += (_, _, _) => _focus.Commands = true;
        // The client's handler runs its own entries; the rows after them are client actions run here.
        entries.ItemActivated += index => RunAction((int)index);
        _commandScroll = new ClassicCommandScroll(entries, _n.CommandScroll, Plugin.Kit.Layout(ClassicCommandListLayout.Uif).Find("list_content")) { Name = "command_entry_scroll" };
        AddChild(_commandScroll);
        MoveChild(_focus, -1);
        _model = ClassicCommandListSkin.Group(NativeCommandList.Read(world).Concat(NativeGenieCommands.Rows(world)).ToList());
        _groups.Clear();
        foreach (var (title, _) in _model)
        {
            var group = ClassicCommandListLayout.Groups.First(g => g.Title == title);
            int row = _groups.AddItem(group.ClientGroup is { } client ? NativeCommandList.GroupTitle(world, client) ?? title : title);
            _groups.SetItemTooltip(row, "");
        }
        _groups.Select(0);
        ShowGroup(0);
    }

    /// <summary>The client refills its list whenever the window opens; show the selected Classic group again.</summary>
    private void Refresh()
    {
        if (_world == null || !IsInstanceValid(_world) || _groups.ItemCount == 0) return;
        int[] picked = _groups.GetSelectedItems();
        if (picked.Length == 0) _groups.Select(0);
        _focus.Commands = false;
        ShowGroup(picked.Length > 0 ? picked[0] : 0);
    }

    public void ShowGroup(int index)
    {
        if (index >= 0 && index < _model.Count) ShowRows(_model[index].Rows);
    }

    /// <summary>Lists <paramref name="rows"/> in the client's command list: white commands, unavailable ones grey and not selectable.</summary>
    public void ShowRows(IReadOnlyList<Row> rows)
    {
        if (_world == null) return;
        _shown = rows;
        _actionStates.Clear();
        NativeCommandList.Show(_world, rows, (list, row, entry) => Mark(list, row, entry.Available));
        if (_entries != null) _entries.GetVScrollBar().Value = 0;
        RefreshActions();
    }

    private static void Mark(ItemList list, int row, bool available)
    {
        list.SetItemCustomFgColor(row, available ? ClassicCommandListLayout.CommandInk : ClassicCommandListLayout.UnavailableInk);
        list.SetItemDisabled(row, !available);
    }

    /// <summary>Client actions follow the client's state while shown: grey and disabled, with the client's reason, when they cannot run.</summary>
    private void RefreshActions()
    {
        if (_world == null || !IsInstanceValid(_world) || _entries == null || !IsVisibleInTree()) return;
        for (int i = 0; i < _shown.Count && i < _entries.ItemCount; i++)
        {
            if (_shown[i].Entry != null) continue;
            var state = NativeGenieCommands.StateOf(_world, _shown[i].Id);
            if (_actionStates.TryGetValue(i, out var last) && last == state) continue;
            _actionStates[i] = state;
            Mark(_entries, i, state.Enabled);
            _entries.SetItemTooltip(i, state.Tooltip);
            if (!state.Enabled && _entries.IsSelected(i)) _entries.Deselect(i);
        }
    }

    private void RunAction(int index)
    {
        if (_world == null || !IsInstanceValid(_world) || index < 0 || index >= _shown.Count || _shown[index].Entry != null) return;
        NativeGenieCommands.Run(_world, _shown[index].Id);
        RefreshActions();
    }

    private void Art(Rect2 rect, Rect2 source, string name)
    {
        var art = new TextureRect
        {
            Name = name, MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = new AtlasTexture { Atlas = Plugin.Kit.Texture(ClassicCommandListLayout.Texture), Region = source, FilterClip = true },
        };
        Place(art, rect);
    }

    private void Place(Control control, Rect2 rect)
    {
        if (control.GetParent() != this) AddChild(control);
        control.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        control.CustomMinimumSize = Vector2.Zero;
        control.Position = rect.Position; control.Size = rect.Size;
        control.SetMeta("command_expected_rect", rect);
    }

    /// <summary>Original list look: GulimChe-sized Arial rows stacked by text height, the green outline on the selected row.</summary>
    private static void Style(ItemList list, Color ink)
    {
        var panel = new StyleBoxEmpty();
        panel.SetContentMarginAll(0);
        list.AddThemeStyleboxOverride("panel", panel);
        list.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        list.AddThemeStyleboxOverride("cursor", new StyleBoxEmpty());
        list.AddThemeStyleboxOverride("cursor_unfocused", new StyleBoxEmpty());
        foreach (var state in new[] { "selected", "selected_focus", "hovered_selected", "hovered_selected_focus" })
            list.AddThemeStyleboxOverride(state, Outline(ClassicCommandListLayout.SelectedLine));
        list.AddThemeStyleboxOverride("hovered", Outline(ClassicCommandListLayout.HoverLine));
        list.AddThemeFontOverride("font", Plugin.Kit.Regular);
        list.AddThemeFontSizeOverride("font_size", ClassicCommandListLayout.ListFontSize);
        list.AddThemeConstantOverride("outline_size", 0);
        foreach (var state in new[] { "font_color", "font_selected_color", "font_hovered_color", "font_hovered_selected_color" })
            list.AddThemeColorOverride(state, ink);
        list.AddThemeColorOverride("font_outline_color", Colors.Transparent);
        // The original rows have no rule between them.
        list.AddThemeColorOverride("guide_color", Colors.Transparent);
        list.AddThemeConstantOverride("v_separation", ClassicCommandListLayout.RowGap);
        list.AddThemeConstantOverride("h_separation", ClassicCommandListLayout.TextInset * 2);
        list.AddThemeConstantOverride("line_separation", 0);
        list.AutoHeight = false;
        list.ClipContents = true;
        // The original scroll bar is drawn by the window; the list keeps its own bar for the row width and the value.
        var bar = list.GetVScrollBar();
        bar.SelfModulate = Colors.Transparent;
        bar.MouseFilter = MouseFilterEnum.Ignore;
        bar.CustomMinimumSize = new Vector2(16, 0);
        foreach (var box in new[] { "scroll", "scroll_focus", "grabber", "grabber_highlight", "grabber_pressed" })
            bar.AddThemeStyleboxOverride(box, new StyleBoxEmpty());
    }

    private static StyleBoxFlat Outline(Color line)
    {
        var box = new StyleBoxFlat { DrawCenter = false, BorderColor = line, AntiAliasing = false };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(0);
        return box;
    }
}

/// <summary>The yellow outline 2 px around the list in use (the original <c>RenderSelectionBorder</c>).</summary>
public partial class ClassicCommandListFocus : Control
{
    private readonly ClassicCommandListLayout.Nation _n = ClassicCommandListLayout.ElMorad;
    private bool _commands;

    public ClassicCommandListFocus() { }
    public ClassicCommandListFocus(ClassicCommandListLayout.Nation n) { _n = n; MouseFilter = MouseFilterEnum.Ignore; Size = n.Size; }

    public bool Commands { get => _commands; set { _commands = value; QueueRedraw(); } }
    public Rect2 Outline => (_commands ? _n.Commands : _n.Groups).Grow(ClassicCommandListLayout.FocusOutset);

    public override void _Draw()
    {
        var r = Outline;
        // 1 px lines on whole pixels (rectangle outlines in Godot straddle the edge).
        DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 1), ClassicCommandListLayout.FocusLine);
        DrawRect(new Rect2(r.Position.X, r.End.Y - 1, r.Size.X, 1), ClassicCommandListLayout.FocusLine);
        DrawRect(new Rect2(r.Position.X, r.Position.Y, 1, r.Size.Y), ClassicCommandListLayout.FocusLine);
        DrawRect(new Rect2(r.End.X - 1, r.Position.Y, 1, r.Size.Y), ClassicCommandListLayout.FocusLine);
    }
}

/// <summary>
/// The original list scroll bar over a native <see cref="ItemList"/>: arrow buttons with their art, a 14 x 16 thumb
/// that slides in the track, track clicks and thumb drags. It only shows when the list has more rows than fit
/// (as the original), keeps the list to whole rows and snaps the scroll value to rows.
/// </summary>
public partial class ClassicCommandScroll : Control
{
    private readonly ItemList _list = null!;
    private readonly ClassicCommandListLayout.Scroll _scroll = null!;
    private readonly Rect2 _listRect;
    private readonly Button _up = null!, _down = null!;
    private readonly TextureRect _thumb = null!;
    private readonly StyleBox _hover = null!, _noHover = new StyleBoxEmpty();
    private bool _dragging;

    public ClassicCommandScroll() { }

    public ClassicCommandScroll(ItemList list, ClassicCommandListLayout.Scroll scroll, LayoutNode? uif)
    {
        _list = list; _scroll = scroll; _listRect = list.GetMeta("command_expected_rect").AsRect2();
        _hover = list.GetThemeStylebox("hovered");
        // Unavailable rows cannot be selected, so they show no hover outline.
        list.GuiInput += e => { if (e is InputEventMouseMotion motion) Hovered(list.GetItemAtPosition(motion.Position, true)); };
        list.MouseExited += () => Hovered(-1);
        MouseFilter = MouseFilterEnum.Stop;
        Position = scroll.Area.Position; Size = scroll.Area.Size;
        SetMeta("command_expected_rect", scroll.Area);
        var bar = uif?.Children.FirstOrDefault(c => c.Type == "scrollbar");
        var arrows = bar?.Children.Where(c => c.IsButton).ToArray() ?? Array.Empty<LayoutNode>();
        _up = Arrow("command_scroll_up", arrows.FirstOrDefault(), scroll.Up, -1);
        _down = Arrow("command_scroll_down", arrows.Skip(1).FirstOrDefault(), scroll.Down, 1);
        var thumb = bar?.Children.FirstOrDefault(c => c.Type == "trackbar")?.Images.FirstOrDefault(i => i.Tag == 1);
        _thumb = new TextureRect
        {
            Name = "command_scroll_thumb", MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Size = ClassicCommandListLayout.Thumb,
            Texture = thumb?.Texture is { } t ? new AtlasTexture { Atlas = Plugin.Kit.Texture(t), Region = new Rect2(thumb.SrcX, thumb.SrcY, thumb.SrcW, thumb.SrcH), FilterClip = true } : null,
        };
        AddChild(_thumb);
    }

    public float Pitch
    {
        get
        {
            if (_list.ItemCount == 0) return 1;
            var first = _list.GetItemRect(0, false);
            return _list.ItemCount > 1 ? _list.GetItemRect(1, false).Position.Y - first.Position.Y : first.Size.Y + _list.GetThemeConstant("v_separation");
        }
    }

    public bool Needed => _list.GetVScrollBar() is var bar && bar.MaxValue - bar.Page > .5;
    public Rect2 ThumbRect => _thumb.GetRect();
    public Button Up => _up;
    public Button Down => _down;

    private Button Arrow(string name, LayoutNode? art, Rect2 rect, int direction)
    {
        var button = new Button { Name = name, FocusMode = FocusModeEnum.None, Text = "" };
        ClassicReportDesign.StyleButton(button, false, art);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            if (button.GetThemeStylebox(state) is StyleBoxTexture box) { box.SetContentMarginAll(0); box.SetTextureMarginAll(0); }
        button.Position = rect.Position - _scroll.Area.Position; button.Size = rect.Size;
        button.SetMeta("command_expected_rect", rect);
        button.Pressed += () => Step(direction);
        AddChild(button);
        return button;
    }

    private void Hovered(int index)
    {
        if (!IsInstanceValid(_list)) return;
        var hover = index >= 0 && index < _list.ItemCount && _list.IsItemDisabled(index) ? _noHover : _hover;
        if (_list.GetThemeStylebox("hovered") != hover) _list.AddThemeStyleboxOverride("hovered", hover);
    }

    public void Step(int rows)
    {
        var bar = _list.GetVScrollBar();
        bar.Value = Math.Clamp(bar.Value + rows * Pitch, 0, Math.Max(0, bar.MaxValue - bar.Page));
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            _dragging = button.Pressed && Track().HasPoint(button.Position);
            if (_dragging) Seek(button.Position.Y);
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion motion && _dragging) { Seek(motion.Position.Y); AcceptEvent(); }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
        {
            Step(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
            AcceptEvent();
        }
    }

    private Rect2 Track() => new(_scroll.Track.Position - _scroll.Area.Position, _scroll.Track.Size);

    private void Seek(float y)
    {
        var track = Track(); var bar = _list.GetVScrollBar();
        double ratio = (y - track.Position.Y - ClassicCommandListLayout.Thumb.Y / 2) / Math.Max(1, track.Size.Y - ClassicCommandListLayout.Thumb.Y);
        double range = Math.Max(0, bar.MaxValue - bar.Page);
        bar.Value = Math.Round(Math.Clamp(ratio, 0, 1) * range / Pitch) * Pitch;
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_list)) return;
        // Whole rows only: the list is as tall as the rows that fit in its original box.
        float pitch = Pitch;
        if (_list.ItemCount > 0 && pitch > 1)
        {
            float height = Mathf.Floor(_listRect.Size.Y / pitch) * pitch;
            if (Math.Abs(_list.Size.Y - height) > .5f) _list.Size = new Vector2(_list.Size.X, height);
        }
        var bar = _list.GetVScrollBar();
        bool needed = Needed;
        Visible = needed;
        if (!needed) return;
        double snapped = Math.Round(bar.Value / pitch) * pitch;
        if (!_dragging && !Input.IsMouseButtonPressed(MouseButton.Left) && Math.Abs(bar.Value - snapped) > .01) bar.Value = snapped;
        double range = Math.Max(1, bar.MaxValue - bar.Page);
        var rect = ClassicCommandListLayout.ThumbAt(Track(), bar.Value / range);
        if (_thumb.Position != rect.Position) _thumb.Position = rect.Position;
        _up.Disabled = bar.Value <= .01;
        _down.Disabled = bar.Value >= range - .01;
    }
}
