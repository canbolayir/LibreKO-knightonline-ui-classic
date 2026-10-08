using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicCapeSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static void Apply(Control body)
    {
        Node? owner = body; while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_cape") || !window.HasMeta("classic_cape_controls")) return;
        window.SetMeta("classic_cape", true); window.AddChild(new ClassicCapePanel(window, body)); window.ResetSize();
    }
}

public partial class ClassicCapePanel : Control
{
    private readonly Button _cancel;
    public ClassicCapePanel(HudWindow window, Control body)
    {
        Name = "classic_cape"; Size = CustomMinimumSize = ClassicCapeLayout.Size; TextureFilter = TextureFilterEnum.Nearest;
        var controls = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => controls.OfType<T>().Single(c => c.Name == name);
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var reference = Plugin.Kit.Layout("co_knightsmantleshop_us");
        var background = reference.Images.First();
        AddChild(new TextureRect { Texture = ClassicChatControls.Atlas(background), Size = new Vector2(373, 468),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore });
        var extension = new Panel { Position = ClassicCapeLayout.Extension.Position, Size = ClassicCapeLayout.Extension.Size, MouseFilter = MouseFilterEnum.Ignore };
        var border = new StyleBoxFlat { BgColor = Colors.Black, BorderColor = new Color("b9ab91") };
        border.SetBorderWidthAll(1); extension.AddThemeStyleboxOverride("panel", border); AddChild(extension);
        ClassicPartySkin.Drag(this, header, new Rect2(3, 2, 365, 14));
        ClassicPartySkin.Drag(this, header, new Rect2(383, 2, 208, 36));
        Place(Find<LookPreview>("cape_preview"), ClassicCapeLayout.Preview, false);
        for (int slot = 0; slot < 4; slot++) Swatch(Find<Button>("cape_pattern_" + slot), ClassicCapeLayout.Pattern(slot));
        for (int slot = 0; slot < 6; slot++) Swatch(Find<Button>("cape_colour_" + slot), ClassicCapeLayout.Colour(slot));
        Action("cape_turn_left", "btn_left", ""); Action("cape_turn_right", "btn_right", "");
        Action("cape_pattern_previous", "btn_patterndown", ""); Action("cape_pattern_next", "btn_patternup", "");
        Action("cape_colour_previous", "btn_colordown", ""); Action("cape_colour_next", "btn_colorup", "");
        Place(Find<Label>("cape_pattern_page"), new Rect2(250, 253, 52, 19));
        Place(Find<Label>("cape_colour_page"), new Rect2(64, 401, 65, 19));
        foreach (string id in new[] { "cape_pattern_page", "cape_colour_page" })
            Find<Label>(id).HorizontalAlignment = HorizontalAlignment.Center;
        Place(Find<Label>("cape_chosen"), ClassicCapeLayout.Chosen);
        Place(Find<Label>("cape_requirement"), ClassicCapeLayout.Requirement);
        Place(Find<Label>("cape_price"), ClassicCapeLayout.Price);
        foreach (string id in new[] { "cape_chosen", "cape_requirement", "cape_price" })
        {
            var label = Find<Label>(id); label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.ClipText = false;
            label.CustomMaximumSize = label.Size; label.ClipContents = true;
        }
        Find<Label>("cape_price").AddThemeColorOverride("font_color", new Color("e4c58b"));
        Caption("Clan Cape", new Rect2(20, 275, 167, 18));
        Caption("Custom dye", new Rect2(395, 14, 184, 22));
        Caption("Preview a colour before applying it.", new Rect2(395, 50, 184, 44));
        int row = 0;
        foreach (string channel in new[] { "R", "G", "B" })
        {
            Caption(channel, new Rect2(395, 107 + row * 36, 18, 24));
            var slider = Find<HSlider>("cape_dye_" + channel); Place(slider, ClassicCapeLayout.Slider(row));
            var track = new StyleBoxFlat { BgColor = new Color("211c15"), BorderColor = new Color("82735b") }; track.SetBorderWidthAll(1);
            slider.AddThemeStyleboxOverride("slider", track);
            slider.AddThemeColorOverride("grabber_area_color", new Color("c4ad82"));
            Place(Find<Label>("cape_value_" + channel), ClassicCapeLayout.Value(row));
            Find<Label>("cape_value_" + channel).HorizontalAlignment = HorizontalAlignment.Right; row++;
        }
        var ticket = Find<CheckBox>("cape_ticket"); Place(ticket, ClassicCapeLayout.Ticket);
        ticket.Text = "Castellan ticket";
        var checkbox = Plugin.Kit.Layout("el_chat_us").Find("btn_check_normal")!;
        foreach (string state in new[] { "unchecked", "checked", "unchecked_disabled", "checked_disabled" })
            ticket.AddThemeIconOverride(state, ClassicChatControls.Atlas(checkbox.Images.First(i => i.Tag == (state.StartsWith("unchecked") ? 0 : 1))));
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" }) ticket.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        Place(Find<Label>("cape_hint"), ClassicCapeLayout.Hint);
        Place(Find<Label>("cape_status"), ClassicCapeLayout.Status);
        Action("cape_buy", "btn_purchase", "Buy"); _cancel = Find<Button>("cape_cancel"); Action("cape_cancel", "btn_cancel", "Cancel");
        foreach (int y in new[] { 98, 218, 282, 374 }) AddChild(new ColorRect { Position = new Vector2(395, y), Size = new Vector2(184, 1), Color = new Color("6a5d48"), MouseFilter = MouseFilterEnum.Ignore });

        void Action(string id, string artId, string text)
        {
            var button = Find<Button>(id); var art = reference.Find(artId)!;
            ClassicMerchantSkin.Button(button, art, this, new Rect2(art.Position, art.SizeVec), text);
            Place(button, new Rect2(art.Position, art.SizeVec));
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, new Color("271c12"));
            button.AddThemeColorOverride("font_disabled_color", new Color("6d6356"));
        }
    }
    private void Place(Control control, Rect2 rect, bool font = true)
    {
        ClassicVendorSkin.Move(control, this, rect); control.SetMeta("cape_expected_rect", rect);
        if (font) { control.AddThemeFontOverride("font", Plugin.Kit.Bold); control.AddThemeFontSizeOverride("font_size", 13); control.AddThemeConstantOverride("outline_size", 0); }
        if (control is Label label) { label.VerticalAlignment = VerticalAlignment.Center; label.ClipContents = true; label.ClipText = false; label.AddThemeConstantOverride("line_spacing", 0); }
    }
    private void Caption(string text, Rect2 rect)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(label); Place(label, rect); label.AddThemeColorOverride("font_color", new Color("e4c58b"));
    }
    private void Swatch(Button button, Rect2 rect)
    {
        Place(button, rect); button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("disabled", new StyleBoxEmpty()); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        foreach (string state in new[] { "hover", "pressed", "hover_pressed" })
        { var border = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = new Color("ffdd82"), DrawCenter = false }; border.SetBorderWidthAll(1); button.AddThemeStyleboxOverride(state, border); }
        // Cloth children draw above the button style; reserve one pixel for the selection outline.
        foreach (var child in button.GetChildren().OfType<Control>()) { child.OffsetLeft = child.OffsetTop = 1; child.OffsetRight = child.OffsetBottom = -1; }
        button.ChildEnteredTree += child => { if (child is Control art) { art.OffsetLeft = art.OffsetTop = 1; art.OffsetRight = art.OffsetBottom = -1; } };
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode is Key.Enter or Key.KpEnter) { GetViewport().SetInputAsHandled(); return; }
        if (key.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled(); _cancel.EmitSignal(BaseButton.SignalName.Pressed);
    }
}
