using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The Classic party list in place of the client's party HUD: the original member cells driven by the
/// plugin party API in the client's row order, left click picks (and targets) a member, right click opens the
/// client's member menu (with User Vote in Juraid), the small arrows move a member up or down, and the party
/// actions (invite by name, leave or disband, party board, war board, row meter, automatic re-invite, the
/// commander bar's restore) call the client's own handlers. Since ff78eef each cell also shows the member's
/// buffs and connection, and its tooltip the level progress and ping; the selection follows the client's
/// highlight (the picked member, or the party member who is the world target). Tooltips use the original
/// tooltip look (co_tooltip_us), long ones wrapped as the client wraps its buff tooltips.
/// </summary>
public partial class ClassicPartyHud : Control
{
    private readonly IGameParty _party = null!;
    private readonly Func<WorldRoot?> _world = null!;
    private readonly ClassicPartyHudRow[] _rows = new ClassicPartyHudRow[8];
    private readonly Control _actions = new() { Name = "party_actions", MouseFilter = MouseFilterEnum.Ignore };
    private readonly Button _leave = null!, _meter = null!, _rejoin = null!, _commander = null!;
    private readonly Control _grip = null!;
    private bool _queued;

    public ClassicPartyHud() { }

    public ClassicPartyHud(IGameParty party, Func<WorldRoot?> world)
    {
        _party = party; _world = world;
        Name = "classic_party_hud"; MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest;
        Theme = ClassicPremiumChip.Tooltip();
        var heading = Plugin.Kit.Layout(ClassicPartyHudLayout.Art).Strings.First();
        ClassicPartySkin.Caption(this, "Party", new Rect2(heading.Position, heading.SizeVec), heading.Color, heading.Size).Name = "party_caption";
        _grip = new Control { Name = "party_drag", Position = ClassicPartyHudLayout.Drag.Position, Size = ClassicPartyHudLayout.Drag.Size, MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Move };
        AddChild(_grip);
        for (int i = 0; i < _rows.Length; i++)
        {
            var row = new ClassicPartyHudRow(this) { Name = "party_member_" + i, Visible = false };
            var rect = ClassicPartyHudLayout.Row(i); row.Position = rect.Position; row.Size = row.CustomMinimumSize = rect.Size;
            _rows[i] = row; AddChild(row);
        }
        AddChild(_actions);
        Action("Invite", 0, () => NativeParty.InviteByName(_world())).TooltipText = "Invite a player by name";
        _leave = Action("Leave", 1, () => _party.Leave());
        Action("Seek Party", 2, () => NativeParty.ToggleBoard(_world())).TooltipText = "Party board";
        Action("War Board", 3, () => _party.ToggleWarBoard()).TooltipText = "Party War Board";
        _meter = Action("Meter", -1, () => { _party.CycleRowMeter(); QueueRefresh(); });
        _meter.TooltipText = "Party meter under each member: off, damage dealt or healing";
        _rejoin = Action("Re-invite", -2, () => { _party.ToggleAutoRejoin(); QueueRefresh(); });
        _commander = Action("Commander bar", -3, () => { _party.ShowCommanderBar(); QueueRefresh(); });
        _commander.TooltipText = "Show the commander bar again";
    }

    private Button Action(string text, int index, Action pressed)
    {
        var button = new Button { Text = text, Name = "party_" + text.ToLowerInvariant().Replace(' ', '_'), FocusMode = FocusModeEnum.None, ClipText = true };
        ClassicChatControls.SkinButton(button, Plugin.Kit.Layout(ClassicPartyHudLayout.ButtonArt).Find("btn_party")!);
        button.AddThemeFontOverride("font", Plugin.Kit.Bold); button.AddThemeFontSizeOverride("font_size", 10);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" }) button.AddThemeColorOverride(state, Plugin.Kit.Nation == 1 ? Colors.Black : new Color("ffff80"));
        ClassicPartySkin.KarusButtonContrast(button);
        var rect = index switch
        {
            -1 => ClassicPartyHudLayout.MeterAction,
            -2 => ClassicPartyHudLayout.RejoinAction,
            -3 => ClassicPartyHudLayout.CommanderAction,
            _ => ClassicPartyHudLayout.Action(index),
        };
        _actions.AddChild(button); button.Position = rect.Position; button.Size = rect.Size;
        button.SetMeta("party_expected_rect", rect);
        button.Pressed += pressed;
        return button;
    }

    public IGameParty Party => _party;
    public WorldRoot? World => _world();

    public override void _Ready()
    {
        _party.Changed += QueueRefresh;
        HudLayout.Attach(this, ClassicPartyHudLayout.LayoutId, _grip, () => ClassicPartyHudLayout.DefaultPosition(GetViewportRect().Size));
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
        var members = _party.InParty ? _party.Members : Array.Empty<GamePartyMember>();
        Visible = members.Count > 0;
        int shown = Math.Min(members.Count, _rows.Length);
        for (int i = 0; i < _rows.Length; i++)
        {
            _rows[i].Visible = i < shown;
            if (i < shown) _rows[i].Bind(members[i], i, shown);
        }
        _actions.Position = new Vector2(0, ClassicPartyHudLayout.RowsTop + shown * ClassicPartyHudLayout.RowPitch);
        _actions.Size = new Vector2(ClassicPartyHudLayout.Width, ClassicPartyHudLayout.ActionsHeight);
        // Theme sizes settle in the tree; keep every action at its art size.
        foreach (var button in _actions.GetChildren().OfType<Button>())
        {
            var rect = button.GetMeta(HotNames.PartyExpectedRect).AsRect2();
            if (button.GetRect() != rect) { button.Position = rect.Position; button.Size = rect.Size; }
        }
        _leave.Text = _party.IsLeader ? "Disband" : "Leave";
        _leave.TooltipText = _party.IsLeader ? "Disband the party" : "Leave the party";
        _meter.Text = _party.RowMeterMode switch { PartyBoardMeterMode.Damage => "Meter: Damage", PartyBoardMeterMode.Healing => "Meter: Healing", _ => "Meter: Off" };
        _rejoin.Text = _party.AutoRejoin ? "Re-invite: On" : "Re-invite: Off";
        _rejoin.TooltipText = WrapTip(_party.AutoRejoinTip);
        bool commander = _party.CommanderBarHidden;
        _commander.Visible = commander;
        CustomMinimumSize = Size = new Vector2(ClassicPartyHudLayout.Width, ClassicPartyHudLayout.Height(shown, commander));
    }

    /// <summary>Long tips (the official re-invite text) in lines of the client's buff tooltip width.</summary>
    public static string WrapTip(string text) => LibreKO.Domain.TextWrap.Wrap(text, ClassicPartyHudLayout.TipLetters);
}

/// <summary>
/// One original party member cell: HP bar with the status look, MP bar, name, meter and selection; the member's
/// buffs on the name line and the connection glyph beside it.
/// </summary>
public partial class ClassicPartyHudRow : Control
{
    private readonly ClassicPartyHud _hud = null!;
    private readonly LayoutNode _layout = null!;
    private readonly Label _name = null!;
    private readonly Button _up = null!, _down = null!;
    private readonly ClassicPartyBuff[] _buffs = new ClassicPartyBuff[ClassicPartyHudLayout.BuffMax];
    private readonly ClassicPartySignal _signal = null!;
    private GamePartyMember _member;
    private bool _bound;
    private float _share;

    public ClassicPartyHudRow() { }

    public ClassicPartyHudRow(ClassicPartyHud hud)
    {
        _hud = hud; _layout = Plugin.Kit.Layout(ClassicPartyHudLayout.Art);
        MouseFilter = MouseFilterEnum.Stop;
        // The name gives way to the buff icons, so its width is set on every bind (no deferred reset).
        var name = ClassicPartyHudLayout.Name;
        _name = new Label { Name = "party_member_name", Position = name.Position, Size = name.Size, ClipText = true, ClipContents = true, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _name.AddThemeFontOverride("font", Plugin.Kit.ChatStrong);
        _name.AddThemeFontSizeOverride("font_size", 10);
        _name.AddThemeColorOverride("font_color", new Color("8080ff"));
        AddChild(_name);
        for (int i = 0; i < _buffs.Length; i++)
        {
            _buffs[i] = new ClassicPartyBuff { Name = "party_buff_" + i, Visible = false };
            AddChild(_buffs[i]);
        }
        _signal = new ClassicPartySignal { Name = "party_signal", Position = ClassicPartyHudLayout.Signal.Position, Size = ClassicPartyHudLayout.Signal.Size };
        AddChild(_signal);
        _up = Arrow("party_move_up", ClassicPartyHudLayout.ArrowUp, ClassicPartyHudLayout.MoveUp, "Move up", LibreKO.Domain.PartyOrder.Up);
        _down = Arrow("party_move_down", ClassicPartyHudLayout.ArrowDown, ClassicPartyHudLayout.MoveDown, "Move down", LibreKO.Domain.PartyOrder.Down);
        GuiInput += OnInput;
    }

    private Button Arrow(string name, string art, Rect2 rect, string tip, int step)
    {
        var button = new Button { Name = name, TooltipText = tip, FocusMode = FocusModeEnum.None, Position = rect.Position, Size = rect.Size, Visible = false };
        ClassicChatControls.SkinButton(button, Plugin.Kit.Layout(ClassicPartyHudLayout.ArrowArt).Find(art)!);
        // The art keeps its proportions at the small size: no nine-patch border.
        foreach (var state in new[] { "normal", "pressed", "hover", "disabled" })
            if (button.GetThemeStylebox(state) is StyleBoxTexture box) box.SetTextureMarginAll(0);
        if (button.GetThemeStylebox("normal") is StyleBoxTexture normal)
        {
            var disabled = (StyleBoxTexture)normal.Duplicate(); disabled.ModulateColor = new Color(.45f, .45f, .45f);
            button.AddThemeStyleboxOverride("disabled", disabled);
        }
        button.SetMeta("party_expected_rect", rect);
        button.Pressed += () => { if (_bound) _hud.Party.MoveRow(_member.Id, step); };
        AddChild(button);
        Callable.From(() => { if (IsInstanceValid(button)) { button.Position = rect.Position; button.Size = rect.Size; } }).CallDeferred();
        return button;
    }

    public GamePartyMember Member => _member;
    public int BuffsShown => _buffs.Count(b => b.Visible);

    public void Bind(GamePartyMember member, int index, int count)
    {
        _member = member; _bound = true;
        // The client offers the arrows while the party has more than one member; the ends cannot move further.
        _up.Visible = _down.Visible = count > 1;
        _up.Disabled = index == 0; _down.Disabled = index == count - 1;
        _name.Text = member.Name;
        // Original party colours: the leader in the caption yellow, the commander green, members blue.
        _name.AddThemeColorOverride("font_color", member.Leader ? new Color("ffff80") : NativeParty.IsCommander(member.Id) ? new Color("00ff00") : new Color("8080ff"));
        int ping = _hud.Party.PingMs(member.Id);
        TooltipText = Tip(member.Tooltip, _hud.Party.LevelPercent(member.Id), ping);
        _signal.Ms = ping;
        BindBuffs(_hud.Party.MemberBuffs(member.Id));
        _share = LibreKO.Domain.PartyWarBoard.Fill(Math.Max(0, _hud.Party.RowMeterShare(member.Id)));
        QueueRedraw();
    }

    /// <summary>The client's member tooltip, then the level progress and the client's ping text.</summary>
    public static string Tip(string client, double levelPercent, int ping) =>
        client + "\nLevel progress " + levelPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%\n" + LibreKO.Domain.PartySignal.Tooltip(ping);

    /// <summary>
    /// The buffs right of the name, ending under the bars: as many as fit after the name, at least two (the
    /// name then ends in an ellipsis); like the client's one line, the rest is not shown.
    /// </summary>
    private void BindBuffs(IReadOnlyList<GamePartyBuff> buffs)
    {
        var name = ClassicPartyHudLayout.Name;
        float natural = _name.GetThemeFont("font").GetStringSize(_member.Name, HorizontalAlignment.Left, -1, _name.GetThemeFontSize("font_size")).X;
        int shown = Math.Min(buffs.Count, ClassicPartyHudLayout.BuffsShown(natural));
        _name.Position = name.Position; _name.Size = new Vector2(ClassicPartyHudLayout.NameWidth(shown), name.Size.Y);
        for (int i = 0; i < _buffs.Length; i++)
        {
            _buffs[i].Visible = i < shown;
            if (i >= shown) continue;
            var rect = ClassicPartyHudLayout.Buff(i, shown);
            _buffs[i].Position = rect.Position; _buffs[i].Size = rect.Size;
            _buffs[i].Bind(buffs[i]);
        }
    }

    private void OnInput(InputEvent ev)
    {
        if (!_bound || ev is not InputEventMouseButton { Pressed: true } click) return;
        if (click.ButtonIndex == MouseButton.Left) { _hud.Party.Pick(_member.Id); AcceptEvent(); }
        else if (click.ButtonIndex == MouseButton.Right)
        {
            NativeParty.PrepareMemberMenu(_hud.World, _member.Name);
            _hud.Party.ShowMemberMenu(_member.Id);
            AcceptEvent();
        }
    }

    /// <summary>The client's HP look (normal, lasting, drop, slow, undead) on the matching original bar art.</summary>
    private string HpArt() => _member.HpLook switch
    {
        LibreKO.Domain.PartyHpLook.Lasting => "progress_hp_0_lasting",
        LibreKO.Domain.PartyHpLook.Drop => "progress_hp_0_drop",
        LibreKO.Domain.PartyHpLook.Slow => "progress_hp_0_slow",
        _ => "progress_hp_0",
    };

    private void Bar(string id, Rect2 rect, float value, Color? tint = null)
    {
        var source = _layout.Find(id)!;
        foreach (var art in source.Images.OrderBy(a => a.Tag))
        {
            var dest = rect; var src = new Rect2(art.SrcX, art.SrcY, art.SrcW, art.SrcH);
            if (art.Tag == 1) { dest.Size *= new Vector2(value, 1); src.Size *= new Vector2(value, 1); }
            if (dest.Size.X > 0) DrawTextureRectRegion(Plugin.Kit.Texture(art.Texture!), dest, src, art.Tag == 1 ? tint : null);
        }
    }

    public override void _Draw()
    {
        if (!_bound) return;
        bool undead = _member.HpLook == LibreKO.Domain.PartyHpLook.Undead;
        if (_member.MaxHp > 0) Bar(HpArt(), ClassicPartyHudLayout.Hp, Fill(_member.Hp, _member.MaxHp), undead ? new Color(.55f, .55f, .55f) : null);
        if (_member.MaxMp > 0) Bar("progress_mp_0_curse", ClassicPartyHudLayout.Mp, Fill(_member.Mp, _member.MaxMp));
        var mode = _hud.Party.RowMeterMode;
        if (mode != PartyBoardMeterMode.Off)
        {
            var meter = ClassicPartyHudLayout.Meter;
            DrawRect(meter, new Color(0, 0, 0, .9f));
            DrawRect(new Rect2(meter.Position, new Vector2(meter.Size.X * _share, meter.Size.Y)), mode == PartyBoardMeterMode.Healing ? ClassicPartyHudLayout.MeterHealing : ClassicPartyHudLayout.MeterDamage);
        }
        if (_member.Picked) DrawRect(ClassicPartyHudLayout.Selection, new Color("00ff00"), false, 1);
    }

    private static float Fill(int value, int max) => max <= 0 ? 0 : Mathf.Clamp(value / (float)max, 0, 1);
}

/// <summary>One member buff on the name line: the client's skill icon (or its initial) with the client's buff tooltip.</summary>
public partial class ClassicPartyBuff : Control
{
    private Texture2D? _icon;
    private string _initial = "";

    public ClassicPartyBuff() { MouseFilter = MouseFilterEnum.Pass; TextureFilter = TextureFilterEnum.Linear; }

    public int SkillId { get; private set; }
    public bool HasIcon => _icon != null;

    public void Bind(GamePartyBuff buff)
    {
        SkillId = buff.SkillId; _icon = buff.Icon; TooltipText = buff.Tooltip;
        _initial = buff.Tooltip.Length > 0 ? buff.Tooltip[..1] : "?";
        QueueRedraw();
    }

    public override void _Draw()
    {
        var bounds = new Rect2(Vector2.Zero, Size);
        if (_icon != null) { DrawTextureRect(_icon, bounds, false); return; }
        // Without art, the skill's initial (the client draws it bare) on a dark Classic cell.
        DrawRect(bounds, new Color(0, 0, 0, .85f));
        DrawRect(bounds, new Color("82735b"), false, 1);
        var font = Plugin.Kit.Bold; const int size = 9;
        float width = font.GetStringSize(_initial, HorizontalAlignment.Left, -1, size).X;
        DrawString(font, new Vector2((Size.X - width) / 2, (Size.Y - font.GetHeight(size)) / 2 + font.GetAscent(size)), _initial, HorizontalAlignment.Left, -1, size, Colors.White);
    }
}

/// <summary>
/// The member's connection as the client measures it (ff78eef): three arcs lit by the ping level in the client's
/// colours, struck through without a signal, with the client's "Ping: N ms" tooltip.
/// </summary>
public partial class ClassicPartySignal : Control
{
    private static readonly Dictionary<LibreKO.Domain.PartySignalLevel, Color> Tints = new()
    {
        [LibreKO.Domain.PartySignalLevel.Strong] = new Color("4cd964"),
        [LibreKO.Domain.PartySignalLevel.Good] = new Color("f2cf3a"),
        [LibreKO.Domain.PartySignalLevel.Weak] = new Color("e8483c"),
        [LibreKO.Domain.PartySignalLevel.Poor] = new Color("e8483c"),
        [LibreKO.Domain.PartySignalLevel.Lost] = new Color(.56f, .56f, .56f),
        [LibreKO.Domain.PartySignalLevel.Unknown] = new Color(.56f, .56f, .56f),
    };
    private static readonly Color Unlit = new(1, 1, 1, .16f), Shadow = new(0, 0, 0, .45f);
    private int _ms = LibreKO.Domain.PartySignal.Unknown;

    public ClassicPartySignal() { MouseFilter = MouseFilterEnum.Pass; TooltipText = LibreKO.Domain.PartySignal.Tooltip(_ms); }

    public int Ms
    {
        get => _ms;
        set
        {
            if (_ms == value) return;
            _ms = value;
            TooltipText = LibreKO.Domain.PartySignal.Tooltip(value);
            QueueRedraw();
        }
    }

    public LibreKO.Domain.PartySignalLevel Level => LibreKO.Domain.PartySignal.Level(_ms);
    public static Color Tint(LibreKO.Domain.PartySignalLevel level) => Tints[level];

    public override void _Draw()
    {
        var level = Level;
        int lit = LibreKO.Domain.PartySignal.Arcs(level);
        bool lost = level == LibreKO.Domain.PartySignalLevel.Lost;
        var tint = Tints[level];
        const float dot = 1.5f, from = -Mathf.Pi * .75f, to = -Mathf.Pi * .25f;
        var centre = new Vector2(Size.X / 2, Size.Y - dot - 1);
        float step = (centre.Y - .5f) / 3;
        for (int arc = 1; arc <= 3; arc++)
        {
            bool on = arc <= lit || lost;
            if (on) DrawArc(centre, step * arc, from, to, 10, Shadow, 2.8f, true);
            DrawArc(centre, step * arc, from, to, 10, on ? tint : Unlit, 1.5f, true);
        }
        DrawCircle(centre, dot + .7f, Shadow);
        DrawCircle(centre, dot, tint);
        if (!lost) return;
        DrawLine(new Vector2(1, 1), Size - Vector2.One, Shadow, 2.8f, true);
        DrawLine(new Vector2(1, 1), Size - Vector2.One, tint, 1.4f, true);
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName PartyExpectedRect = "party_expected_rect";
}
