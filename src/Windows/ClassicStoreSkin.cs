using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

public static class ClassicStoreSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicStorePanel? Apply(Control body)
    {
        Node? root = body; while (root != null && root is not HudWindow) root = root.GetParent();
        if (root is not HudWindow window || window.HasMeta("classic_store") || window.GetMeta("classic_store_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_store", true); var panel = new ClassicStorePanel(window, body); window.AddChild(panel);
        var overlay = window.GetChildren().OfType<Control>().Single(c => c.Name == "pus_overlay"); window.MoveChild(overlay, window.GetChildCount() - 1);
        window.ResetSize(); return panel;
    }
}

public partial class ClassicStorePanel : Control
{
    public HudWindow Window { get; }
    private readonly Control _body, _overlay, _grip;
    private readonly Label _title;
    private readonly Button _close;
    private readonly PanelContainer[] _modals;
    public ClassicStorePanel(HudWindow window, Control body)
    {
        Window = window; _body = body; Name = "classic_store"; TextureFilter = TextureFilterEnum.Nearest;
        var theme = new Theme { DefaultFont = Plugin.Kit.Bold, DefaultFontSize = 13 };
        foreach (bool featured in new[] { false, true }) foreach (bool active in new[] { false, true })
        {
            var box = ClassicStoreLayout.Box(8, active); box.ContentMarginTop = 26;
            if (featured) box.BorderColor = new Color("bd9b59");
            theme.SetStylebox("pus_card_" + (featured ? "featured_" : "") + (active ? "active" : "normal"), "PanelContainer", box);
        }
        theme.SetStylebox("pus_contact_normal", "PanelContainer", ClassicStoreLayout.Box(6));
        theme.SetStylebox("pus_contact_active", "PanelContainer", ClassicStoreLayout.Box(6, true));
        Theme = theme; window.Theme = theme;
        _overlay = CharacterDetailsSkin.Tree(window).OfType<Control>().Single(c => c.Name == "pus_overlay");
        _modals = CharacterDetailsSkin.Tree(_overlay).OfType<PanelContainer>().Where(c => c.Name.ToString() is "pus_details" or "pus_gift_box").ToArray();
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        _title = header.GetChildren().OfType<Label>().Last(); _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>().Where(c => c != _overlay)) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        body.Reparent(this); body.Visible = true;
        _title.Reparent(this); _title.MouseFilter = MouseFilterEnum.Ignore; _title.CustomMinimumSize = Vector2.Zero;
        _title.VerticalAlignment = VerticalAlignment.Center; _title.AddThemeColorOverride("font_color", new Color("ead7a6")); Font(_title, 13);
        _title.AddThemeColorOverride("font_shadow_color", Colors.Black); _title.AddThemeConstantOverride("shadow_offset_x", 1); _title.AddThemeConstantOverride("shadow_offset_y", 1);
        ClassicPartySkin.Drag(this, header, new Rect2(10, 4, 700, 31)); _grip = GetChildren().OfType<Control>().Single(c => c.Name == "party_drag");
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_shoppingmall_us").Find("btn_close")!, this, ClassicStoreLayout.Close(1024), "");
        _close.FocusMode = FocusModeEnum.All;
        var description = CharacterDetailsSkin.Tree(_overlay).OfType<Label>().Single(c => c.Name == "pus_details_description");
        var parent = description.GetParent(); int index = description.GetIndex();
        var scroll = new ScrollContainer { Name = "pus_description_scroll", CustomMinimumSize = new Vector2(0, 84), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = description.Visible };
        parent.AddChild(scroll); parent.MoveChild(scroll, index); description.Reparent(scroll); description.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        description.VisibilityChanged += () => scroll.Visible = description.Visible;
        Watch(body); Watch(_overlay);
        foreach (var modal in _modals)
        {
            modal.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            modal.AddChild(new ClassicFrame { Name = "pus_modal_frame", BackgroundAlpha = 1, BackgroundColor = Colors.Black, ShowBehindParent = true });
        }
        body.MinimumSizeChanged += UpdateMinimumSize; Resized += Arrange;
        window.VisibilityChanged += () => { if (!window.Visible) foreach (var option in CharacterDetailsSkin.Tree(this).Concat(CharacterDetailsSkin.Tree(_overlay)).OfType<OptionButton>()) option.GetPopup().Hide(); };
    }
    public override Vector2 _GetMinimumSize() => (_body?.GetCombinedMinimumSize() ?? Vector2.Zero) + ClassicStoreLayout.Chrome;
    public override void _Ready() => Arrange();
    private void Arrange()
    {
        var rect = ClassicStoreLayout.Body(Size); _body.Position = rect.Position; _body.Size = rect.Size;
        _close.Position = ClassicStoreLayout.Close(Size.X).Position; _close.Size = ClassicStoreLayout.Close(Size.X).Size;
        _title.Position = new Vector2(22, 9); _title.Size = new Vector2(Size.X - 190, 24);
        _grip.Size = new Vector2(Size.X - 174, 31); QueueRedraw();
    }
    private static void Font(Control control, int size = 13)
    {
        control.AddThemeFontOverride("font", Plugin.Kit.Bold); control.AddThemeFontSizeOverride("font_size", size); control.AddThemeConstantOverride("outline_size", 0);
    }
    private void Watch(Node node)
    {
        if (node.HasMeta("classic_store_control")) return; node.SetMeta("classic_store_control", true);
        Style(node); foreach (var child in node.GetChildren()) Watch(child); node.ChildEnteredTree += Watch;
    }
    private void Style(Node node)
    {
        if (node is not Control control) return;
        int size = control is Label l ? Math.Clamp(l.GetThemeFontSize("font_size"), 11, 13) : 13;
        if (control.Name.ToString() is "pus_details_name" or "pus_recipient_name") size = 16;
        Font(control, size);
        if (control is LineEdit edit)
        {
            ClassicVendorSkin.Input(edit); Font(edit); edit.TextDirection = TextDirection.Ltr; edit.KeepEditingOnTextSubmit = true;
            edit.GuiInput += ev => { if (ev is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { Escape(); edit.AcceptEvent(); } };
        }
        if (control is Button button)
        {
            if (button.TooltipText is "Close" or "Remove from the cart" or "Buy for yourself instead")
            {
                button.Icon = null; button.Text = ""; button.CustomMinimumSize = Platform.TouchUi ? new Vector2(40, 40) : new Vector2(24, 24);
                ClassicVendorSkin.Button(button, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!);
            }
            else
            {
                var icon = button.Icon;
                ClassicReportDesign.StyleButton(button, false, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!); button.Icon = icon;
                foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, new Color("ead7a6"));
                button.AddThemeColorOverride("font_disabled_color", new Color("a0937c"));
                if (button.HasMeta("pus_category_selected")) button.TooltipText = button.Text + (button.TooltipText.Length > 0 ? "\n" + button.TooltipText : "");
                if (button.HasMeta("pus_category_selected") && button.GetMeta("pus_category_selected").AsBool()) button.AddThemeStyleboxOverride("normal", button.GetThemeStylebox("pressed"));
                foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
                {
                    var box = (StyleBox)button.GetThemeStylebox(state).Duplicate(); box.ContentMarginLeft = 6; box.ContentMarginRight = 6; button.AddThemeStyleboxOverride(state, box);
                }
                float height = Platform.TouchUi ? Math.Max(40, button.CustomMinimumSize.Y) : button.TooltipText is "One more" or "One less" ? 24 : 28;
                button.CustomMinimumSize = new Vector2(button.CustomMinimumSize.X, height);
            }
            Font(button); button.FocusMode = FocusModeEnum.All;
            if (button is OptionButton option)
            {
                var popup = option.GetPopup(); popup.AddThemeFontOverride("font", Plugin.Kit.Bold); popup.AddThemeFontSizeOverride("font_size", 13);
                popup.AddThemeColorOverride("font_color", ClassicDesign.Text); popup.AddThemeStyleboxOverride("panel", ClassicStoreLayout.Box(4)); popup.AddThemeStyleboxOverride("hover", ClassicStoreLayout.Box(4, true));
            }
        }
        if (control is ScrollContainer sc)
        {
            if (sc.GetParent().Name == "pus_gift_pick") sc.CustomMinimumSize = new Vector2(0, Platform.TouchUi ? 240 : 160);
            var bar = sc.GetVScrollBar(); bar.CustomMinimumSize = new Vector2(10, 0); bar.AddThemeStyleboxOverride("scroll", ClassicStoreLayout.Box(0));
            foreach (string state in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
            {
                var box = ClassicDesign.ButtonBox(state == "grabber" ? "normal" : state == "grabber_highlight" ? "hover" : "pressed"); box.SetContentMarginAll(0); bar.AddThemeStyleboxOverride(state, box);
            }
        }
        if (control is PanelContainer panel)
        {
            if (panel.HasMeta("pus_card_id"))
            {
                string style = "pus_card_" + (panel.GetMeta("pus_card_featured", false).AsBool() ? "featured_" : "") + (panel.GetMeta("pus_card_active", false).AsBool() ? "active" : "normal");
                panel.AddThemeStyleboxOverride("panel", panel.GetThemeStylebox(style)); return;
            }
            if (panel.Name.ToString() is "pus_card_icon" || panel.GetChildren().OfType<TextureRect>().Any(t => t.Name == "pus_details_icon")) panel.AddThemeStyleboxOverride("panel", ClassicStoreLayout.IconBox());
            else if (panel.Name.ToString() is "pus_categories" or "pus_cart" or "pus_gift_banner" or "pus_gift_card" || panel.Name.ToString().StartsWith("pus_cart_line_")) panel.AddThemeStyleboxOverride("panel", ClassicStoreLayout.Box());
            else if (panel.GetThemeStylebox("panel") is StyleBoxFlat flat)
            {
                var box = (StyleBoxFlat)flat.Duplicate(); box.SetCornerRadiusAll(0); box.ShadowSize = 0; panel.AddThemeStyleboxOverride("panel", box);
            }
        }
    }
    private void Escape()
    {
        var option = CharacterDetailsSkin.Tree(this).Concat(CharacterDetailsSkin.Tree(_overlay)).OfType<OptionButton>().FirstOrDefault(o => o.GetPopup().Visible);
        if (option != null) { option.GetPopup().Hide(); return; }
        var modal = _modals.FirstOrDefault(m => m.Visible && _overlay.Visible);
        if (modal != null) CharacterDetailsSkin.Tree(modal).OfType<Button>().Single(b => b.TooltipText == "Close").EmitSignal(BaseButton.SignalName.Pressed);
        else _close.EmitSignal(BaseButton.SignalName.Pressed);
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        Escape(); GetViewport().SetInputAsHandled();
    }
    public override void _Draw()
    {
        var texture = Plugin.Kit.Texture("ui_message_us.png"); if (texture == null || Size.X < 400) return;
        DrawTextureRectRegion(texture, new Rect2(Vector2.Zero, Size), new Rect2(45, 122, 37, 34));
        DrawRect(new Rect2(1, 39, Size.X - 2, Size.Y - 40), Colors.Black);
        DrawTextureRectRegion(texture, new Rect2(0, 0, 328, 39), new Rect2(0, 80, 328, 39));
        for (float x = 327; x < Size.X - 43; x += 327)
        {
            float width = Math.Min(328, Size.X - 43 - x); int y = ((int)x / 327) % 2 == 1 ? 40 : 0;
            DrawTextureRectRegion(texture, new Rect2(x, 0, width, 39), new Rect2(0, y, width, 39));
        }
        DrawTextureRectRegion(texture, new Rect2(Size.X - 43, 0, 43, 39), new Rect2(0, 120, 43, 39));
        DrawTextureRectRegion(texture, ClassicStoreLayout.CloseBacking(Size.X), new Rect2(0, 161, 133, 30));
    }
}
