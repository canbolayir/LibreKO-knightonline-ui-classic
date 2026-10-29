using Godot;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Native;
using L = KnightOnlineUiClassic.Layout.ClassicMykoHudLayout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The top-right group on the original art (read through LocalAssets), top to bottom: the premium bar (<c>re_eventpremium</c>,
/// the account premium's name), the clan premium bar while the player is in a clan, the clan bonus bar while the
/// server reports the clan's online count (3277df5: "Clan Bonus" and the client's O/X marks; its content is the
/// client's bonus text), the time box (the client's clock
/// on the content box's frame and text style), the notice bars (<c>re_eventnotice</c>) and the GM List bar. The client's event title chips
/// (server notices with a title and a message: rate events, the clan notice, notices a GM posts) become the teal
/// bars, and the first keeps the client's expand/collapse toggle on the original + / − knob. Pressing a bar opens the
/// content box (<c>re_*_eventcontent</c>) under the group with its details (premium time left and benefits, the
/// notice's message, the online GM names); pressing it again or anywhere else closes it. The client's own premium
/// chips and clock are hidden while the group shows them. Without the art nothing changes.
/// </summary>
public static class ClassicNoticeBars
{
    public const string StackName = "classic_notice_stack", GmBar = "notice_gm_list", PremiumBar = "notice_premium",
        ClanBar = "notice_clan_premium", BonusBar = "notice_clan_bonus", TimePlate = "notice_time", ContentBox = "notice_content", Caption = "notice_caption", Knob = "notice_knob";
    public const string GmKey = "gm", PremiumKey = "premium", ClanKey = "clan", BonusKey = "clanbonus";
    /// <summary>The clan bonus bar: its tooltip title, and its caption before the client's O/X marks (the client's chip says "Clan").</summary>
    public const string BonusTitle = "Clan Bonus", BonusCaption = "Clan";
    private const string Skinned = "classic_notice_bar";

    public static bool Available => Bar != null && ClassicMykoArt.Layout(L.Content) != null && ClassicMykoArt.Layout(L.Premium) != null;

    private static LayoutNode? Bar => ClassicMykoArt.Layout(L.Notice);

    public static string KeyOf(string title) => "title:" + title;

    /// <summary>Takes over the client's title column; false (nothing changed) without the art.</summary>
    public static bool Apply(Container column)
    {
        if (!Available) return false;
        column.AddThemeConstantOverride("separation", L.BarGap);
        foreach (var child in column.GetChildren()) Chip(column, child);
        NativeWindows.OnDescendantAdded(column, node => Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(node) && GodotObject.IsInstanceValid(column)) Chip(column, node);
        }).CallDeferred());
        Callable.From(() => Stack(column)).CallDeferred();
        return true;
    }

    /// <summary>The GM List bar and content box beside the column (created once its parent exists).</summary>
    public static ClassicNoticeStack? Stack(Container column)
    {
        if (!GodotObject.IsInstanceValid(column) || column.GetParent() is not { } parent) return null;
        if (parent.GetNodeOrNull<ClassicNoticeStack>(StackName) is { } existing) return existing;
        var stack = new ClassicNoticeStack(column) { Name = StackName };
        parent.AddChild(stack);
        return stack;
    }

    private static void Chip(Container column, Node node)
    {
        if (node is not PanelContainer chip || chip.HasMeta(Skinned) || Bar?.Find("btn_notice") is not { } art) return;
        chip.SetMeta(Skinned, true);
        var label = chip.GetChildren().OfType<HBoxContainer>().SelectMany(r => r.GetChildren().OfType<Label>()).FirstOrDefault();
        var toggle = chip.GetChildren().OfType<HBoxContainer>().SelectMany(r => r.GetChildren().OfType<Button>()).FirstOrDefault();
        chip.CustomMinimumSize = L.Bar;
        chip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        chip.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        // With the knob, the caption is inset by the knob's width on both sides so it stays centred on the bar.
        int side = toggle != null ? L.CaptionLeft : 1;
        chip.AddThemeStyleboxOverride("panel", new StyleBoxEmpty { ContentMarginLeft = side, ContentMarginRight = L.KnobRight, ContentMarginTop = 2 });
        foreach (var row in chip.GetChildren().OfType<HBoxContainer>()) { row.AddThemeConstantOverride("separation", 0); row.CustomMinimumSize = Vector2.Zero; }
        string title = label?.Text ?? "";
        bool hovered = false;
        chip.MouseEntered += () => { hovered = true; chip.QueueRedraw(); };
        chip.MouseExited += () => { hovered = false; chip.QueueRedraw(); };
        chip.Draw += () =>
        {
            bool open = Stack(column)?.OpenKey == KeyOf(title);
            DrawBar(chip, art, open ? ButtonState.Pressed : hovered ? ButtonState.Hover : ButtonState.Normal);
        };
        if (label != null)
        {
            label.Name = Caption;
            StyleCaption(label);
            label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginBottom = 2 });
            label.TooltipText = "";
        }
        chip.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } click) return;
            if (!new Rect2(Vector2.Zero, chip.Size).HasPoint(click.Position)) return;
            Stack(column)?.Toggle(KeyOf(title));
            chip.AcceptEvent();
        };
        if (toggle != null) SkinKnob(toggle);
    }

    /// <summary>The client's expand/collapse toggle on the original knob: + while one notice shows, − while all do.</summary>
    private static void SkinKnob(Button toggle)
    {
        bool expanded = Game.Session.Zone.EventTitles.Expanded == true;
        if (Bar?.Find(expanded ? "btn_reduction" : "btn_extention") is not { } knob) return;
        toggle.Name = Knob;
        toggle.Text = "";
        toggle.CustomMinimumSize = new Vector2(knob.W, knob.H);
        toggle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        toggle.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        foreach (var (state, tag) in new[] { ("normal", ButtonState.Normal), ("hover", ButtonState.Hover), ("pressed", ButtonState.Pressed),
            ("hover_pressed", ButtonState.Pressed), ("focus", ButtonState.Normal), ("disabled", ButtonState.Disabled) })
            if (ClassicMykoArt.Box(knob, tag) is { } box) toggle.AddThemeStyleboxOverride(state, box);
    }

    public static void DrawBar(CanvasItem canvas, LayoutNode art, int state)
    {
        if (ClassicMykoArt.State(art, state) is { } image && ClassicMykoArt.Texture(image) is { } texture)
            canvas.DrawTextureRectRegion(texture, new Rect2(Vector2.Zero, L.Bar), ClassicMykoArt.Source(image));
    }

    /// <summary>The art of a bar of the group (premium, clan premium, clan bonus, GM List).</summary>
    public static LayoutNode? BarArt(string key) => key switch
    {
        PremiumKey or ClanKey or BonusKey => ClassicMykoArt.Layout(L.Premium)?.Find("btn_premium"),
        _ => Bar?.Find("btn_notice"),
    };

    /// <summary>The bar caption: the original's white bold Verdana, centred on one line.</summary>
    public static void StyleCaption(Label label)
    {
        var node = Bar?.Find("btn_notice")?.Strings.FirstOrDefault();
        if (node != null)
        {
            label.AddThemeFontOverride("font", Plugin.Kit.FontFor(node));
            label.AddThemeFontSizeOverride("font_size", UiKit.FontSize(node));
        }
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    /// <summary>The message of a notice bar, or the GM names (or the empty line) for the GM List.</summary>
    public static string? TextOf(string key)
    {
        if (key == GmKey) return NativeGmList.Names.Count == 0 ? L.NoGms : string.Join("\n", NativeGmList.Names);
        if (Game.Connection is not { } net) return null;
        foreach (var entry in Game.Session.Zone.EventTitles.Entries)
            if (KeyOf(entry.Title) == key) return entry.Message.Length > 0 ? entry.Message : entry.Title;
        return null;
    }
}

/// <summary>
/// The group's own pieces: the premium, clan premium and GM List bars, the time box and the content box. They
/// follow the client's title column (which stays the client's and keeps its bars): the group is right-aligned at the
/// client's premium chip spot, and the column is placed between the time box and the GM List bar.
/// </summary>
public partial class ClassicNoticeStack : Control
{
    private readonly Container _column;
    private readonly ClassicGroupBar _premium, _clan, _bonus, _gm;
    private readonly Control _time, _box, _clip, _thumb;
    private readonly Label _timeValue, _text;
    private readonly TextureButton? _up, _down;
    private readonly Rect2 _track;
    private readonly Vector2 _textArea;
    private int _scroll;
    private double _syncClock;
    public string? OpenKey { get; private set; }

    /// <summary>The client world whose clock and clan state the group shows (the column's world).</summary>
    public LibreKO.Scene.WorldRoot? World { get; set; }
    /// <summary>The clock text ("12:00  Clear"); the client's clock by default.</summary>
    public Func<string?> ClockText { get; set; }
    /// <summary>The clan premium; the client's by default.</summary>
    public Func<NativePremium.Clan> ClanSource { get; set; }
    /// <summary>The clan's online count for the bonus bar (<see cref="Net.NoClanBonus"/> hides it); the client's by default.</summary>
    public Func<int> BonusSource { get; set; }

    public ClassicNoticeStack()
    {
        _column = null!; _premium = _clan = _bonus = _gm = null!; _time = _box = _clip = _thumb = null!; _timeValue = _text = null!;
        ClockText = () => null; ClanSource = () => default; BonusSource = () => ClanSession.NoClanBonus;
    }

    public ClassicNoticeStack(Container column)
    {
        _column = column;
        World = AncestorWorld(column);
        ClockText = () => NativePremium.Clock(World);
        ClanSource = () => NativePremium.ClanOf(World, Game.Connection);
        BonusSource = () => Game.Session.Clan.ClanOnlineCount;
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        _premium = new ClassicGroupBar(this, ClassicNoticeBars.PremiumKey) { Name = ClassicNoticeBars.PremiumBar };
        _clan = new ClassicGroupBar(this, ClassicNoticeBars.ClanKey) { Name = ClassicNoticeBars.ClanBar };
        _bonus = new ClassicGroupBar(this, ClassicNoticeBars.BonusKey) { Name = ClassicNoticeBars.BonusBar };
        _gm = new ClassicGroupBar(this, ClassicNoticeBars.GmKey) { Name = ClassicNoticeBars.GmBar };
        _gm.SetCaption(L.GmListTitle, true); _gm.TooltipText = L.GmListTitle;
        AddChild(_premium); AddChild(_clan); AddChild(_bonus);

        var art = ClassicMykoArt.Layout(L.Content)!;
        var view = ClassicMykoArt.View(art);
        var textNode = art.Find("text_content")!;
        var scroll = art.Find("scroll")!;

        // The time box: the content box's frame (without its scroll bar) at bar size, the clock centred in its text style.
        _time = new Panel { Name = ClassicNoticeBars.TimePlate, Size = L.Bar, CustomMinimumSize = L.Bar, MouseFilter = MouseFilterEnum.Pass };
        if (ClassicMykoArt.Compose(art, n => n.X < scroll.X) is { } frame)
            _time.AddThemeStyleboxOverride("panel", new StyleBoxTexture
            {
                Texture = frame, TextureMarginLeft = L.TimeFrame, TextureMarginTop = L.TimeFrame, TextureMarginRight = L.TimeFrame, TextureMarginBottom = L.TimeFrame,
            });
        _timeValue = new Label
        {
            Name = "notice_time_value", Position = new Vector2(L.TimeFrame, 0), Size = new Vector2(L.Bar.X - 2 * L.TimeFrame, L.Bar.Y),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipText = true, MouseFilter = MouseFilterEnum.Ignore,
        };
        view.Style(_timeValue, textNode);
        _timeValue.AddThemeFontOverride("font", Plugin.Kit.Bold);
        _timeValue.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _time.AddChild(_timeValue);
        AddChild(_time);
        AddChild(_gm);

        _box = new Control { Name = ClassicNoticeBars.ContentBox, Size = art.SizeVec, CustomMinimumSize = art.SizeVec, MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _box.AddChild(view);
        _textArea = textNode.SizeVec;
        view.Get("text_content")!.Visible = false;
        _clip = new Control { Name = "notice_text_clip", Position = textNode.Position, Size = textNode.SizeVec, ClipContents = true, MouseFilter = MouseFilterEnum.Stop };
        _text = new Label { Name = "notice_text", AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore, Size = new Vector2(textNode.W, 0) };
        view.Style(_text, textNode);
        _text.AddThemeFontOverride("font", Plugin.Kit.Bold);
        _text.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _text.AddThemeConstantOverride("line_spacing", 0);
        _clip.AddChild(_text);
        _box.AddChild(_clip);
        _clip.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
            {
                ScrollBy(wheel.ButtonIndex == MouseButton.WheelUp ? -L.ContentLineScroll : L.ContentLineScroll);
                _clip.AcceptEvent();
            }
        };
        // The art's scroll bar: up and down by a line, the thumb shows the position.
        var buttons = scroll.Children.Where(n => n.IsButton).ToList();
        _up = buttons.Count > 0 ? view.ControlOf(buttons[0]) as TextureButton : null;
        _down = buttons.Count > 1 ? view.ControlOf(buttons[1]) as TextureButton : null;
        if (_up != null) { _up.Name = "notice_scroll_up"; _up.Pressed += () => ScrollBy(-L.ContentLineScroll); }
        if (_down != null) { _down.Name = "notice_scroll_down"; _down.Pressed += () => ScrollBy(L.ContentLineScroll); }
        var trackbar = scroll.Children.First(n => n.Type == "trackbar");
        var thumbNode = trackbar.Images.First(i => i.Tag == 1);
        _thumb = view.ControlOf(thumbNode)!;
        _thumb.Name = "notice_scroll_thumb";
        _track = new Rect2(thumbNode.X - trackbar.X, 0, thumbNode.W, trackbar.H);
        AddChild(_box);
        NativeGmList.Changed += Refresh;
        if (Game.Connection is { } net)
            NativeHooks.Listen("Net.EventTitlesChangedEvent (notice group)", () => Game.Session.Zone.EventTitlesChangedEvent += Refresh, () => Game.Session.Zone.EventTitlesChangedEvent -= Refresh, this);
        NativeGmList.Attach(this);
        Sync();
    }

    private static LibreKO.Scene.WorldRoot? AncestorWorld(Node node)
    {
        for (var current = node.GetParent(); current != null; current = current.GetParent())
            if (current is LibreKO.Scene.WorldRoot world) return world;
        return null;
    }

    public override void _ExitTree() => NativeGmList.Changed -= Refresh;

    public Control PremiumBar => _premium;
    public Control ClanBar => _clan;
    public Control BonusBar => _bonus;
    public string BonusCaption => _bonus.Caption.Text;
    public Control TimePlate => _time;
    public Label TimeValue => _timeValue;
    public Control GmBar => _gm;
    public Control Box => _box;
    public Label Text => _text;
    public string PremiumCaption => _premium.Caption.Text;
    public string ClanCaption => _clan.Caption.Text;
    public int Scroll => _scroll;
    public int MaxScroll => Math.Max(0, _text.GetLineCount() - VisibleLines);
    private int VisibleLines => Math.Max(1, (int)(_clip.Size.Y / Math.Max(1, _text.GetLineHeight())));

    /// <summary>Opens the box for a bar, switches to another bar, or closes it when the open bar is pressed again.</summary>
    public void Toggle(string key)
    {
        OpenKey = OpenKey == key ? null : key;
        _scroll = 0;
        Refresh();
    }

    private string? TextOf(string key) => key switch
    {
        ClassicNoticeBars.ClanKey => NativePremium.ClanDetails(ClanSource()),
        ClassicNoticeBars.BonusKey => Bonus() is { Shown: true } bonus ? bonus.Tooltip : null,
        ClassicNoticeBars.PremiumKey => NativePremium.Details(NativePremium.Current(Game.Connection)),
        _ => ClassicNoticeBars.TextOf(key),
    };

    public void Refresh()
    {
        if (!GodotObject.IsInstanceValid(this)) return;
        Sync();
        string? text = OpenKey == null ? null : TextOf(OpenKey);
        if (OpenKey == ClassicNoticeBars.ClanKey && !_clan.Visible) text = null;
        if (OpenKey == ClassicNoticeBars.BonusKey && !_bonus.Visible) text = null;
        if (OpenKey != null && text == null) OpenKey = null;
        _box.Visible = OpenKey != null;
        if (text != null && _text.Text != text) { _text.Text = text; _scroll = 0; }
        // Whole lines only: the text area keeps the original top and ends on a line.
        int line = Math.Max(1, _text.GetLineHeight());
        _clip.Size = new Vector2(_textArea.X, Mathf.Floor(_textArea.Y / line) * line);
        _text.ResetSize();
        _text.Size = new Vector2(_clip.Size.X, Math.Max(_clip.Size.Y, _text.GetLineCount() * _text.GetLineHeight()));
        ScrollBy(0);
        foreach (var bar in new[] { _premium, _clan, _bonus, _gm }) bar.QueueRedraw();
        if (GodotObject.IsInstanceValid(_column)) foreach (var chip in _column.GetChildren().OfType<CanvasItem>()) chip.QueueRedraw();
    }

    private void ScrollBy(int lines)
    {
        _scroll = Math.Clamp(_scroll + lines, 0, MaxScroll);
        _text.Position = new Vector2(0, -_scroll * _text.GetLineHeight());
        float room = _track.Size.Y - _thumb.Size.Y;
        _thumb.Position = new Vector2(_thumb.Position.X, _track.Position.Y + (MaxScroll == 0 ? 0 : room * _scroll / MaxScroll));
        if (_up != null) _up.Disabled = _scroll == 0;
        if (_down != null) _down.Disabled = _scroll >= MaxScroll;
    }

    private NativePremium.ClanBonus Bonus()
    {
        int online = BonusSource();
        return NativePremium.BonusLines(ClanSource().InClan && online != ClanSession.NoClanBonus, online);
    }

    /// <summary>Premium, clan and bonus captions, the clock, and the client's own chips and clock kept hidden.</summary>
    public void Sync()
    {
        var account = NativePremium.Current(Game.Connection);
        _premium.SetCaption(account.Name, account.Active);
        _premium.TooltipText = account.Active ? account.Name + " (click for the time left and benefits)" : "No Premium";
        var clan = ClanSource();
        _clan.Visible = clan.InClan;
        _clan.SetCaption(clan.Active ? "Clan Premium" : "Clan: No Premium", clan.Active);
        _clan.TooltipText = clan.Active ? "Clan Premium (click for the time left)" : "Clan: No Premium";
        var bonus = Bonus();
        _bonus.Visible = bonus.Shown;
        if (bonus.Shown)
        {
            _bonus.SetCaption(ClassicNoticeBars.BonusCaption + "   " + bonus.Marks, true);
            _bonus.TooltipText = ClassicNoticeBars.BonusTitle + ": " + bonus.Online + " online (click for the bonus)";
        }
        string clock = ClockText() ?? "";
        int split = clock.IndexOf(' ');
        string time = split > 0 ? clock[..split] : clock.Length > 0 ? clock : "--:--";
        string weather = split > 0 ? clock[split..].Trim() : "";
        if (_timeValue.Text != L.TimeNote + time) _timeValue.Text = L.TimeNote + time;
        _time.TooltipText = "Server time " + time + (weather.Length > 0 ? ", " + weather : "");
        if (World is { } world && GodotObject.IsInstanceValid(world)) NativePremium.HideClientChips(world, L.Bar.X);
    }

    private Control[]? _stacked;

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_column)) { QueueFree(); return; }
        _syncClock += delta;
        if (_syncClock >= 0.25) { _syncClock = 0; Sync(); }
        // Right-aligned at the client's premium chip spot: premium, clan premium, time, the client's notice bars, GM List.
        float right = GetViewportRect().Size.X - L.GroupRight, y = L.GroupTop;
        foreach (var part in _stacked ??= new Control[] { _premium, _clan, _bonus, _time })
        {
            if (!part.Visible) continue;
            SetPlace(part, new Vector2(right - part.Size.X, y));
            y += part.Size.Y + L.BarGap;
        }
        SetPlace(_column, new Vector2(right - _column.Size.X, y));
        // The last shown notice bar, read by index (no child array per frame).
        Control? last = null;
        if (_column.Visible)
            for (int i = _column.GetChildCount() - 1; i >= 0 && last == null; i--)
                if (_column.GetChild(i) is Control c && c.Visible && !c.IsQueuedForDeletion()) last = c;
        if (last != null) y = last.GlobalPosition.Y + last.Size.Y + L.BarGap;
        SetPlace(_gm, new Vector2(right - L.Bar.X, y));
        SetPlace(_box, new Vector2(right - _box.Size.X, y + L.Bar.Y + L.ContentGap));
    }

    /// <summary>A click anywhere outside the group's bars and the box closes the box (the click goes on).</summary>
    public override void _Input(InputEvent input)
    {
        if (OpenKey == null || input is not InputEventMouseButton { Pressed: true } click) return;
        var parts = new List<Control> { _premium, _clan, _bonus, _gm, _box };
        if (GodotObject.IsInstanceValid(_column)) parts.AddRange(_column.GetChildren().OfType<Control>());
        if (parts.Any(p => p.IsVisibleInTree() && ScreenRect(p).HasPoint(click.Position))) return;
        OpenKey = null;
        Refresh();
    }

    private static Rect2 ScreenRect(Control control)
    {
        var transform = control.GetGlobalTransformWithCanvas();
        return new Rect2(transform.Origin, control.Size * transform.Scale);
    }

    private static void SetPlace(Control control, Vector2 at)
    {
        at = at.Round();
        if (control.GlobalPosition != at) control.GlobalPosition = at;
    }
}

/// <summary>A bar of the group drawn on its original art; pressing it opens or closes its content.</summary>
public partial class ClassicGroupBar : Control
{
    private readonly ClassicNoticeStack _stack;
    private readonly string _key;
    private bool _hovered;
    public Label Caption { get; }

    public ClassicGroupBar() { _stack = null!; _key = ""; Caption = null!; }

    public ClassicGroupBar(ClassicNoticeStack stack, string key)
    {
        _stack = stack; _key = key;
        Size = L.Bar; CustomMinimumSize = L.Bar;
        MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand;
        Caption = new Label { Name = ClassicNoticeBars.Caption, Position = new Vector2(1, 0), Size = L.Bar - new Vector2(2, 2) };
        ClassicNoticeBars.StyleCaption(Caption);
        AddChild(Caption);
        MouseEntered += () => { _hovered = true; QueueRedraw(); };
        MouseExited += () => { _hovered = false; QueueRedraw(); };
        GuiInput += input =>
        {
            if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } click || !new Rect2(Vector2.Zero, Size).HasPoint(click.Position)) return;
            _stack.Toggle(_key);
            AcceptEvent();
        };
    }

    /// <summary>The caption in white, or muted while the bar shows no premium.</summary>
    public void SetCaption(string text, bool active)
    {
        if (Caption.Text != text) Caption.Text = text;
        ThemeNames.SetColor(Caption, ThemeNames.FontColor, active ? Colors.White : L.Muted);
    }

    public override void _Draw()
    {
        if (ClassicNoticeBars.BarArt(_key) is { } art)
            ClassicNoticeBars.DrawBar(this, art, _stack.OpenKey == _key ? ButtonState.Pressed : _hovered ? ButtonState.Hover : ButtonState.Normal);
    }
}
