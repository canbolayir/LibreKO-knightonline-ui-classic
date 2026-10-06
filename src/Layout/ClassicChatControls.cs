using Godot;

namespace KnightOnlineUiClassic.Layout;

public static class ClassicChatControls
{
    private static LayoutNode CommandCaption(int state = 0)
    {
        var button = Plugin.Kit.Layout("{nation}_cmd_us").Find("btn_inventory")!;
        return button.Images.First(n => n.Tag == state).Strings.First();
    }

    public static void SnapToPixels(Control control)
    {
        bool snapping = false;
        control.ItemRectChanged += () =>
        {
            if (snapping) return;
            snapping = true;
            var position = control.Position.Round();
            var size = control.Size.Round();
            if (control.Position != position) control.Position = position;
            if (control.Size != size) control.Size = size;
            snapping = false;
        };
    }
    public static Texture2D? Atlas(LayoutNode image) => image.Texture == null ? null : new AtlasTexture
    {
        Atlas = Plugin.Kit.Texture(image.Texture),
        Region = new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH),
    };

    public static StyleBoxTexture TextureStyle(LayoutNode image, int border = 2)
    {
        var style = new StyleBoxTexture
        {
            Texture = image.Texture == null ? null : Plugin.Kit.Texture(image.Texture),
            RegionRect = new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH),
        };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
            style.SetTextureMargin(side, border);
        style.SetContentMarginAll(0);
        return style;
    }

    public static void SkinButton(Button button, LayoutNode source)
    {
        string[] states = { "normal", "pressed", "hover", "disabled" };
        for (int i = 0; i < states.Length; i++)
        {
            var image = source.Images.FirstOrDefault(n => n.Tag == i) ?? source.Images.First();
            button.AddThemeStyleboxOverride(states[i], TextureStyle(image));
        }
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    public static Button Tab(string text)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, ClipText = true };
        SkinButton(button, Plugin.Kit.Layout("{nation}_chat_us").Find("btn_normal")!);
        var caption = CommandCaption();
        button.AddThemeFontOverride("font", Plugin.Kit.FontFor(caption));
        button.AddThemeFontSizeOverride("font_size", UiKit.FontSize(caption));
        button.AddThemeColorOverride("font_color", caption.Color);
        button.AddThemeColorOverride("font_hover_color", CommandCaption(2).Color);
        button.AddThemeColorOverride("font_pressed_color", CommandCaption(1).Color);
        button.AddThemeColorOverride("font_hover_pressed_color", CommandCaption(1).Color);
        button.AddThemeColorOverride("font_focus_color", caption.Color);
        button.AddThemeColorOverride("font_outline_color", Colors.Black);
        button.AddThemeConstantOverride("outline_size", 1);
        if (Plugin.Kit.Nation == 1)
        {
            // A separate caption keeps black ink readable in every Karus tab state.
            button.Text = "";
            var label = new Label
            {
                Text = text, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true,
            };
            KarusCaption(label, dark: true);
            button.AddChild(label);
            label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        return button;
    }

    private static void KarusCaption(Label label, bool dark = false)
    {
        var caption = CommandCaption();
        label.AddThemeFontOverride("font", Plugin.Kit.FontFor(caption));
        label.AddThemeFontSizeOverride("font_size", UiKit.FontSize(caption));
        label.AddThemeColorOverride("font_color", dark ? Colors.Black : new Color("f4f1e6"));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", dark ? 0 : 2);
        label.AddThemeColorOverride("font_shadow_color", dark ? Colors.Transparent : Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", dark ? 0 : 1);
        label.AddThemeConstantOverride("shadow_offset_y", dark ? 0 : 1);
        label.AddThemeConstantOverride("shadow_outline_size", dark ? 0 : 1);
    }

    public static void Input(LineEdit input, bool originalChat = false)
    {
        input.AddThemeFontOverride("font", Plugin.Kit.ChatFont);
        input.AddThemeFontSizeOverride("font_size", 13);
        input.AddThemeColorOverride("font_color", Colors.White);
        if (originalChat)
        {
            // This row stays visible without a border or a button texture.
            var native = new StyleBoxEmpty();
            native.ContentMarginLeft = native.ContentMarginRight = 5;
            native.ContentMarginTop = native.ContentMarginBottom = 1;
            input.AddThemeStyleboxOverride("normal", native);
            input.AddThemeStyleboxOverride("focus", native);
            input.AddThemeColorOverride("font_uneditable_color", Colors.White);
            input.AddThemeColorOverride("font_placeholder_color", new Color("d7d5cb"));
            input.AddThemeColorOverride("font_outline_color", Colors.Black);
            input.AddThemeConstantOverride("outline_size", 1);
            return;
        }
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.8f), BorderColor = new Color("968a65") };
        style.SetBorderWidthAll(1);
        style.ContentMarginLeft = style.ContentMarginRight = 5;
        style.ContentMarginTop = style.ContentMarginBottom = 2;
        input.AddThemeStyleboxOverride("normal", style);
        input.AddThemeStyleboxOverride("focus", style);
    }

    public static Panel Title(string text)
    {
        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Move };
        panel.AddThemeStyleboxOverride("panel", FooterStyle());
        var label = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var caption = CommandCaption();
        label.AddThemeFontOverride("font", Plugin.Kit.FontFor(caption));
        label.AddThemeFontSizeOverride("font_size", UiKit.FontSize(caption));
        label.AddThemeColorOverride("font_color", caption.Color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 1);
        if (Plugin.Kit.Nation == 1) KarusCaption(label, dark: true);
        panel.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return panel;
    }

    public static Panel Backdrop()
    {
        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        var body = Plugin.Kit.Layout("{nation}_chat_us").Images.First(n => n.Y == 0 && n.X > 0 && n.W > 100);
        var style = TextureStyle(body, 0);
        // The source also contains a darker edit strip from row 107 onward.
        // Use only a clean sample of its uniform native background opacity.
        style.RegionRect = new Rect2(body.SrcX + 4, body.SrcY + 4, 4, 4);
        style.AxisStretchHorizontal = style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile;
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    // The first 84 pixels are the original Chat frame. Its remaining pixels
    // contain the old checkbox socket and must never be stretched into a tab.
    public static StyleBoxTexture FooterStyle()
    {
        var layout = Plugin.Kit.Layout("{nation}_chat_us");
        var image = layout.Images.FirstOrDefault(n => n.X == 0 && n.Y == 123)
            ?? layout.Images.First(n => n.X == 0 && n.Y == 0);
        int offset = image.Y == 0 ? 123 : 0;
        var style = new StyleBoxTexture
        {
            Texture = Plugin.Kit.Texture(image.Texture!),
            RegionRect = new Rect2(image.SrcX, image.SrcY + offset, 84, Math.Min(22, image.SrcH - offset)),
        };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom }) style.SetTextureMargin(side, 3);
        style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile;
        style.SetContentMarginAll(0);
        return style;
    }

    public static Panel Frame()
    {
        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", FooterStyle());
        return panel;
    }

    public static Panel TabFrame(Button tab)
    {
        var frame = Frame();
        frame.AddChild(tab);
        tab.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        tab.OffsetLeft = 3;
        tab.OffsetRight = -3;
        tab.OffsetTop = Plugin.Kit.Nation == 1 ? 2 : 3;
        tab.OffsetBottom = Plugin.Kit.Nation == 1 ? -2 : -3;
        return frame;
    }
}

public sealed class ClassicChatTransparency
{
    private readonly Control _surface;
    private readonly Control _title;
    private readonly string _setting;
    private int _step;

    public ClassicChatTransparency(Control surface, Control title, string setting)
    {
        _surface = surface;
        _title = title;
        _setting = setting;
        _step = Mathf.Clamp(Plugin.Kit.Context.Settings.GetInt(setting, 0), 0, 3);
        Apply();
    }

    public void Cycle()
    {
        _step = (_step + 1) % 4;
        Plugin.Kit.Context.Settings.SetInt(_setting, _step);
        Apply();
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private void Apply()
    {
        float alpha = 1 - _step * 0.25f;
        _surface.Modulate = Colors.White;
        foreach (var child in Descendants(_surface))
        {
            // SelfModulate fades a Panel's artwork without fading its child label.
            if (child is Panel panel) panel.SelfModulate = new Color(1, 1, 1, alpha);
            if (child is Button button)
                foreach (var state in new[] { "normal", "pressed", "hover", "disabled" })
                    if (button.GetThemeStylebox(state) is StyleBoxTexture texture)
                        texture.ModulateColor = new Color(1, 1, 1, alpha);
        }
        _title.TooltipText = $"Click to change transparency ({_step * 25}%); drag to move";
    }
}
public partial class ClassicScrollRail : Control
{
    private readonly TextureButton _up;
    private readonly TextureButton _down;
    private readonly ClassicScrollTrack _track;
    // Range model only: the visible rail draws native pixels instead of stretching
    // a proportional VScrollBar grabber over the entire height of an empty log.
    public VScrollBar Bar { get; } = new() { Visible = false, FocusMode = FocusModeEnum.None };

    public ClassicScrollRail(LogText log)
    {
        MouseFilter = MouseFilterEnum.Stop;
        var source = Plugin.Kit.Layout("{nation}_chat_us").Find("scroll")!;
        var arrows = source.Children.Where(n => n.IsButton).OrderBy(n => n.Y).ToArray();
        _up = Arrow(arrows[0]);
        _down = Arrow(arrows[1]);
        _up.Pressed += () => log.Scroll(26);
        _down.Pressed += () => log.Scroll(-26);
        AddChild(_up);
        AddChild(_down);
        AddChild(Bar);
        _track = new ClassicScrollTrack(Bar, source.Children.First(n => n.Type == "trackbar"));
        AddChild(_track);
        Bar.Changed += Refresh;
        Bar.ValueChanged += _ => Refresh();
        log.BindScrollBar(Bar);
        Resized += Arrange;
        Arrange();
        Refresh();
    }

    private static TextureButton Arrow(LayoutNode source) => new()
    {
        TextureNormal = ClassicChatControls.Atlas(source.Images.First(n => n.Tag == 0)),
        TextureDisabled = ClassicChatControls.Atlas(source.Images.First(n => n.Tag == 0)),
        TexturePressed = ClassicChatControls.Atlas(source.Images.First(n => n.Tag == 1)),
        TextureHover = ClassicChatControls.Atlas(source.Images.First(n => n.Tag == 2)),
        FocusMode = FocusModeEnum.None, IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.Scale,
    };

    private void Refresh()
    {
        double end = Math.Max(Bar.MinValue, Bar.MaxValue - Bar.Page);
        _up.Disabled = Bar.Value <= Bar.MinValue;
        _down.Disabled = Bar.Value >= end;
        _up.SelfModulate = new Color(1, 1, 1, _up.Disabled ? 0.7f : 1);
        _down.SelfModulate = new Color(1, 1, 1, _down.Disabled ? 0.7f : 1);
        _track.QueueRedraw();
    }

    private void Arrange()
    {
        _up.Position = Vector2.Zero;
        _up.Size = new Vector2(18, 18);
        _down.Position = new Vector2(0, Mathf.Max(18, Size.Y - 18));
        _down.Size = new Vector2(18, 18);
        _track.Position = new Vector2(0, 18);
        _track.Size = new Vector2(18, Mathf.Max(18, Size.Y - 36));
    }
}

public partial class ClassicScrollTrack : Control
{
    private readonly float _thumbHeight;
    private readonly bool _emptyAtEnd;
    private readonly float _scrollStep;
    private readonly VScrollBar _range;
    private readonly Texture2D? _texture;
    private readonly Rect2 _trackRegion;
    private readonly Rect2 _thumbRegion;
    private bool _dragging;
    private bool _hovering;
    private float _dragOffset;

    public ClassicScrollTrack(VScrollBar range, LayoutNode source, float thumbHeight=18, bool emptyAtEnd=true, float scrollStep=26)
    {
        _range = range;
        _thumbHeight=thumbHeight;
        _emptyAtEnd=emptyAtEnd;
        _scrollStep=scrollStep;
        var track = source.Images.First(n => n.Tag == 0);
        var thumb = source.Images.First(n => n.Tag == 1);
        _texture = Plugin.Kit.Texture(track.Texture!);
        _trackRegion = new Rect2(track.SrcX, track.SrcY, track.SrcW, track.SrcH);
        _thumbRegion = new Rect2(thumb.SrcX, thumb.SrcY, thumb.SrcW, thumb.SrcH);
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hovering = true; QueueRedraw(); };
        MouseExited += () => { _hovering = false; QueueRedraw(); };
        Resized += QueueRedraw;
    }

    private double Span => Math.Max(0, _range.MaxValue - _range.Page - _range.MinValue);
    private float Travel => Mathf.Max(0, Size.Y - _thumbHeight);
    private float ThumbTop => Span <= 0 ? (_emptyAtEnd ? Travel:0) : Travel * (float)((_range.Value - _range.MinValue) / Span);

    public override void _Draw()
    {
        if (_texture == null) return;
        // Preserve each original texture sample. Only the final tile is cropped.
        float pitch = Math.Max(1, _trackRegion.Size.Y);
        for (float y = 0; y < Size.Y; y += pitch)
        {
            float height = Math.Min(pitch, Size.Y - y);
            DrawTextureRectRegion(_texture, new Rect2(0, y, Size.X, height),
                new Rect2(_trackRegion.Position, new Vector2(_trackRegion.Size.X, height)));
        }
        var tint = Span <= 0 ? new Color(1, 1, 1, 0.7f)
            : _dragging ? new Color(0.85f, 0.85f, 0.85f)
            : _hovering ? new Color(1.12f, 1.12f, 1.12f) : Colors.White;
        DrawTextureRectRegion(_texture, new Rect2(0, Mathf.Round(ThumbTop), Size.X, _thumbHeight), _thumbRegion, tint);
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true } press) return;
        if (press.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _range.Value += press.ButtonIndex == MouseButton.WheelUp ? -_scrollStep : _scrollStep;
            AcceptEvent();
            return;
        }
        if (press.ButtonIndex != MouseButton.Left) return;
        if (Span > 0)
        {
            if (press.Position.Y >= ThumbTop && press.Position.Y < ThumbTop + _thumbHeight)
            {
                _dragging = true;
                _dragOffset = press.Position.Y - ThumbTop;
                MouseDefaultCursorShape = CursorShape.Vsize;
            }
            else _range.Value += press.Position.Y < ThumbTop ? -_range.Page : _range.Page;
        }
        QueueRedraw();
        AcceptEvent();
    }

    public override void _Input(InputEvent ev)
    {
        if (!_dragging) return;
        if (ev is InputEventMouseMotion)
        {
            if (Travel > 0)
                _range.Value = _range.MinValue + Mathf.Clamp((GetLocalMousePosition().Y - _dragOffset) / Travel, 0, 1) * Span;
            GetViewport().SetInputAsHandled();
        }
        else if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _dragging = false;
            MouseDefaultCursorShape = CursorShape.Arrow;
            QueueRedraw();
            GetViewport().SetInputAsHandled();
        }
    }
}

