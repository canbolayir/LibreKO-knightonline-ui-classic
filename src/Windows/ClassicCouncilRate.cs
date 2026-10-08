using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicCouncilRate
{
    public static readonly string[] WindowIds = { "nationtaxrate", "siegetaxrate" };
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicCouncilRatePanel? Apply(Control body)
    {
        Node? root = body;
        while (root != null && root is not HudWindow) root = root.GetParent();
        if (root is not HudWindow window || window.HasMeta("classic_council_rate")) return null;
        if (window.GetMeta("classic_council_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_council_rate", true);
        var panel = new ClassicCouncilRatePanel(window, body); window.AddChild(panel); window.ResetSize(); return panel;
    }
}

public partial class ClassicCouncilRatePanel : Control
{
    public HudWindow Window { get; }
    private readonly LayoutNode _art;
    private readonly Button _accept, _cancel, _down, _up;
    public ClassicCouncilRatePanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_council_rate"; Size = CustomMinimumSize = new Vector2(271, 105);
        TextureFilter = TextureFilterEnum.Nearest;
        _art = Plugin.Kit.Layout(window.Id == "nationtaxrate" ? "co_nationtaxrate_us" : "co_warfaretaxrate_us");
        var controls = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => controls.OfType<T>().Single(c => c.Name == name);
        var bar = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        ClassicPartySkin.Drag(this, bar, new Rect2(3, 2, 265, 9));
        Place(Find<Label>("taxrate_prompt"), "text_message");
        var prompt = Find<Label>("taxrate_prompt"); prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        prompt.ClipText = false; prompt.VerticalAlignment = VerticalAlignment.Center;
        var value = Find<Label>("taxrate_value"); Place(value, "text_money");
        if (window.Id == "nationtaxrate")
        {
            // Native text includes the percent unit; center the combined value between the original arrows.
            var combined = new Rect2(93, 45, 84, 18); value.Position = combined.Position; value.Size = combined.Size;
            Callable.From(() => value.Size = combined.Size).CallDeferred();
            value.SetMeta("council_expected_rect", combined);
        }
        _accept = Find<Button>("taxrate_accept"); _cancel = Find<Button>("taxrate_cancel");
        _down = Find<Button>("taxrate_down"); _up = Find<Button>("taxrate_up");
        Button(_accept, "btn_ok", "OK"); Button(_cancel, "btn_cancel", "Cancel");
        Button(_down, "btn_down", ""); Button(_up, "btn_up", "");
    }
    private void Place(Control control, string field)
    {
        var source = _art.Find(field)!; var rect = new Rect2(source.Position, source.SizeVec);
        ClassicVendorSkin.Move(control, this, rect);
        control.AddThemeFontOverride("font", source.Bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        control.AddThemeFontSizeOverride("font_size", UiKit.FontSize(source));
        control.AddThemeColorOverride("font_color", source.Color);
        control.SetMeta("council_expected_rect", rect);
    }
    private void Button(Button button, string field, string text)
    {
        var source = _art.Find(field)!; var rect = new Rect2(source.Position, source.SizeVec);
        ClassicMerchantSkin.Button(button, source, this, rect, text);
        button.AddThemeFontOverride("font", Plugin.Kit.Bold); button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_shadow_color", Colors.Black);
        button.AddThemeConstantOverride("shadow_offset_x", 1); button.AddThemeConstantOverride("shadow_offset_y", 1);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" }) button.GetThemeStylebox(state).SetContentMarginAll(0);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, new Color("ffffc6"));
        button.SetMeta("council_expected_rect", rect);
    }
    public override void _Process(double delta)
    {
        bool pending = Window.GetMeta("council_rate_pending", false).AsBool();
        _accept.Disabled = _down.Disabled = _up.Disabled = pending;
        _accept.SelfModulate = _down.SelfModulate = _up.SelfModulate = pending ? new Color(.5f, .5f, .5f) : Colors.White;
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape) _cancel.EmitSignal(BaseButton.SignalName.Pressed);
        else if (key.Keycode is Key.Enter or Key.KpEnter) { if (!_accept.Disabled) _accept.EmitSignal(BaseButton.SignalName.Pressed); }
        else return;
        GetViewport().SetInputAsHandled();
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(3, 3, 265, 99), Colors.Black);
        foreach (var part in _art.Children.Where(n => n.IsImage))
            DrawTextureRectRegion(Plugin.Kit.Texture(part.Texture!), new Rect2(part.Position, part.SizeVec), new Rect2(part.SrcX, part.SrcY, part.SrcW, part.SrcH));
    }
}
