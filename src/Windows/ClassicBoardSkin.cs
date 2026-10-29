using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// Bounty board, sheriff reports, fishing hall of fame and nation force on party board artwork. The native
/// forms, lists, paging and actions are reparented and keep their callbacks; rows stay server-built.
/// </summary>
public static class ClassicBoardSkin
{
    public static readonly string[] WindowIds = NativeBoards.WindowIds;
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicBoardPanel? Apply(Control body)
    {
        Node? root = body;
        while (root != null && root is not HudWindow) root = root.GetParent();
        if (root is not HudWindow window || window.HasMeta("classic_board")) return null;
        if (window.GetMeta(NativeBoards.Prepared, 0).AsInt32() != 1) return null;
        window.SetMeta("classic_board", true);
        var panel = new ClassicBoardPanel(window); window.AddChild(panel); window.ResetSize(); return panel;
    }
}

public partial class ClassicBoardPanel : Control
{
    public static bool Karus => Plugin.Kit.Nation == 1;
    public static Color Heading => new(Karus ? "dbdbdb" : "f8d987");
    public static Color PageInk => new(Karus ? "c0c0c0" : "ffff80");
    public static Color ButtonInk => Karus ? new Color("17130f") : new Color("f8d987");
    public static readonly Color Ink = Colors.White, Medal = new("f8d987"), Highlight = new("ffff80"), Muted = new("b4aea1"), Selection = new("00ff00");
    public const int TitleSize = 17, HeadSize = 15, BodySize = 12, ButtonSize = 13;

    public HudWindow Window { get; }
    public VBoxContainer? Rows { get; private set; }
    public Control? Selected { get; private set; }
    public int Page { get; private set; }
    public int Pages { get; private set; } = 1;

    private readonly LayoutNode _art;
    private readonly Control[] _controls;
    private readonly List<Rect2> _boxes = new(), _slots = new();
    private readonly List<(float X, float Top, float Bottom)> _lines = new();
    private readonly List<(float Left, float Right, float Y)> _rules = new();
    private ClassicBoardLayout.Column[] _columns = Array.Empty<ClassicBoardLayout.Column>();
    private Rect2 _rowsRect;
    private float _headEnd;
    private int _pageRows;
    private bool _paged;
    private Label? _page, _detail;
    private Button? _up, _down, _claim, _voteYes, _voteNo;
    private ScrollContainer? _detailScroll;
    private ClassicBoardGauge? _gauge;
    private Label? _percent;

    public ClassicBoardPanel(HudWindow window)
    {
        Window = window; Name = "classic_board"; TextureFilter = TextureFilterEnum.Nearest;
        _art = Plugin.Kit.Layout(ClassicBoardLayout.Art);
        _controls = CharacterDetailsSkin.Tree(window).OfType<Control>().ToArray();
        Size = CustomMinimumSize = window.Id switch
        {
            "bounty" => ClassicBoardLayout.Bounty.Size,
            "report" => ClassicBoardLayout.Report.Size,
            "fishinghall" => ClassicBoardLayout.Fishing.Size,
            _ => ClassicBoardLayout.Forces.Size,
        };
        var bar = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        ClassicPartySkin.Drag(this, bar, ClassicBoardLayout.Drag(Size.X));
        var title = Find<Label>("board_title");
        Text(title, ClassicBoardLayout.Title(Size.X), true, TitleSize, Heading, HorizontalAlignment.Center);
        Shadow(title);
        var close = Find<Button>("board_close");
        Place(close, ClassicBoardLayout.Close(Size.X));
        ClassicChatControls.SkinButton(close, _art.Find("btn_exit")!);
        close.Text = ""; close.Icon = null; close.FocusMode = FocusModeEnum.None; close.TooltipText = "Close";
        switch (window.Id)
        {
            case "bounty": BuildBounty(); break;
            case "report": BuildReport(); break;
            case "fishinghall": BuildFishing(); break;
            default: BuildForces(); break;
        }
    }

    private T Find<T>(string name) where T : Control => _controls.OfType<T>().Single(c => c.Name == name);

    private void BuildBounty()
    {
        _boxes.Add(ClassicBoardLayout.Bounty.Box);
        var box = ClassicBoardLayout.Bounty.Box;
        Table(box, ClassicBoardLayout.Bounty.Columns, ClassicBoardLayout.Bounty.Lines, ClassicBoardLayout.Bounty.HeadRule, ClassicBoardLayout.Bounty.FormRule,
            new[] { "Target", "Reward", "Posted by" });
        _rules.Add((box.Position.X, box.End.X, ClassicBoardLayout.Bounty.FormRule));
        Text(Find<Label>("bounty_post_title"), ClassicBoardLayout.Bounty.FormTitle, true, BodySize + 1, Heading, HorizontalAlignment.Left);
        Find<Label>("bounty_list_title").Visible = false;
        Text(Find<Label>("bounty_target_caption"), ClassicBoardLayout.Bounty.TargetCaption, true, BodySize, Ink, HorizontalAlignment.Left);
        Text(Find<Label>("bounty_reward_caption"), ClassicBoardLayout.Bounty.RewardCaption, true, BodySize, Ink, HorizontalAlignment.Left);
        Input(Find<LineEdit>("bounty_target"), ClassicBoardLayout.Bounty.Target);
        Input(Find<LineEdit>("bounty_reward"), ClassicBoardLayout.Bounty.Reward);
        ArtButton(Find<Button>("bounty_post"), _art.Find("btn_add")!, ClassicBoardLayout.Bounty.Post);
        List(ClassicBoardLayout.Bounty.Rows, ClassicBoardLayout.Bounty.PageRows, ClassicBoardLayout.Bounty.Paging, true);
        _claim = new Button { Name = "bounty_claim", Text = "Claim", Disabled = true, TooltipText = "Claim the selected bounty" };
        AddChild(_claim); ArtButton(_claim, _art.Find("btn_delete")!, ClassicBoardLayout.PageAction(ClassicBoardLayout.Bounty.Paging));
        _claim.Pressed += () => Forward("Claim");
    }

    private void BuildFishing()
    {
        var box = ClassicBoardLayout.Fishing.Box;
        _boxes.Add(box);
        Text(Find<Label>("fishing_title"), ClassicBoardLayout.Fishing.Caption, true, BodySize + 1, Heading, HorizontalAlignment.Center);
        Table(box, ClassicBoardLayout.Fishing.Columns, ClassicBoardLayout.Fishing.Lines, ClassicBoardLayout.Fishing.HeadRule, box.End.Y,
            new[] { "Rank", "Angler", "Score" });
        List(ClassicBoardLayout.Fishing.Rows, ClassicBoardLayout.Fishing.PageRows, ClassicBoardLayout.Fishing.Paging, true);
    }

    private void BuildReport()
    {
        var gate = Find<Label>("report_gate");
        Text(gate, ClassicBoardLayout.Report.Gate, true, BodySize, Muted, HorizontalAlignment.Center);
        gate.AutowrapMode = TextServer.AutowrapMode.WordSmart; gate.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
        _boxes.Add(ClassicBoardLayout.Report.FormBox);
        Text(Find<Label>("report_file_title"), ClassicBoardLayout.Report.FormTitle, true, BodySize + 1, Heading, HorizontalAlignment.Left);
        Text(Find<Label>("report_target_caption"), ClassicBoardLayout.Report.TargetCaption, true, BodySize, Ink, HorizontalAlignment.Left);
        Text(Find<Label>("report_reason_caption"), ClassicBoardLayout.Report.ReasonCaption, true, BodySize, Ink, HorizontalAlignment.Left);
        Input(Find<LineEdit>("report_target"), ClassicBoardLayout.Report.Target);
        Input(Find<LineEdit>("report_reason"), ClassicBoardLayout.Report.Reason);
        ArtButton(Find<Button>("report_file"), _art.Find("btn_add")!, ClassicBoardLayout.Report.File);
        Text(Find<Label>("report_list_title"), ClassicBoardLayout.Report.ListTitle, true, BodySize + 1, Heading, HorizontalAlignment.Center);
        if (_controls.OfType<HSeparator>().FirstOrDefault(c => c.Name == "report_separator") is { } separator) separator.Visible = false;
        var box = ClassicBoardLayout.Report.Box;
        _boxes.Add(box);
        Table(box, ClassicBoardLayout.Report.Columns, ClassicBoardLayout.Report.Lines, ClassicBoardLayout.Report.HeadRule, ClassicBoardLayout.Report.DetailRule,
            new[] { "Accused", "Votes" });
        _rules.Add((box.Position.X, box.End.X, ClassicBoardLayout.Report.DetailRule));
        // The server pages the reports; the native arrows, page and Refresh drive it.
        List(ClassicBoardLayout.Report.Rows, ClassicBoardLayout.Report.PageRows, ClassicBoardLayout.Report.Paging, false);
        float y = ClassicBoardLayout.Report.Paging;
        Arrow(Find<Button>("report_prev"), "btn_page_up", ClassicBoardLayout.PageUp(y));
        Arrow(Find<Button>("report_next"), "btn_page_down", ClassicBoardLayout.PageDown(y));
        var page = Find<Label>("report_page");
        Text(page, ClassicBoardLayout.PageText(y), true, ButtonSize, PageInk, HorizontalAlignment.Center);
        ArtButton(Find<Button>("report_refresh"), _art.Find("btn_refresh")!, ClassicBoardLayout.PageAction(y));
        // The selected report's reason, scrolled inside the lower table area.
        _detailScroll = new ScrollContainer { Name = "report_detail_scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever };
        AddChild(_detailScroll); Place(_detailScroll, ClassicBoardLayout.Report.Detail);
        _detail = new Label { Name = "report_detail", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(ClassicBoardLayout.Report.Detail.Size.X, 0),
            VerticalAlignment = VerticalAlignment.Top, MouseFilter = MouseFilterEnum.Pass };
        Style(_detail, false, BodySize, Highlight);
        // Lines advance by whole pixels so the scrolled reason always starts on a line.
        _detail.AddThemeConstantOverride("line_spacing", (int)ClassicBoardLayout.Report.DetailLine - Mathf.CeilToInt(Plugin.Kit.Regular.GetHeight(BodySize)));
        _detailScroll.AddChild(_detail);
        var rail = new ClassicContentScrollRail(_detailScroll) { Name = "report_detail_rail" };
        AddChild(rail); Place(rail, ClassicBoardLayout.Report.DetailRail);
        _voteYes = new Button { Name = "report_vote_yes", Text = "Vote Yes", Disabled = true };
        _voteNo = new Button { Name = "report_vote_no", Text = "Vote No", Disabled = true };
        AddChild(_voteYes); AddChild(_voteNo);
        ArtButton(_voteYes, _art.Find("btn_party")!, ClassicBoardLayout.Report.VoteYes);
        ArtButton(_voteNo, _art.Find("btn_whisper")!, ClassicBoardLayout.Report.VoteNo);
        _voteYes.Pressed += () => Forward("Vote Yes"); _voteNo.Pressed += () => Forward("Vote No");
        var status = Find<Label>("report_status");
        Place(status, ClassicBoardLayout.Report.Status);
        Style(status, true, BodySize, null);
        status.HorizontalAlignment = HorizontalAlignment.Center; status.AutowrapMode = TextServer.AutowrapMode.Off;
        status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; status.MouseFilter = MouseFilterEnum.Pass;
    }

    private void BuildForces()
    {
        var box = ClassicBoardLayout.Forces.Box;
        _boxes.Add(box);
        foreach (float x in ClassicBoardLayout.Forces.Lines) _lines.Add((x, box.Position.Y + ClassicBoardArt.Border, box.End.Y - ClassicBoardArt.Border));
        Find<Label>("forces_title").Visible = false;
        Text(Find<Label>("forces_state"), ClassicBoardLayout.Forces.State, true, BodySize, Muted, HorizontalAlignment.Center);
        Text(Find<Label>("forces_rank_caption"), ClassicBoardLayout.Forces.Caption(0), true, BodySize, Heading, HorizontalAlignment.Left);
        Text(Find<Label>("forces_rank"), ClassicBoardLayout.Forces.Value(0), true, BodySize, Highlight, HorizontalAlignment.Left);
        var points = new Label { Name = "forces_points_caption", Text = "Force Points", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(points); Text(points, ClassicBoardLayout.Forces.Caption(1), true, BodySize, Heading, HorizontalAlignment.Left);
        Text(Find<Label>("forces_points"), ClassicBoardLayout.Forces.Value(1), true, BodySize, Ink, HorizontalAlignment.Left);
        Text(Find<Label>("forces_anger_caption"), ClassicBoardLayout.Forces.Caption(2), true, BodySize, Heading, HorizontalAlignment.Left);
        var bar = Find<Control>("forces_anger");
        if (ClassicBoardGauge.Readable(bar))
        {
            bar.Visible = false;
            _gauge = new ClassicBoardGauge(bar) { Name = "forces_gauge" };
            AddChild(_gauge); Place(_gauge, ClassicBoardLayout.Forces.Gauge);
            _percent = new Label { Name = "forces_percent", MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_percent); Text(_percent, ClassicBoardLayout.Forces.Percent, true, BodySize, Ink, HorizontalAlignment.Right);
        }
        else Place(bar, new Rect2(ClassicBoardLayout.Forces.Value(2).Position + new Vector2(0, 4), new Vector2(150, 16)));
        ArtButton(Find<Button>("forces_join"), _art.Find("btn_add")!, ClassicBoardLayout.Forces.Join);
        ArtButton(Find<Button>("forces_leave"), _art.Find("btn_delete")!, ClassicBoardLayout.Forces.Leave);
    }

    /// <summary>Table borders, column lines, header rule and the column headings.</summary>
    private void Table(Rect2 box, ClassicBoardLayout.Column[] columns, float[] lines, float headRule, float linesEnd, string[] heads)
    {
        _columns = columns;
        foreach (float x in lines) _lines.Add((x, box.Position.Y + ClassicBoardArt.Border, linesEnd));
        _rules.Add((box.Position.X, box.End.X, headRule));
        _headEnd = headRule;
        for (int i = 0; i < heads.Length; i++)
        {
            var head = new Label { Name = "board_head_" + i, Text = heads[i], MouseFilter = MouseFilterEnum.Ignore };
            AddChild(head); Text(head, columns[i].Head, true, HeadSize, Heading, HorizontalAlignment.Center);
            Callable.From(() => head.AddThemeFontSizeOverride("font_size", Fitting(head, HeadSize, 11, head.Size.X))).CallDeferred();
        }
    }

    /// <summary>Reparents the native rows onto the table. Local paging shows whole rows only.</summary>
    private void List(Rect2 rect, int pageRows, float paging, bool localPaging)
    {
        _rowsRect = rect; _pageRows = pageRows; _paged = localPaging;
        var rows = Find<VBoxContainer>("board_rows");
        Find<ScrollContainer>("board_scroll").Visible = false;
        Place(rows, rect);
        rows.ClipContents = true; rows.MouseFilter = MouseFilterEnum.Pass;
        rows.AddThemeConstantOverride("separation", 0);
        Rows = rows;
        if (!localPaging) return;
        _up = new Button { Name = "board_page_up" }; _down = new Button { Name = "board_page_down" };
        AddChild(_up); AddChild(_down);
        Arrow(_up, "btn_page_up", ClassicBoardLayout.PageUp(paging));
        Arrow(_down, "btn_page_down", ClassicBoardLayout.PageDown(paging));
        _up.Pressed += () => Turn(-1); _down.Pressed += () => Turn(1);
        _page = new Label { Name = "board_page", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_page); Text(_page, ClassicBoardLayout.PageText(paging), true, ButtonSize, PageInk, HorizontalAlignment.Center);
    }

    public void Turn(int step)
    {
        int page = Math.Clamp(Page + step, 0, Pages - 1);
        if (page == Page) return;
        Page = page; Select(null);
    }

    /// <summary>Current native rows, excluding rows the client has already released.</summary>
    public PanelContainer[] LiveRows() => Rows == null ? Array.Empty<PanelContainer>()
        : Rows.GetChildren().OfType<PanelContainer>().Where(r => !r.IsQueuedForDeletion()).ToArray();

    private void Select(Control? row)
    {
        Selected = row;
        if (_detail != null)
        {
            _detail.Text = row != null ? row.GetMeta("board_detail", "").AsString() : LiveRows().Length > 0 ? "Select a report to read its reason." : "";
            _detailScroll!.ScrollVertical = 0;
        }
        QueueRedraw();
    }

    /// <summary>Presses the selected row's own native button.</summary>
    private void Forward(string text)
    {
        if (Selected == null || !GodotObject.IsInstanceValid(Selected) || Selected.IsQueuedForDeletion()) return;
        CharacterDetailsSkin.Tree(Selected).OfType<Button>().FirstOrDefault(b => b.Text == text)?.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private Button? RowButton(string text) => Selected == null ? null : CharacterDetailsSkin.Tree(Selected).OfType<Button>().FirstOrDefault(b => b.Text == text);

    private void StyleRows()
    {
        if (Rows == null) return;
        foreach (var child in Rows.GetChildren().OfType<Control>())
        {
            if (child is Label empty && !empty.HasMeta(HotNames.BoardRow))
            {
                // The client's empty-list line is centred in the empty table.
                empty.SetMeta(HotNames.BoardRow, true);
                Style(empty, true, BodySize, Muted);
                empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center;
                empty.CustomMinimumSize = new Vector2(ClassicBoardLayout.RowWidth, _rowsRect.Size.Y);
                empty.AddThemeConstantOverride(HotNames.OutlineSize, 0);
            }
            if (child is not PanelContainer row || row.HasMeta(HotNames.BoardRow) || row.IsQueuedForDeletion()) continue;
            row.SetMeta(HotNames.BoardRow, true);
            row.MouseFilter = MouseFilterEnum.Pass;
            row.AddThemeStyleboxOverride(HotNames.Panel, new StyleBoxEmpty());
            row.CustomMinimumSize = new Vector2(ClassicBoardLayout.RowWidth, ClassicBoardLayout.RowHeight);
            var inner = row.GetChildren().OfType<Control>().FirstOrDefault();
            if (inner == null) continue;
            var labels = CharacterDetailsSkin.Tree(inner).OfType<Label>().ToArray();
            var cells = new Control { Name = "board_cells", MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(cells); inner.Visible = false;
            Label?[] order = Window.Id switch
            {
                // Native order: target, "by poster", reward, Claim.
                "bounty" => new[] { labels.ElementAtOrDefault(0), labels.ElementAtOrDefault(2), labels.ElementAtOrDefault(1) },
                // Native order: name and tally, then the reason line.
                "report" => new[] { labels.ElementAtOrDefault(0), labels.ElementAtOrDefault(1) },
                _ => labels.Take(3).ToArray(),
            };
            if (Window.Id == "report" && labels.Length > 2) row.SetMeta(HotNames.BoardDetail, labels[2].Text);
            if (Window.Id == "bounty" && order[2] is { } poster && poster.Text.StartsWith("by ", StringComparison.Ordinal)) poster.Text = poster.Text[3..];
            string me = LibreKO.Game.Session.Self.LastEnter.Name ?? "";
            bool mine = Window.Id == "fishinghall" && me.Length > 0 && order.ElementAtOrDefault(1)?.Text == me;
            int rank = Window.Id == "fishinghall" && int.TryParse(order[0]?.Text.TrimStart('#'), out int r) ? r : 0;
            for (int i = 0; i < order.Length && i < _columns.Length; i++)
            {
                if (order[i] is not { } cell) continue;
                var column = _columns[i];
                ClassicVendorSkin.Move(cell, cells, new Rect2(column.X, 0, column.Width, ClassicBoardLayout.RowHeight));
                var color = mine ? Highlight : Window.Id switch
                {
                    "bounty" when i == 1 => Heading,
                    "fishinghall" when i == 0 && rank is > 0 and <= 3 => Medal,
                    "report" when i == 1 => Muted,
                    _ => Ink,
                };
                Style(cell, true, BodySize, color);
                cell.HorizontalAlignment = column.Align; cell.VerticalAlignment = VerticalAlignment.Center;
                var inset = new StyleBoxEmpty { ContentMarginLeft = column.Align == HorizontalAlignment.Left ? 3 : 0, ContentMarginRight = column.Align == HorizontalAlignment.Right ? 4 : 0 };
                cell.AddThemeStyleboxOverride(HotNames.Normal, inset);
                cell.AutowrapMode = TextServer.AutowrapMode.Off; cell.ClipText = true;
                cell.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                cell.TooltipText = cell.Text; cell.MouseFilter = MouseFilterEnum.Pass;
                cell.SetMeta(HotNames.BoardCell, i);
            }
            if (Window.Id != "fishinghall")
                row.GuiInput += ev =>
                {
                    if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Select(row);
                };
        }
    }

    private void Arrange()
    {
        if (Rows == null) return;
        var rows = LiveRows();
        if (Selected != null && (!GodotObject.IsInstanceValid(Selected) || Selected.IsQueuedForDeletion() || Selected.GetParent() != Rows)) Select(null);
        if (_paged)
        {
            Pages = Math.Max(1, (rows.Length + _pageRows - 1) / _pageRows);
            if (Page >= Pages) { Page = Pages - 1; Select(null); }
            for (int i = 0; i < rows.Length; i++) rows[i].Visible = i / _pageRows == Page;
            _page!.Text = $"{Page + 1} / {Pages}";
            _up!.Disabled = Page == 0; _down!.Disabled = Page + 1 >= Pages;
        }
        foreach (var gone in Rows.GetChildren().OfType<Control>().Where(c => c.IsQueuedForDeletion())) gone.Visible = false;
        if (_detail != null && Selected == null) _detail.Text = rows.Length > 0 ? "Select a report to read its reason." : "";
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (!_paged || ev is not InputEventMouseButton { Pressed: true } wheel || !_rowsRect.HasPoint(wheel.Position)) return;
        if (wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Turn(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        if (!Window.IsVisibleInTree()) return;
        StyleRows(); Arrange();
        switch (Window.Id)
        {
            case "bounty":
                _claim!.Disabled = Selected == null;
                break;
            case "report":
                // The native gate and status colours follow the sheriff state; the gate keeps the Classic palette.
                bool eligible = !Find<Button>("report_file").Disabled;
                ThemeNames.SetColor(Find<Label>("report_gate"), HotNames.FontColor, eligible ? Highlight : Muted);
                _voteYes!.Disabled = RowButton("Vote Yes") is not { Disabled: false };
                _voteNo!.Disabled = RowButton("Vote No") is not { Disabled: false };
                var status = Find<Label>("report_status"); status.TooltipText = status.Text;
                if (_detailScroll != null && !Godot.Input.IsMouseButtonPressed(MouseButton.Left))
                {
                    float line = ClassicBoardLayout.Report.DetailLine;
                    // Pad the last line so the scroll range is a whole number of lines.
                    float height = Math.Max(1, _detail!.GetLineCount()) * line;
                    if (_detail.CustomMinimumSize.Y != height)
                    {
                        _detail.CustomMinimumSize = new Vector2(ClassicBoardLayout.Report.Detail.Size.X, height);
                        // The scroll range follows the container's measured minimum size; measure it now.
                        _detailScroll.UpdateMinimumSize(); _detailScroll.GetCombinedMinimumSize(); _detailScroll.QueueSort();
                    }
                    int snapped = (int)(Mathf.Round(_detailScroll.ScrollVertical / line) * line);
                    if (snapped != _detailScroll.ScrollVertical) _detailScroll.ScrollVertical = snapped;
                }
                break;
            case "forces":
                bool joined = Find<Button>("forces_join").Disabled;
                ThemeNames.SetColor(Find<Label>("forces_state"), HotNames.FontColor, joined ? Highlight : Muted);
                var points = Find<Label>("forces_points");
                const string prefix = "Force Points: ";
                if (points.Text.StartsWith(prefix, StringComparison.Ordinal)) points.Text = points.Text[prefix.Length..];
                if (_gauge != null) { _gauge.QueueRedraw(); _percent!.Text = Mathf.RoundToInt(_gauge.Fraction * 100) + "%"; }
                break;
        }
        QueueRedraw();
    }

    /// <summary>Moves a native control onto the artwork; its own visibility stays native-owned.</summary>
    private void Place(Control control, Rect2 rect)
    {
        bool visible = control.Visible;
        ClassicVendorSkin.Move(control, this, rect);
        control.Visible = visible;
        control.SetMeta("board_expected_rect", rect);
    }

    private static void Style(Control control, bool bold, int size, Color? color)
    {
        control.AddThemeFontOverride("font", bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        control.AddThemeFontSizeOverride("font_size", size);
        control.AddThemeConstantOverride("outline_size", 0);
        if (color is { } ink) control.AddThemeColorOverride("font_color", ink);
        control.AddThemeColorOverride("font_shadow_color", Colors.Black);
        control.AddThemeConstantOverride("shadow_offset_x", 1); control.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    private void Text(Label label, Rect2 rect, bool bold, int size, Color color, HorizontalAlignment align)
    {
        Place(label, rect);
        Style(label, bold, size, color);
        label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        label.HorizontalAlignment = align; label.VerticalAlignment = VerticalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.Off; label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }

    private static void Shadow(Control control)
    {
        control.AddThemeColorOverride("font_shadow_color", Colors.Black);
        control.AddThemeConstantOverride("shadow_offset_x", 1); control.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    private void Input(LineEdit input, Rect2 rect)
    {
        Place(input, rect);
        ClassicVendorSkin.Input(input);
        input.AddThemeFontOverride("font", Plugin.Kit.Bold); input.AddThemeFontSizeOverride("font_size", BodySize);
        var box = ClassicDesign.InputBox(); box.SetContentMarginAll(1); box.ContentMarginLeft = box.ContentMarginRight = 4;
        var locked = (StyleBoxFlat)box.Duplicate(); locked.BorderColor = new Color(Karus ? "4a4844" : "5a4a2c");
        input.AddThemeStyleboxOverride("normal", box); input.AddThemeStyleboxOverride("focus", box);
        input.AddThemeStyleboxOverride("read_only", locked);
        input.AddThemeColorOverride("font_uneditable_color", Muted);
        input.FocusMode = FocusModeEnum.All; input.TextDirection = TextDirection.Ltr;
    }

    /// <summary>Party board button: original state images in the board's sunken slot.</summary>
    private void ArtButton(Button button, LayoutNode source, Rect2 rect)
    {
        Place(button, rect);
        _slots.Add(rect);
        ClassicChatControls.SkinButton(button, source);
        if (button.GetThemeStylebox("pressed") is { } pressed) button.AddThemeStyleboxOverride("hover_pressed", pressed);
        if (button.GetThemeStylebox("normal") is StyleBoxTexture normal)
        {
            var disabled = (StyleBoxTexture)normal.Duplicate();
            disabled.ModulateColor = Karus ? new Color(.72f, .72f, .72f) : new Color(.6f, .6f, .6f);
            button.AddThemeStyleboxOverride("disabled", disabled);
        }
        button.Icon = null; button.FocusMode = FocusModeEnum.None; button.Alignment = HorizontalAlignment.Center;
        button.ClipText = true; button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        button.AddThemeFontOverride("font", Plugin.Kit.Bold);
        button.AddThemeFontSizeOverride("font_size", Fitting(button, ButtonSize, 11, rect.Size.X - 6));
        button.AddThemeConstantOverride("outline_size", 0);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, ButtonInk);
        // Karus keeps its dark caption on the dimmed stone, as the party board does.
        button.AddThemeColorOverride("font_disabled_color", Karus ? ButtonInk : new Color("8c8270"));
        button.AddThemeColorOverride("font_shadow_color", Karus ? Colors.Transparent : Colors.Black);
        button.AddThemeConstantOverride("shadow_offset_x", 1); button.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    /// <summary>Friend-list page arrow; the disabled arrow is dimmed at the list ends.</summary>
    private void Arrow(Button button, string id, Rect2 rect)
    {
        Place(button, rect);
        ClassicChatControls.SkinButton(button, _art.Find(id)!);
        if (button.GetThemeStylebox("pressed") is { } pressed) button.AddThemeStyleboxOverride("hover_pressed", pressed);
        if (button.GetThemeStylebox("normal") is StyleBoxTexture normal)
        {
            var disabled = (StyleBoxTexture)normal.Duplicate();
            disabled.ModulateColor = new Color(1, 1, 1, .45f);
            button.AddThemeStyleboxOverride("disabled", disabled);
        }
        button.Text = ""; button.Icon = null; button.FocusMode = FocusModeEnum.None;
        button.TooltipText = id == "btn_page_up" ? "Previous page" : "Next page";
    }

    /// <summary>Largest size from <paramref name="size"/> down to <paramref name="minimum"/> whose caption fits.</summary>
    public static int Fitting(Control control, int size, int minimum, float width)
    {
        string text = control is Label label ? label.Text : control is Button button ? button.Text : "";
        var font = control.GetThemeFont("font");
        float room = font == Plugin.Kit.Bold ? 3 : 0;
        while (size > minimum && font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + room > width) size--;
        return size;
    }

    public override void _Draw()
    {
        ClassicBoardArt.Window(this, new Rect2(Vector2.Zero, Size));
        foreach (var box in _boxes) ClassicBoardArt.Box(this, box);
        // An empty list keeps the column lines in its heading only, so the empty line reads across the table.
        bool empty = Rows != null && LiveRows().Length == 0;
        foreach (var (x, top, bottom) in _lines) ClassicBoardArt.Column(this, x, top, empty ? _headEnd : bottom);
        foreach (var (left, right, y) in _rules) ClassicBoardArt.Rule(this, left, right, y);
        foreach (var slot in _slots) ClassicBoardArt.Slot(this, slot);
        if (Selected != null && GodotObject.IsInstanceValid(Selected) && Selected.IsVisibleInTree() && Rows != null)
            DrawRect(new Rect2(Rows.Position + Selected.Position, new Vector2(ClassicBoardLayout.RowWidth, ClassicBoardLayout.RowHeight)), Selection, false, 1);
    }
}

/// <summary>Party board artwork pieces: frame, table border, column line, rule and button slot.</summary>
public static class ClassicBoardArt
{
    private static bool Karus => Plugin.Kit.Nation == 1;
    private const float Corner = 8;
    public static float Border => Karus ? 4 : 3;
    private static Texture2D? Sheet => Plugin.Kit.Texture(Plugin.Kit.Layout(ClassicBoardLayout.Art).Images.First().Texture!);
    private static Rect2 ColumnSource => Karus ? new Rect2(190, 100, 4, 32) : new Rect2(128, 100, 3, 32);
    private static Rect2 RuleSource => Karus ? new Rect2(20, 74, 32, 4) : new Rect2(20, 75, 32, 3);
    private static Rect2 SlotSource => Karus ? new Rect2(70, 422, 76, 22) : new Rect2(70, 422, 76, 23);

    /// <summary>The board frame over an opaque underlay, as the warfare and quest frames are drawn.</summary>
    public static void Window(CanvasItem canvas, Rect2 rect)
    {
        canvas.DrawRect(rect.Grow(-2), Colors.Black);
        Box(canvas, rect);
    }

    /// <summary>Frame corners and tiled edges, never stretched; the inner table border is the same art.</summary>
    public static void Box(CanvasItem canvas, Rect2 rect)
    {
        if (Sheet is not { } sheet) return;
        float c = Corner, w = rect.Size.X, h = rect.Size.Y;
        var o = rect.Position;
        Tile(canvas, sheet, new Rect2(o + new Vector2(c, 0), new Vector2(w - 2 * c, c)), new Rect2(20, 0, 40, c));
        Tile(canvas, sheet, new Rect2(o + new Vector2(c, h - c), new Vector2(w - 2 * c, c)), new Rect2(20, 481 - c, 40, c));
        Tile(canvas, sheet, new Rect2(o + new Vector2(0, c), new Vector2(c, h - 2 * c)), new Rect2(0, 100, c, 40));
        Tile(canvas, sheet, new Rect2(o + new Vector2(w - c, c), new Vector2(c, h - 2 * c)), new Rect2(320 - c, 100, c, 40));
        canvas.DrawTextureRectRegion(sheet, new Rect2(o, new Vector2(c, c)), new Rect2(0, 0, c, c));
        canvas.DrawTextureRectRegion(sheet, new Rect2(o + new Vector2(w - c, 0), new Vector2(c, c)), new Rect2(320 - c, 0, c, c));
        canvas.DrawTextureRectRegion(sheet, new Rect2(o + new Vector2(0, h - c), new Vector2(c, c)), new Rect2(0, 481 - c, c, c));
        canvas.DrawTextureRectRegion(sheet, new Rect2(o + new Vector2(w - c, h - c), new Vector2(c, c)), new Rect2(320 - c, 481 - c, c, c));
    }

    public static void Column(CanvasItem canvas, float x, float top, float bottom)
    {
        if (Sheet is { } sheet) Tile(canvas, sheet, new Rect2(x, top, ColumnSource.Size.X, bottom - top), ColumnSource);
    }

    /// <summary>A header rule across a table, joining both side borders.</summary>
    public static void Rule(CanvasItem canvas, float left, float right, float y)
    {
        float inset = Border;
        if (Sheet is { } sheet) Tile(canvas, sheet, new Rect2(left + inset, y, right - left - 2 * inset, RuleSource.Size.Y), RuleSource);
    }

    /// <summary>The sunken slot the board's 70x18 buttons sit in.</summary>
    public static void Slot(CanvasItem canvas, Rect2 button)
    {
        if (Sheet is not { } sheet) return;
        var source = SlotSource;
        var rect = new Rect2(button.Position - new Vector2(3, 2), button.Size + new Vector2(6, source.Size.Y - 18));
        const float m = 5;
        float w = rect.Size.X, h = rect.Size.Y;
        var o = rect.Position;
        void Part(Rect2 dst, Rect2 src) { if (dst.Size.X > 0 && dst.Size.Y > 0) canvas.DrawTextureRectRegion(sheet, dst, src); }
        Part(new Rect2(o, new Vector2(m, h)), new Rect2(source.Position, new Vector2(m, source.Size.Y)));
        Part(new Rect2(o + new Vector2(w - m, 0), new Vector2(m, h)), new Rect2(source.End.X - m, source.Position.Y, m, source.Size.Y));
        Part(new Rect2(o + new Vector2(m, 0), new Vector2(w - 2 * m, h)), new Rect2(source.Position.X + m, source.Position.Y, source.Size.X - 2 * m, source.Size.Y));
    }

    private static void Tile(CanvasItem canvas, Texture2D sheet, Rect2 destination, Rect2 source)
    {
        for (float y = 0; y < destination.Size.Y; y += source.Size.Y)
            for (float x = 0; x < destination.Size.X; x += source.Size.X)
            {
                var size = new Vector2(Math.Min(source.Size.X, destination.Size.X - x), Math.Min(source.Size.Y, destination.Size.Y - y));
                canvas.DrawTextureRectRegion(sheet, new Rect2(destination.Position + new Vector2(x, y), size), new Rect2(source.Position, size));
            }
    }
}

/// <summary>The nation status-bar HP gauge, filled from the native force anger bar.</summary>
public partial class ClassicBoardGauge : Control
{
    private readonly Control _source;
    private readonly LayoutNode _art;
    public ClassicBoardGauge(Control source)
    {
        _source = source; MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest;
        _art = Plugin.Kit.Layout(ClassicBoardLayout.Gauge).Find("Progress_HP")!;
    }
    public static bool Readable(Control bar) => KnightOnlineUiClassic.Native.Native.Has(bar, "frac");
    public float Fraction => Mathf.Clamp(KnightOnlineUiClassic.Native.Native.Get<float>(_source, "frac"), 0, 1);
    public override void _Draw()
    {
        foreach (var part in _art.Images.OrderBy(a => a.Tag))
        {
            var src = new Rect2(part.SrcX, part.SrcY, part.SrcW, part.SrcH);
            var dst = new Rect2(Vector2.Zero, Size);
            if (part.Tag == 1) { dst.Size *= new Vector2(Fraction, 1); src.Size *= new Vector2(Fraction, 1); }
            if (dst.Size.X >= 1) DrawTextureRectRegion(Plugin.Kit.Texture(part.Texture!), dst, src);
        }
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName BoardCell = "board_cell";
    internal static readonly StringName BoardDetail = "board_detail";
    internal static readonly StringName BoardRow = "board_row";
    internal static readonly StringName FontColor = "font_color";
    internal static readonly StringName Normal = "normal";
    internal static readonly StringName OutlineSize = "outline_size";
    internal static readonly StringName Panel = "panel";
}
