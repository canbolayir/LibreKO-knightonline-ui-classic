using NativeApi = KnightOnlineUiClassic.Native.Native;
using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// Classic compositions for the native event windows. None has an original UIF; each is built from the
/// plain Classic frame, even content margins, inventory cells, service buttons and the original close and
/// hotbar arrow buttons, around the client's own labels, buttons and rows.
/// </summary>
public static class ClassicEventsSkin
{
    public static readonly string[] WindowIds = NativeEventWindows.WindowIds;
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicEventPanel? Apply(Control body)
    {
        if (NativeApi.WindowOf(body) is not { } window || window.HasMeta("classic_events")) return null;
        if (window.GetMeta(NativeEventWindows.ControlsMeta, 0).AsInt32() != 1 || NativeEventWindows.WorldOf(window) is not { } world)
        {
            ClassicSkin.Apply(window);
            return null;
        }
        window.SetMeta("classic_events", true);
        ClassicEventPanel panel = window.Id switch
        {
            "collectionrace" => new ClassicCollectionRacePanel(window, body, world),
            "lottery" => new ClassicLotteryPanel(window, body, world),
            "roulette" => new ClassicRoulettePanel(window, body, world),
            _ => new ClassicTournamentPanel(window, body, world),
        };
        window.AddChild(panel);
        window.ResetSize();
        return panel;
    }
}

/// <summary>Shared frame, title, close/minimize controls and component helpers for the event windows.</summary>
public abstract partial class ClassicEventPanel : Control
{
    public const string ExpectedRect = "events_expected_rect";
    public static Color Caption => new("e2c68b");
    public static Color Ink => ClassicDesign.Text;
    public static Color Muted => new("aaa69e");
    public static Color Accepted => new("a0db92");
    public static Color Refused => new("dd9292");
    public static Color Amount => new("dfc184");

    public HudWindow Window { get; }
    protected WorldRoot World { get; }
    protected readonly Control[] NativeControls;
    /// <summary>Everything except the title strip; hidden while the window is minimized.</summary>
    public Control Content { get; }
    public Label TitleLabel { get; }
    public Button? CloseButton { get; protected set; }
    public Button? MinimizeButton { get; }
    private readonly ClassicFrame _frame;
    private readonly Control _grip;
    private Vector2 _full;
    private bool _minimized;

    protected ClassicEventPanel(HudWindow window, Control body, WorldRoot world, Vector2 size, float alpha = 1)
    {
        Window = window; World = world; Name = "classic_events"; TextureFilter = TextureFilterEnum.Nearest;
        NativeControls = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        var bar = window.Header!.GetChildren().OfType<HBoxContainer>().First();
        TitleLabel = bar.GetChildren().OfType<Label>().Last();
        var buttons = bar.GetChildren().OfType<Button>().ToArray();
        CloseButton = buttons.FirstOrDefault(b => b.TooltipText == "Close");
        MinimizeButton = buttons.FirstOrDefault(b => b.TooltipText is "Minimize" or "Restore");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _full = size; Size = CustomMinimumSize = size;
        _frame = new ClassicFrame { Size = size, BackgroundAlpha = alpha, BackgroundColor = Colors.Black };
        AddChild(_frame);
        Content = new Control { Name = "events_content", Size = size, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(Content);
        _grip = new Control { Name = "events_drag", MouseDefaultCursorShape = CursorShape.Move };
        AddChild(_grip);
        _grip.GuiInput += ev => bar.EmitSignal(Control.SignalName.GuiInput, ev);
        ClassicVendorSkin.Move(TitleLabel, this, ClassicEventsLayout.Title(size.X));
        Bold(TitleLabel, ClassicDesign.FrameTitle);
        TitleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        TitleLabel.MouseFilter = MouseFilterEnum.Ignore;
        if (CloseButton != null) SkinClose(CloseButton);
        if (MinimizeButton != null)
        {
            ClassicVendorSkin.Move(MinimizeButton, this, ClassicEventsLayout.Minimize(size.X, CloseButton != null));
            SkinToggle();
        }
        Arrange();
        if (!TicksWhileHidden) ShownOnly.Gate(this, Sync);
    }

    /// <summary>A panel whose refresh acts on the game (the roulette lands its spin) keeps ticking while hidden.</summary>
    protected virtual bool TicksWhileHidden => false;

    /// <summary>Adds a Classic close button for a window whose client version only minimizes.</summary>
    protected void AddClose(Action close)
    {
        var button = new Button { Name = "events_close", TooltipText = "Close" };
        AddChild(button);
        button.Pressed += close;
        SkinClose(button);
        CloseButton = button;
        if (MinimizeButton != null) ClassicVendorSkin.Move(MinimizeButton, this, ClassicEventsLayout.Minimize(_full.X, true));
        Arrange();
    }

    private void SkinClose(Button button)
    {
        ClassicMerchantSkin.Button(button, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, ClassicEventsLayout.Close(_full.X), "");
        button.TooltipText = "Close";
    }

    private void SkinToggle()
    {
        var art = Plugin.Kit.Layout("{nation}_hotkey_us").Find(_minimized ? "btn_down" : "btn_up")!;
        var rect = MinimizeButton!.GetRect();
        ClassicMerchantSkin.Button(MinimizeButton, art, this, rect, "");
        MinimizeButton.Text = "";
    }

    protected void SetFullSize(Vector2 size)
    {
        if (size == _full) return;
        _full = size; Content.Size = size;
        if (!_minimized) _frame.Size = size;
        if (!_minimized) { Size = CustomMinimumSize = size; Window.ResetSize(); }
    }

    private void Arrange()
    {
        float width = _full.X;
        bool closable = CloseButton != null;
        if (_minimized)
        {
            Size = CustomMinimumSize = ClassicEventsLayout.Minimized;
            Place(TitleLabel, ClassicEventsLayout.MinimizedTitle(closable), this);
            Place(_grip, ClassicEventsLayout.MinimizedGrip, this);
            if (MinimizeButton != null) Place(MinimizeButton, ClassicEventsLayout.MinimizedToggle(closable), this);
            if (CloseButton != null) Place(CloseButton, ClassicEventsLayout.MinimizedClose, this);
        }
        else
        {
            Size = CustomMinimumSize = _full;
            Place(TitleLabel, ClassicEventsLayout.Title(width), this);
            Place(_grip, ClassicEventsLayout.Grip(width), this);
            if (MinimizeButton != null) Place(MinimizeButton, ClassicEventsLayout.Minimize(width, closable), this);
            if (CloseButton != null) Place(CloseButton, ClassicEventsLayout.Close(width), this);
        }
        _frame.Size = _minimized ? ClassicEventsLayout.Minimized : _full;
        Content.Visible = !_minimized;
        // The client resets the title's clipping when it minimizes or restores.
        TitleLabel.CustomMinimumSize = Vector2.Zero;
        TitleLabel.ClipText = true;
        TitleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        QueueRedraw();
    }

    public override void _Process(double delta) => Sync();

    private void Sync()
    {
        if (MinimizeButton != null && MinimizeButton.Text.Length > 0) MinimizeButton.Text = "";
        if (Window.Minimized != _minimized)
        {
            _minimized = Window.Minimized;
            if (MinimizeButton != null) SkinToggle();
            Arrange();
            Window.ResetSize();
        }
        if (!_minimized) Refresh();
    }

    /// <summary>Mirrors native state into the Classic composition every visible frame (and once more on show).</summary>
    protected virtual void Refresh() { }

    // Component helpers ------------------------------------------------------------------------------

    protected T Find<T>(string name) where T : Control => NativeControls.OfType<T>().Single(c => c.Name == name);

    protected static void Place(Control control, Rect2 rect, Node parent)
    {
        ClassicVendorSkin.Move(control, parent, rect);
        control.SetMeta(ExpectedRect, rect);
    }

    protected void Place(Control control, Rect2 rect) => Place(control, rect, Content);

    public static void Bold(Control control, Color? ink = null, int size = 13)
    {
        control.AddThemeFontOverride("font", Plugin.Kit.Bold);
        control.AddThemeFontSizeOverride("font_size", size);
        control.AddThemeConstantOverride("outline_size", 0);
        control.AddThemeColorOverride("font_shadow_color", Colors.Black);
        control.AddThemeConstantOverride("shadow_offset_x", 1);
        control.AddThemeConstantOverride("shadow_offset_y", 1);
        if (ink is { } color) control.AddThemeColorOverride("font_color", color);
        if (control is Label label) label.VerticalAlignment = VerticalAlignment.Center;
    }

    protected Label Text(string text, Rect2 rect, Color ink, HorizontalAlignment align = HorizontalAlignment.Left, Node? parent = null)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        (parent ?? Content).AddChild(label);
        label.Position = rect.Position; label.Size = rect.Size;
        Bold(label, ink);
        label.SetMeta(ExpectedRect, rect);
        return label;
    }

    protected void Value(Label label, Rect2 rect, Color ink, HorizontalAlignment align = HorizontalAlignment.Right)
    {
        Place(label, rect);
        Bold(label, ink);
        label.HorizontalAlignment = align;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.SizeFlagsHorizontal = SizeFlags.Fill;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.SelfModulate = Colors.White;
    }

    protected void Action(Button button, Rect2 rect, string? text = null)
    {
        ClassicMerchantSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!, Content, rect, text);
        button.SetMeta(ExpectedRect, rect);
        Bold(button);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, new Color("efd9b4"));
        button.AddThemeColorOverride("font_disabled_color", ClassicDesign.Muted);
    }

    /// <summary>The original quest inset, drawn behind the controls placed inside it.</summary>
    protected Panel Inset(Rect2 rect)
    {
        var inset = new Panel { Position = rect.Position, Size = rect.Size, MouseFilter = MouseFilterEnum.Ignore, Name = "events_inset" };
        inset.AddThemeStyleboxOverride("panel", ClassicNpcLayout.InsetBox());
        Content.AddChild(inset);
        return inset;
    }

    protected void Rule(float y) => Content.AddChild(new ColorRect { Color = new Color("6d5c3d"), Position = new Vector2(ClassicEventsLayout.Margin, y),
        Size = new Vector2(_full.X - ClassicEventsLayout.Margin * 2, 1), MouseFilter = MouseFilterEnum.Ignore });

    public static StyleBoxTexture CellBox()
    {
        bool karus = Plugin.Kit.Nation == 1;
        var box = new StyleBoxTexture { Texture = Plugin.Kit.Texture(karus ? "ui_ka_inven_us.png" : "ui_el_inven_us.png"),
            RegionRect = new Rect2(karus ? 159 : 157, karus ? 326 : 333, 51, 52) };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
        { box.SetTextureMargin(side, 4); box.SetContentMargin(side, 0); }
        box.ExpandMarginLeft = 4; box.ExpandMarginTop = 3; box.ExpandMarginRight = 2; box.ExpandMarginBottom = 4;
        return box;
    }

    /// <summary>Restyles a native icon panel as an original inventory cell with the shared count geometry.</summary>
    public static void Cell(PanelContainer cell)
    {
        if (cell.HasMeta("events_cell")) return;
        cell.SetMeta("events_cell", true);
        cell.CustomMinimumSize = ClassicEventsLayout.Cell;
        cell.AddThemeStyleboxOverride("panel", CellBox());
        var children = cell.GetChildren().OfType<Control>().ToArray();
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore, Name = "events_cell_overlay" };
        cell.AddChild(overlay);
        foreach (var icon in children.OfType<TextureRect>())
        {
            icon.Reparent(overlay, false); icon.CustomMinimumSize = Vector2.Zero;
            icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            icon.OffsetLeft = icon.OffsetTop = 2; icon.OffsetRight = icon.OffsetBottom = -2;
            icon.StretchMode = TextureRect.StretchModeEnum.Scale;
        }
        foreach (var count in children.OfType<Label>()) ItemCountStyle.Apply(count, overlay);
        cell.MoveChild(overlay, 0);
    }

    /// <summary>A plugin-owned cell showing an icon, for reels and lists.</summary>
    public static Panel IconCell(Rect2 rect, Node parent)
    {
        var cell = new Panel { Position = rect.Position, Size = rect.Size, MouseFilter = MouseFilterEnum.Stop, Name = "events_cell" };
        cell.AddThemeStyleboxOverride("panel", CellBox());
        parent.AddChild(cell);
        var icon = new TextureRect { Name = "icon", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore };
        cell.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.OffsetLeft = icon.OffsetTop = 2; icon.OffsetRight = icon.OffsetBottom = -2;
        var mark = new Label { Name = "mark", Text = "?", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        cell.AddChild(mark);
        mark.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Bold(mark, new Color("6f6a60"), 11);
        return cell;
    }

    public static void ShowIcon(Panel cell, Texture2D? texture)
    {
        cell.GetNode<TextureRect>("icon").Texture = texture;
        cell.GetNode<Label>("mark").Visible = texture == null;
    }

    public static Texture2D? ItemIcon(int itemId) =>
        itemId > 0 && (itemId == LibreKO.Domain.QuestData.CoinItemId || LibreKO.Domain.ItemData.Get(itemId) != null) ? LibreKO.ItemIcons.Get(itemId) : null;

    public static Texture2D? GoldIcon => LibreKO.ItemIcons.Get(LibreKO.Domain.QuestData.CoinItemId);

    protected static void Scroll(ScrollContainer scroll) => ClassicDarkFrame.Scroll(scroll);

    /// <summary>Native "Caption: value" text without its caption.</summary>
    protected static string After(string text)
    {
        int split = text.IndexOf(": ", StringComparison.Ordinal);
        return split < 0 ? text : text[(split + 2)..];
    }

    /// <summary>Classic ink for a native semantic modulate (good, bad or dim).</summary>
    protected static Color Semantic(Color native, Color fallback) =>
        native.IsEqualApprox(UiTheme.Good) ? Accepted : native.IsEqualApprox(UiTheme.Bad) ? Refused
        : native.IsEqualApprox(UiTheme.TextDim) || native.IsEqualApprox(UiTheme.TextLo) ? Muted : fallback;
}
