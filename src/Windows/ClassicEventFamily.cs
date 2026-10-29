using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
using NativeApi = KnightOnlineUiClassic.Native.Native;
using Spec = KnightOnlineUiClassic.Layout.ClassicEventFamilyLayout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The Classic look of the event, ranking and war windows the client builds from its own rows (Chaos Dungeon,
/// war results, daily and board rankings, the event schedule, Monster Stone, Wanted, siege war, Knight Royale).
/// None has an original UIF. The client's containers keep arranging every row; the window gets the plain Classic
/// frame of the event windows, the gold title and close knob, and every control the materials of the reviewed
/// Classic windows: Arial Bold with the event ink, service buttons, inventory cells, quest insets, original
/// scroll and check art. Rows the client rebuilds later are restyled as they arrive.
/// </summary>
public static class ClassicEventFamily
{
    public const string Skinned = "classic_event_family";

    /// <summary>Client windows of this family; none is in the plugin API's list of known window ids.</summary>
    public static readonly string[] WindowIds =
    {
        "events", "daily_rank", "rank_board", "war_result", "war_ranking", "chaos_ladder", "chaos_skill", "chaos_caption",
        "nestindun", "vanguard_invite", "vanguard_invite_mini", "vanguard_list", "vanguard_list_mini", "siege_war", "royale_score",
    };

    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();

    /// <summary>Applies the Classic frame and materials to the window owning <paramref name="body"/>.</summary>
    public static ClassicFrame? Apply(Control body)
    {
        if (NativeApi.WindowOf(body) is not { } window || window.HasMeta(Skinned)) return null;
        window.SetMeta(Skinned, true);
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var frame = new ClassicFrame { Name = "classic_frame", BackgroundColor = Colors.Black, BackgroundAlpha = 1 };
        window.AddChild(frame);
        window.MoveChild(frame, 0);
        Header(window);
        if (window.Body.GetParent() is MarginContainer content)
        {
            content.AddThemeConstantOverride("margin_left", Spec.Content.Left);
            content.AddThemeConstantOverride("margin_top", Spec.Content.Top);
            content.AddThemeConstantOverride("margin_right", Spec.Content.Right);
            content.AddThemeConstantOverride("margin_bottom", Spec.Content.Bottom);
        }
        int width = Spec.BodyWidth(window.Id);
        if (width > 0) window.Body.CustomMinimumSize = new Vector2(width, window.Body.CustomMinimumSize.Y);
        // Tooltips of the family's controls take the original tooltip look (co_tooltip_us).
        window.Body.Theme ??= ClassicPremiumChip.Tooltip();
        // The event schedule has its own composition; what it does not claim gets the family materials.
        bool schedule = window.Id == "events" && ClassicEventSchedule.Apply(window);
        Restyle(window.Body);
        if (window.Id == "nestindun") FitDescription(window.Body);
        Native.NativeWindows.OnDescendantAdded(window.Body, node => Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(node)) return;
            if (schedule) ClassicEventSchedule.Arrived(node);
            Restyle(node);
        }).CallDeferred());
        window.ResetSize();
        return frame;
    }

    /// <summary>
    /// Monster Stone: the client reserves a fixed 160-pixel block for the stone's description, which leaves an empty
    /// band under a one-line text. The block keeps its width and takes the height of its text, so the question and
    /// the buttons follow the description at the column's gap and the window fits its content.
    /// </summary>
    private static void FitDescription(Control body)
    {
        if (Tree(body).OfType<Label>().FirstOrDefault(l => l.AutowrapMode != TextServer.AutowrapMode.Off && l.CustomMinimumSize.Y > 0) is { } text)
            text.CustomMinimumSize = new Vector2(text.CustomMinimumSize.X, 0);
    }

    internal static void Header(HudWindow window)
    {
        if (window.Header is not { } header) return;
        var band = new StyleBoxEmpty();
        band.ContentMarginLeft = Spec.Header.Left; band.ContentMarginRight = Spec.Header.Right;
        band.ContentMarginTop = Spec.Header.Top; band.ContentMarginBottom = 0;
        header.AddThemeStyleboxOverride("panel", band);
        foreach (var child in header.GetChildren().OfType<Control>())
            if (child is not HBoxContainer) child.Visible = false;
        if (header.GetChildren().OfType<HBoxContainer>().FirstOrDefault() is not { } bar) return;
        bar.CustomMinimumSize = new Vector2(0, Spec.TitleHeight);
        bar.AddThemeConstantOverride("separation", 6);
        var labels = bar.GetChildren().OfType<Label>().ToArray();
        // The first label is the client's diamond marker; the last is the title.
        foreach (var marker in labels.Take(labels.Length - 1)) marker.Visible = false;
        foreach (var icon in bar.GetChildren().OfType<TextureRect>()) icon.Visible = false;
        if (labels.LastOrDefault() is { } title)
        {
            ClassicEventPanel.Bold(title, ClassicDesign.FrameTitle);
            title.ClipText = true; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
        foreach (var button in bar.GetChildren().OfType<Button>())
        {
            if (button.TooltipText == "Close")
            {
                ClassicVendorSkin.Button(button, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!);
                button.CustomMinimumSize = Spec.Close; button.Text = "";
            }
            else if (button.TooltipText is "Minimize" or "Restore")
            {
                button.AddChild(new ClassicFamilyToggle(button) { Name = "classic_toggle" });
                button.CustomMinimumSize = Spec.Minimize;
            }
            else
            {
                foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
                button.AddThemeColorOverride("icon_normal_color", ClassicEventPanel.Caption);
            }
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            button.FocusMode = Control.FocusModeEnum.None;
        }
    }

    // ---- Materials --------------------------------------------------------------------------------

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < .02f && Math.Abs(a.G - b.G) < .02f && Math.Abs(a.B - b.B) < .02f;

    /// <summary>The bright gold of highlights and chosen states.</summary>
    public static Color Highlight => new("ffe094");

    /// <summary>Classic ink for a client colour: gold captions, bright gold highlights, light text, muted hints, states.</summary>
    public static Color Ink(Color native)
    {
        if (Near(native, UiTheme.GoldBright) || Near(native, UiTheme.GoldVivid) || Near(native, new Color("ffe08a"))) return Highlight;
        if (Near(native, UiTheme.Gold) || Near(native, UiTheme.Neutral) || Near(native, UiTheme.Warning) || Near(native, UiTheme.Bronze)) return ClassicEventPanel.Caption;
        if (Near(native, UiTheme.TextHi) || Near(native, UiTheme.Self) || Near(native, Colors.White) || Near(native, new Color("f1ead9"))) return ClassicEventPanel.Ink;
        if (Near(native, UiTheme.TextLo) || Near(native, UiTheme.TextDim)) return ClassicEventPanel.Muted;
        if (Near(native, UiTheme.Good)) return ClassicEventPanel.Accepted;
        if (Near(native, UiTheme.Bad) || Near(native, UiTheme.Hp)) return ClassicEventPanel.Refused;
        if (Near(native, UiTheme.Premium) || Near(native, UiTheme.Mp)) return new Color("9cd8f0");
        // Other client accents keep their meaning in the nearest Classic ink: greys by brightness, then by hue.
        if (native.S < .25f) return native.V > .75f ? ClassicEventPanel.Ink : ClassicEventPanel.Muted;
        float hue = native.H;
        if (hue < .05f || hue > .9f) return ClassicEventPanel.Refused;
        if (hue < .2f) return native.V > .88f && native.S < .5f ? new Color("ffe094") : ClassicEventPanel.Caption;
        if (hue < .45f) return ClassicEventPanel.Accepted;
        if (hue < .75f) return new Color("9cd8f0");
        return ClassicEventPanel.Ink;
    }

    /// <summary>Restyles a node and everything under it once; tooltips and other popups keep their own look.</summary>
    public static void Restyle(Node root)
    {
        for (var up = root; up != null; up = up.GetParent())
            if (up is Popup) return;
        foreach (var node in Tree(root))
        {
            if (node is not Control control || control.HasMeta(Skinned) || control is HudWindow) continue;
            control.SetMeta(Skinned, true);
            switch (control)
            {
                case ItemSlotView slot: ClassicSlotLook.Attach(slot, light => ClassicProgressionPanel.CellBox(light)); break;
                case CheckBox or CheckButton: Check((Button)control); break;
                case OptionButton option: Action(option); ClassicProgressionPanel.StylePopup(option.GetPopup()); option.Alignment = HorizontalAlignment.Left; break;
                case Button button: StyleButton(button); break;
                case Label label: StyleLabel(label); break;
                case LineEdit edit: ClassicVendorSkin.Input(edit); edit.AddThemeFontOverride("font", Plugin.Kit.Bold); break;
                case HSeparator rule: Rule(rule); break;
                case VSeparator rule: Rule(rule); break;
                case ScrollContainer scroll: Scroll(scroll); break;
                case ScrollBar bar when bar.GetParent() is not ScrollContainer: ScrollBar(bar); break;
                case RichTextLabel rich: Rich(rich); break;
                case ProgressBar bar: Gauge(bar); break;
                case StatBar stat: ClassicFamilyGauge.Replace(stat); break;
                case ItemList list: List(list); break;
                case PanelContainer panel: Panel(panel); break;
            }
        }
    }

    private static IEnumerable<Node> Tree(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren())
            foreach (var node in Tree(child)) yield return node;
    }

    private static void StyleLabel(Label label)
    {
        int size = label.GetThemeFontSize("font_size");
        // Large display figures keep their size; body text uses the event windows' 11 to 13 pixel Arial Bold.
        size = size >= 18 ? size : Math.Clamp(size, Spec.TinyFont, Spec.FontSize);
        var ink = Ink(label.GetThemeColor("font_color"));
        bool outlined = label.GetThemeConstant("outline_size") > 0;
        var alignment = label.VerticalAlignment;
        ClassicEventPanel.Bold(label, ink, size);
        label.VerticalAlignment = alignment;
        if (outlined)
        {
            label.AddThemeConstantOverride("outline_size", 3);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .85f));
        }
        label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
    }

    private static void StyleButton(Button button)
    {
        // A flat button is a clickable strip around its own labels (a collapsible board title), not a service button.
        if (button.Flat) { button.FocusMode = Control.FocusModeEnum.None; return; }
        if (button.Text.Length == 0 && button.Icon != null)
        {
            // Icon buttons keep their glyph in the caption gold without a modern chip.
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" }) button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            foreach (var state in new[] { "icon_normal_color", "icon_focus_color" }) button.AddThemeColorOverride(state, ClassicEventPanel.Caption);
            foreach (var state in new[] { "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" }) button.AddThemeColorOverride(state, new Color("ffe094"));
            return;
        }
        Action(button);
    }

    /// <summary>The original check socket with its caption in the family's Arial Bold and light ink (both nations).</summary>
    private static void Check(Button check)
    {
        ClassicProgressionPanel.StyleCheck(check);
        ClassicEventPanel.Bold(check, null, Spec.SmallFont);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            check.AddThemeColorOverride(state, ClassicDarkFrame.Value);
    }

    /// <summary>The event windows' service button: original art per state, Arial Bold in the button ink.</summary>
    public static void Action(Button button)
    {
        ClassicVendorSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!);
        ClassicEventPanel.Bold(button, null, Spec.SmallFont);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, new Color("efd9b4"));
        button.AddThemeColorOverride("font_disabled_color", ClassicDesign.Muted);
        button.FocusMode = Control.FocusModeEnum.None;
        // The caption decides the width (never clipped), at least the width of the original small buttons.
        button.ClipText = false; button.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
        float caption = Plugin.Kit.Bold.GetStringSize(button.Text, HorizontalAlignment.Left, -1, Spec.SmallFont).X + 2 * Spec.ButtonPadding;
        // Glyph buttons (one or two characters: arrows, plus, minus) stay square; captions get the small-button width.
        float least = button.Text.Trim().Length <= 2 ? Spec.Button : Spec.ButtonMinWidth;
        button.CustomMinimumSize = new Vector2(Math.Max(button.CustomMinimumSize.X, Math.Max(least, Mathf.Ceil(caption))),
            Math.Max(button.CustomMinimumSize.Y, Spec.Button));
        // A toggle shows its chosen state with the pressed art, as the original tab-like buttons do.
        if (button.ToggleMode) button.AddThemeColorOverride("font_pressed_color", new Color("ffe094"));
    }

    private static void Rule(Separator rule)
    {
        var line = new StyleBoxLine { Color = new Color("6d5c3d"), Thickness = 1, Vertical = rule is VSeparator };
        rule.AddThemeStyleboxOverride("separator", line);
    }

    public static void Scroll(ScrollContainer scroll)
    {
        ScrollBar(scroll.GetVScrollBar());
        ScrollBar(scroll.GetHScrollBar());
        scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        scroll.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>The event windows' scroll bar: the shared dark-frame bar (<see cref="ClassicDarkFrame.ScrollBar"/>).</summary>
    public static void ScrollBar(ScrollBar bar) => ClassicDarkFrame.ScrollBar(bar);

    private static void Rich(RichTextLabel rich)
    {
        foreach (var font in new[] { "normal_font", "bold_font" }) rich.AddThemeFontOverride(font, Plugin.Kit.Bold);
        int size = rich.GetThemeFontSize("normal_font_size");
        rich.AddThemeFontSizeOverride("normal_font_size", Math.Clamp(size, Spec.TinyFont, Spec.FontSize));
        rich.AddThemeFontSizeOverride("bold_font_size", Math.Clamp(size, Spec.TinyFont, Spec.FontSize));
        rich.AddThemeColorOverride("default_color", Ink(rich.GetThemeColor("default_color")));
        rich.AddThemeColorOverride("font_shadow_color", Colors.Black);
        rich.AddThemeConstantOverride("shadow_offset_x", 1); rich.AddThemeConstantOverride("shadow_offset_y", 1);
        rich.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
    }

    private static void Gauge(ProgressBar bar)
    {
        var art = Plugin.Kit.Layout("{nation}_statebar_us").Find("Progress_ExpP");
        StyleBox? Part(int tag)
        {
            var image = art?.Images.FirstOrDefault(i => i.Tag == tag);
            if (image?.Texture == null) return null;
            var box = new StyleBoxTexture { Texture = Plugin.Kit.Texture(image.Texture), RegionRect = new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH) };
            box.SetTextureMargin(Side.Left, 3); box.SetTextureMargin(Side.Right, 3); box.SetContentMarginAll(0);
            return box;
        }
        if (Part(0) is { } under) bar.AddThemeStyleboxOverride("background", under);
        if (Part(1) is { } fill) bar.AddThemeStyleboxOverride("fill", fill);
        bar.AddThemeFontOverride("font", Plugin.Kit.Bold);
        bar.AddThemeFontSizeOverride("font_size", Spec.TinyFont);
        bar.AddThemeColorOverride("font_color", ClassicEventPanel.Ink);
    }

    private static void List(ItemList list)
    {
        var panel = ClassicDarkFrame.InputBox(); panel.BgColor = Colors.Black; panel.SetContentMarginAll(3);
        list.AddThemeStyleboxOverride("panel", panel); list.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var selected = new StyleBoxFlat { BgColor = ClassicDarkFrame.SelectedRow };
        foreach (var state in new[] { "selected", "selected_focus" }) list.AddThemeStyleboxOverride(state, selected);
        list.AddThemeFontOverride("font", Plugin.Kit.Bold); list.AddThemeFontSizeOverride("font_size", Spec.SmallFont);
        list.AddThemeColorOverride("font_color", ClassicEventPanel.Ink); list.AddThemeColorOverride("font_selected_color", new Color("ffe094"));
    }

    /// <summary>
    /// Client panels become Classic: bordered sections the original quest inset, rows a transparent band with
    /// the Classic selection colour, chips and header bands plain. Content margins stay the client's.
    /// </summary>
    private static void Panel(PanelContainer panel)
    {
        if (panel.GetThemeStylebox("panel") is not StyleBoxFlat flat) return;
        var margins = (L: flat.ContentMarginLeft, T: flat.ContentMarginTop, R: flat.ContentMarginRight, B: flat.ContentMarginBottom);
        StyleBox box;
        bool bordered = flat.BorderWidthTop > 0 && flat.BorderColor.A > .2f;
        // Sections and panels (even padding) take the inset; rows (wider than tall padding) and round chips do not.
        bool section = bordered && margins.L == margins.T && margins.L >= 6 && flat.CornerRadiusTopLeft < 8;
        bool selected = bordered && Near(flat.BorderColor, UiTheme.Gold);
        if (section && !selected)
        {
            var inset = ClassicNpcLayout.InsetBox();
            box = inset;
        }
        else if (selected)
            box = new StyleBoxFlat { BgColor = ClassicDarkFrame.SelectedRow };
        else
            box = new StyleBoxFlat { BgColor = Colors.Transparent };
        box.ContentMarginLeft = Math.Max(0, margins.L); box.ContentMarginTop = Math.Max(0, margins.T);
        box.ContentMarginRight = Math.Max(0, margins.R); box.ContentMarginBottom = Math.Max(0, margins.B);
        panel.AddThemeStyleboxOverride("panel", box);
    }
}

/// <summary>
/// The original message-box progress bar (groove and red fill) in place of a client stat bar. The stat bar stays
/// in the tree, hidden, and keeps receiving the client's updates; the gauge mirrors its fraction every frame.
/// </summary>
public partial class ClassicFamilyGauge : ProgressBar
{
    public const float Height = 9;
    private readonly StatBar _source;
    public ClassicFamilyGauge() { _source = null!; }

    private ClassicFamilyGauge(StatBar source)
    {
        _source = source; Name = "classic_gauge"; MouseFilter = MouseFilterEnum.Ignore;
        MinValue = 0; MaxValue = 1; Step = 0; ShowPercentage = false;
        CustomMinimumSize = new Vector2(source.CustomMinimumSize.X, Height);
        SizeFlagsHorizontal = source.SizeFlagsHorizontal; SizeFlagsVertical = SizeFlags.ShrinkCenter;
        ClassicWorldOverlays.ProgressBar(this, urgent: true);
        ShownOnly.Gate(this, Sync);
    }

    public static void Replace(StatBar source)
    {
        if (source.GetParent() is not { } parent || parent.HasNode("classic_gauge_" + source.GetInstanceId())) return;
        var gauge = new ClassicFamilyGauge(source) { Name = "classic_gauge_" + source.GetInstanceId() };
        parent.AddChild(gauge);
        parent.MoveChild(gauge, source.GetIndex() + 1);
        source.Visible = false;
        gauge.Sync();
    }

    public override void _Process(double delta) => Sync();

    private void Sync()
    {
        if (_source == null || !GodotObject.IsInstanceValid(_source)) return;
        float shown = NativeApi.Get<float>(_source, "frac");
        if (Math.Abs(Value - shown) > .0001f) Value = shown;
    }
}

/// <summary>Shows the Classic hotbar arrow on the client's minimize button (up: minimize, down: restore).</summary>
public partial class ClassicFamilyToggle : Node
{
    private readonly Button _button;
    private bool? _down;
    public ClassicFamilyToggle() { _button = null!; }
    public ClassicFamilyToggle(Button button) { _button = button; ShownOnly.Gate(this, button, Sync); }
    public override void _Process(double delta) => Sync();
    private void Sync()
    {
        if (_button == null || !GodotObject.IsInstanceValid(_button)) return;
        if (_button.Text.Length > 0) _button.Text = "";
        bool down = _button.TooltipText == "Restore";
        if (_down == down) return;
        _down = down;
        var art = Plugin.Kit.Layout("{nation}_hotkey_us").Find(down ? "btn_down" : "btn_up")!;
        ClassicChatControls.SkinButton(_button, art);
    }
}

/// <summary>
/// Keeps a client item cell on the Classic inventory cell. The client repaints the cell's frame on hover and
/// on every item change; this restores the Classic frame (lit while hovered) and the shared count style.
/// </summary>
public partial class ClassicSlotLook : Node
{
    private readonly ItemSlotView _view;
    private readonly StyleBox _normal, _hover;
    private bool _over;

    public ClassicSlotLook() { _view = null!; _normal = _hover = null!; }

    private ClassicSlotLook(ItemSlotView view, Func<float, StyleBox> box)
    {
        _view = view; _normal = box(1); _hover = box(1.3f);
        Name = "classic_slot_look";
        view.MouseEntered += () => _over = true;
        view.MouseExited += () => _over = false;
        ShownOnly.Gate(this, view, Apply);
        foreach (var count in view.GetChildren().OfType<Label>().Where(l => l is not UpgradeBadge)) ItemCountStyle.Apply(count, view, Plugin.Kit.Bold);
    }

    public static void Attach(ItemSlotView view, Func<float, StyleBox> box)
    {
        if (view.HasNode("classic_slot_look")) return;
        var look = new ClassicSlotLook(view, box);
        view.AddChild(look);
        look.Apply();
    }

    public override void _Process(double delta) => Apply();

    private void Apply()
    {
        if (_view == null || !GodotObject.IsInstanceValid(_view)) return;
        var wanted = _over ? _hover : _normal;
        if (_view.GetThemeStylebox(ThemeNames.Panel) != wanted) _view.AddThemeStyleboxOverride(ThemeNames.Panel, wanted);
    }
}
