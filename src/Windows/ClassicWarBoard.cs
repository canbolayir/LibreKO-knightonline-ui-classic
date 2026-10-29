using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;
using Spec = KnightOnlineUiClassic.Layout.ClassicPartyHudLayout.WarBoard;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The Classic Party War Board in place of the client's board (<see cref="HudPart.WarBoard"/>), driven by the
/// plugin party API: the plain Classic frame, the Damage and Healing tabs, the background transparency the client
/// saves, and one row per ranked member with the client's class colour behind the share. The client keeps the
/// requests, their cooldown and the ranking; the board shows only while the client has it open. The client's tab
/// cooldown (<see cref="IGameParty.WarBoardCooldown"/>, raised through <see cref="IGameParty.Changed"/> whenever its
/// seconds change) shows beside the tabs. Tooltips use the original tooltip look (co_tooltip_us).
/// </summary>
public partial class ClassicWarBoard : Control
{
    private readonly IGameParty _party = null!;
    private readonly ClassicFrame _frame = null!;
    private readonly Control _grip = null!;
    private readonly HSlider _alpha = null!;
    private readonly Button _damage = null!, _healing = null!;
    private readonly ClassicWarBoardRow[] _rows = new ClassicWarBoardRow[PartyWarBoard.MaxRows];
    private readonly Label _empty = null!, _cooldown = null!;
    private bool _queued, _syncing;

    public ClassicWarBoard() { }

    public ClassicWarBoard(IGameParty party)
    {
        _party = party;
        Name = "classic_war_board"; MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Nearest; Visible = false;
        Theme = ClassicPremiumChip.Tooltip();
        _frame = new ClassicFrame { Name = "war_board_frame", BackgroundColor = Colors.Black };
        AddChild(_frame);
        _grip = new Control { Name = "war_board_drag", MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Move };
        Place(_grip, Spec.Drag);
        var title = new Label { Name = "war_board_title", Text = "Party War Board", VerticalAlignment = VerticalAlignment.Center, ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
        ClassicSocialStyle.Font(title, true, ClassicSocialLayout.HeadingSize);
        title.AddThemeColorOverride("font_color", ClassicDesign.FrameTitle);
        Place(title, Spec.Title);
        _alpha = new HSlider { Name = "war_board_alpha", MinValue = PartyWarBoard.MinAlpha, MaxValue = 1, Step = .05, FocusMode = FocusModeEnum.None, TooltipText = "Background transparency" };
        var track = new StyleBoxFlat { BgColor = new Color("211c15"), BorderColor = new Color("82735b"), ContentMarginTop = 2, ContentMarginBottom = 2 };
        track.SetBorderWidthAll(1);
        _alpha.AddThemeStyleboxOverride("slider", track);
        _alpha.AddThemeColorOverride("grabber_area_color", new Color("c4ad82"));
        _alpha.AddThemeColorOverride("grabber_area_highlight_color", new Color("e7c773"));
        _alpha.ValueChanged += value => { if (!_syncing) _party.SetWarBoardAlpha((float)value); ApplyAlpha((float)value); };
        Place(_alpha, Spec.Alpha);
        var close = new Button { Name = "war_board_close", TooltipText = "Close", FocusMode = FocusModeEnum.None };
        ClassicVendorSkin.Button(close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!);
        close.Text = "";
        close.Pressed += () => _party.ToggleWarBoard();
        AddChild(close);
        close.SetMeta("war_board_close", true);
        _damage = Tab("war_board_damage", "Damage", "Damage dealt", PartyBoardMeterMode.Damage, 0);
        _healing = Tab("war_board_healing", "Healing", "Healing", PartyBoardMeterMode.Healing, 1);
        for (int i = 0; i < _rows.Length; i++)
        {
            _rows[i] = new ClassicWarBoardRow { Name = "war_board_row_" + i, Visible = false };
            Place(_rows[i], Spec.Row(i));
        }
        _empty = new Label { Name = "war_board_empty", Text = "No party share yet.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        ClassicSocialStyle.Font(_empty, false, ClassicSocialLayout.FontSize);
        _empty.AddThemeColorOverride("font_color", ClassicDesign.Muted);
        Place(_empty, Spec.Row(0));
        _cooldown = new Label { Name = "war_board_cooldown", Visible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Pass, TooltipText = "The board changes when the cooldown ends" };
        ClassicSocialStyle.Font(_cooldown, true, Spec.CooldownSize);
        _cooldown.AddThemeColorOverride("font_color", Spec.CooldownInk);
        _cooldown.AddThemeColorOverride("font_outline_color", Colors.Black);
        _cooldown.AddThemeConstantOverride("outline_size", 3);
        Place(_cooldown, Spec.Cooldown);
        Resized += Layout;
    }

    private Button Tab(string name, string text, string tip, PartyBoardMeterMode mode, int index)
    {
        var tab = ClassicChatControls.Tab(text);
        tab.Name = name; tab.ToggleMode = true; tab.TooltipText = tip;
        tab.Pressed += () => { _party.SetWarBoardMode(mode); QueueRefresh(); };
        Place(tab, Spec.Tab(index));
        return tab;
    }

    private void Place(Control control, Rect2 rect)
    {
        if (control.GetParent() == null) AddChild(control);
        control.Position = rect.Position; control.Size = rect.Size;
        control.SetMeta("war_board_expected_rect", rect);
        // Theme minimums settle in the tree; keep the declared bounds.
        control.Ready += () => { control.Position = rect.Position; control.Size = rect.Size; };
    }

    private void Layout()
    {
        _frame.Position = Vector2.Zero; _frame.Size = Size;
        foreach (var close in GetChildren().OfType<Button>().Where(b => b.HasMeta("war_board_close")))
        {
            var rect = Spec.Close(Size.X);
            close.Position = rect.Position; close.Size = rect.Size; close.SetMeta("war_board_expected_rect", rect);
        }
    }

    private void ApplyAlpha(float alpha)
    {
        _frame.BackgroundAlpha = alpha;
        _frame.QueueRedraw();
    }

    public override void _Ready()
    {
        _party.Changed += QueueRefresh;
        HudLayout.Attach(this, Spec.LayoutId, _grip, () => Spec.DefaultPosition(GetViewportRect().Size));
        Refresh();
    }

    public override void _ExitTree() => _party.Changed -= QueueRefresh;

    private void QueueRefresh()
    {
        if (_queued) return;
        _queued = true;
        Callable.From(Refresh).CallDeferred();
    }

    public void Refresh()
    {
        _queued = false;
        if (!IsInsideTree()) return;
        Visible = _party.InParty && _party.WarBoardOpen;
        if (!Visible) return;
        var mode = _party.WarBoardMode;
        _damage.SetPressedNoSignal(mode == PartyBoardMeterMode.Damage);
        _healing.SetPressedNoSignal(mode == PartyBoardMeterMode.Healing);
        _syncing = true; _alpha.SetValueNoSignal(_party.WarBoardAlpha); _syncing = false;
        ApplyAlpha(_party.WarBoardAlpha);
        var rows = _party.WarBoardRows;
        int shown = Math.Min(rows.Count, _rows.Length);
        for (int i = 0; i < _rows.Length; i++)
        {
            _rows[i].Visible = i < shown;
            if (i < shown) _rows[i].Bind(rows[i]);
        }
        _empty.Visible = shown == 0;
        // The client counts the tab cooldown down while it holds a changed tab and notifies every new second.
        int cool = _party.WarBoardCooldown;
        _cooldown.Visible = cool > 0;
        _cooldown.Text = cool > 0 ? cool.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        var size = new Vector2(Spec.Width, Spec.Height(shown));
        if (Size != size) { CustomMinimumSize = size; Size = size; }
        Layout();
    }
}

/// <summary>One war board row: the share bar in the member's class colour, "rank. name" and the percent over it.</summary>
public partial class ClassicWarBoardRow : Control
{
    private GameWarBoardRow _row;

    public ClassicWarBoardRow() { MouseFilter = MouseFilterEnum.Pass; }

    public GameWarBoardRow Row => _row;

    public void Bind(GameWarBoardRow row)
    {
        _row = row;
        TooltipText = $"{row.Name}\n{CharacterClassCatalog.DisplayName(row.Class)}\n{PartyWarBoard.Percent(row.Share)}";
        QueueRedraw();
    }

    public override void _Draw()
    {
        var bounds = new Rect2(Vector2.Zero, Size);
        DrawRect(bounds, new Color(0, 0, 0, .9f));
        var fill = Spec.ClassFills.GetValueOrDefault(CharacterClassCatalog.Family(_row.Class), ClassicPartyHudLayout.MeterDamage);
        float share = PartyWarBoard.Fill(_row.Share);
        if (share > 0) DrawRect(new Rect2(0, 0, Size.X * share, Size.Y), fill);
        DrawRect(bounds, ClassicSocialStyle.RowRule, false, 1);
        var font = Plugin.Kit.Bold; const int size = 11;
        float baseline = (Size.Y - font.GetHeight(size)) / 2 + font.GetAscent(size);
        string percent = PartyWarBoard.Percent(_row.Share);
        float percentWidth = font.GetStringSize(percent, HorizontalAlignment.Left, -1, size).X;
        string name = PartyWarBoard.RowName(_row.Rank, _row.Name);
        float room = Size.X - percentWidth - 16;
        // Long names end in an ellipsis before the percent.
        if (font.GetStringSize(name, HorizontalAlignment.Left, -1, size).X > room)
        {
            while (name.Length > 1 && font.GetStringSize(name + "…", HorizontalAlignment.Left, -1, size).X > room) name = name[..^1];
            name += "…";
        }
        void Text(Vector2 at, string text, float width)
        {
            DrawString(font, at + Vector2.One, text, HorizontalAlignment.Left, width, size, Colors.Black, TextServer.JustificationFlag.None);
            DrawString(font, at, text, HorizontalAlignment.Left, width, size, Colors.White, TextServer.JustificationFlag.None);
        }
        Text(new Vector2(4, baseline), name, room);
        Text(new Vector2(Size.X - 4 - percentWidth, baseline), percent, percentWidth + 1);
    }
}
