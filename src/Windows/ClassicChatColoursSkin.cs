using Godot;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicChatColoursSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicChatColoursPanel? Apply(Control body)
    {
        Node? root = body; while (root != null && root is not HudWindow) root = root.GetParent();
        if (root is not HudWindow window || window.HasMeta("classic_chat_colours") || window.GetMeta("classic_chat_colours_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_chat_colours", true);
        var panel = new ClassicChatColoursPanel(window, body); window.AddChild(panel); window.ResetSize(); return panel;
    }
}

public partial class ClassicChatColoursPanel : Control
{
    public HudWindow Window { get; }
    private readonly Button _close;
    private readonly PopupPanel _palette;
    public ClassicChatColoursPanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_chat_colours"; Size = CustomMinimumSize = ClassicChatColoursLayout.Size; TextureFilter = TextureFilterEnum.Nearest;
        var native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => native.OfType<T>().Single(c => c.Name == name);
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        var title = header.GetChildren().OfType<Label>().Last(); _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        _palette = window.GetChildren().OfType<PopupPanel>().Single();
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black, ShowBehindParent = true });
        ClassicPartySkin.Drag(this, header, new Rect2(14, 8, 296, 32));
        Place(title, new Rect2(22, 16, 276, 24)); title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, new Rect2(320, 15, 24, 24), "");
        _close.SetMeta("chat_colours_expected_rect", new Rect2(320, 15, 24, 24));
        for (int slot = 0; slot < ChatColors.SlotCount; slot++)
        {
            var label = Find<Label>("chat_colour_label_" + slot); Place(label, ClassicChatColoursLayout.Label(slot));
            label.TooltipText = label.Text;
            var pick = Find<Button>("chat_colour_pick_" + slot); Action(pick, ClassicChatColoursLayout.Pick(slot));
            pick.TooltipText = "Choose " + label.Text + " colour";
            var swatch = Find<ColorRect>("chat_colour_swatch_" + slot); swatch.CustomMinimumSize = Vector2.Zero;
            swatch.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); swatch.Position = new Vector2(19, 4); swatch.Size = new Vector2(36, 16);
            Callable.From(() => swatch.Size = new Vector2(36, 16)).CallDeferred();
            swatch.SetMeta("chat_colours_expected_rect", new Rect2(19, 4, 36, 16));
        }
        Action(Find<Button>("chat_colour_default"), ClassicChatColoursLayout.Default);
        Action(Find<Button>("chat_colour_apply"), ClassicChatColoursLayout.Apply);
        var popupBox = ClassicDesign.InputBox(); popupBox.BgColor = Colors.Black; popupBox.SetContentMarginAll(6); popupBox.SetBorderWidthAll(1);
        _palette.AddThemeStyleboxOverride("panel", popupBox);
        _palette.AddChild(new ClassicChatColoursPaletteInput(_palette));
        foreach (var pick in _palette.GetChild(0).GetChildren().OfType<Button>())
        {
            pick.FocusMode = FocusModeEnum.All; var colour = pick.GetMeta("chat_palette_colour").AsColor();
            pick.CustomMinimumSize = new Vector2(24, 24); pick.TooltipText = "#" + colour.ToHtml(false).ToUpperInvariant();
            foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
            {
                var box = new StyleBoxFlat { BgColor = colour, BorderColor = state == "normal" ? new Color("76674e") : new Color("ffe3a0") };
                box.SetBorderWidthAll(state == "normal" ? 1 : 2); box.SetContentMarginAll(0); pick.AddThemeStyleboxOverride(state, box);
            }
        }
    }
    private static void Font(Control control)
    {
        control.AddThemeFontOverride("font", Plugin.Kit.Bold); control.AddThemeFontSizeOverride("font_size", 13); control.AddThemeConstantOverride("outline_size", 0);
    }
    private void Place(Control control, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, this, rect); Font(control); control.SetMeta("chat_colours_expected_rect", rect);
        if (control is Label label)
        {
            label.ClipText = label.ClipContents = true; label.CustomMaximumSize = rect.Size; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.AddThemeColorOverride("font_color", ClassicDesign.Text);
        }
    }
    private void Action(Button button, Rect2 rect)
    {
        ClassicMerchantSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!, this, rect, button.Text);
        Font(button); button.Icon = null; button.FocusMode = FocusModeEnum.All; button.SetMeta("chat_colours_expected_rect", rect);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, new Color("efd9b4"));
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (_palette.Visible) _palette.Hide(); else _close.EmitSignal(BaseButton.SignalName.Pressed);
        GetViewport().SetInputAsHandled();
    }
    public override void _Draw() => DrawStyleBox(ClassicDesign.SectionBox(0), ClassicChatColoursLayout.Rows);
}

public partial class ClassicChatColoursPaletteInput : Node
{
    private readonly PopupPanel _palette;
    public ClassicChatColoursPaletteInput(PopupPanel palette) => _palette = palette;
    public override void _Input(InputEvent ev)
    {
        if (!_palette.Visible || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        _palette.Hide(); GetViewport().SetInputAsHandled();
    }
}
