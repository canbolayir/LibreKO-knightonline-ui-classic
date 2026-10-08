using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicIdentitySkin
{
    public static readonly string[] WindowIds = { "creat_clan", "namechange", "disguise" };
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicIdentityPanel? Apply(Control body)
    {
        Node? root = body;
        while (root != null && root is not HudWindow) root = root.GetParent();
        if (root is not HudWindow window || window.HasMeta("classic_identity")) return null;
        if (window.GetMeta("classic_identity_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_identity", true);
        var panel = new ClassicIdentityPanel(window, body); window.AddChild(panel); window.ResetSize();
        return panel;
    }
}

// Original artwork is drawn around independently editable, live game controls.
public partial class ClassicIdentityPanel : Control
{
    public HudWindow Window { get; }
    private readonly Control[] _native;
    private readonly LayoutNode _art;
    private readonly Label _title;
    private ScrollContainer? _forms;
    private Label? _note;
    public ClassicIdentityPanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_identity";
        Size = CustomMinimumSize = ClassicIdentityLayout.Size(window.Id);
        TextureFilter = TextureFilterEnum.Nearest;
        _art = Plugin.Kit.Layout(ClassicIdentityLayout.Artwork(window.Id));
        _native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        var bar = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        _title = bar.GetChildren().OfType<Label>().Last();
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        ClassicPartySkin.Drag(this, bar, new Rect2(4, 2, Size.X - 8, window.Id == "disguise" ? 43 : window.Id == "namechange" ? 13 : 23));
        switch (window.Id)
        {
            case "creat_clan": Clan(); break;
            case "namechange": Rename(); break;
            case "disguise": Disguise(); break;
        }
    }
    private T Native<T>(string name) where T : Control => _native.OfType<T>().Single(c => GodotObject.IsInstanceValid(c) && !c.IsQueuedForDeletion() && c.Name == name);
    private static void Font(Control control, int size = 13, bool bold = false)
    {
        control.AddThemeFontOverride("font", bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        control.AddThemeFontSizeOverride("font_size", size);
        control.AddThemeConstantOverride("outline_size", 0);
    }
    private void Place(Control control, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, this, rect); Font(control);
        control.SetMeta("identity_expected_rect", rect);
    }
    private void Original(Control control, string field)
    {
        Place(control, ClassicIdentityLayout.Field(Window.Id, field));
        var reference = _art.Find(field)!;
        Font(control, reference.Size > 0 ? UiKit.FontSize(reference) : 13, reference.Bold);
        if (control is Label label) label.AddThemeColorOverride("font_color", reference.Color);
    }
    private void Action(Button button, string field, string text)
    {
        var rect = ClassicIdentityLayout.Field(Window.Id, field);
        ClassicMerchantSkin.Button(button, _art.Find(field)!, this, rect, text);
        Font(button, Window.Id == "disguise" ? 13 : 15, true);
        button.AddThemeColorOverride("font_shadow_color", Colors.Black);
        button.AddThemeConstantOverride("shadow_offset_x", 1); button.AddThemeConstantOverride("shadow_offset_y", 1);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, new Color(Window.Id == "namechange" ? "80ffff" : Window.Id == "disguise" ? "ffddfe" : "f9e9b0"));
        button.AddThemeColorOverride("font_disabled_color", new Color("8b8175"));
        button.SetMeta("identity_expected_rect", rect);
    }
    private void Input(LineEdit input, string field)
    {
        Original(input, field); Font(input, 13);
        input.TextDirection = TextDirection.Ltr; input.Alignment = HorizontalAlignment.Left;
        input.AddThemeColorOverride("font_color", Colors.White);
        input.AddThemeColorOverride("font_placeholder_color", new Color("8e8e89"));
        // The original input's own atlas background remains behind the native text editor.
        foreach (string state in new[] { "normal", "focus", "read_only" })
            input.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2 });
        input.CustomMinimumSize = Vector2.Zero;
        input.Size = ClassicIdentityLayout.Field(Window.Id, field).Size;
    }
    private void Clan()
    {
        Original(_title, "Text_title"); _title.Text = "Create a Clan";
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        var message = Native<Label>("clan_create_message");
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll); Place(scroll, ClassicIdentityLayout.Field(Window.Id, "Text_Message")); Scroll(scroll);
        message.Reparent(scroll, false); message.Visible = true; Font(message, 14, true);
        message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        message.CustomMinimumSize = new Vector2(1, 0); message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        message.VerticalAlignment = VerticalAlignment.Center;
        Input(Native<LineEdit>("clan_create_name"), "Edit_Clan");
        Action(Native<Button>("clan_create_accept"), "btn_yes", "OK");
        Action(Native<Button>("clan_create_cancel"), "btn_no", "Cancel");
    }
    private void Rename()
    {
        var hint = Native<Label>("name_change_hint"); Original(hint, "text_explanation");
        hint.Text = "Enter your new name. Requires a Scroll of Identity.\nYour old name will no longer be valid after the change. Choose carefully. Names must have 3-20 characters.";
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; hint.ClipText = false;
        var label = new Label { Text = "ID", MouseFilter = MouseFilterEnum.Ignore }; AddChild(label);
        Place(label, new Rect2(28, 80, 31, 22)); Font(label, 16, true);
        Input(Native<LineEdit>("name_change_name"), "edit_id");
        var status = Native<Label>("name_change_status"); Place(status, new Rect2(25, 101, 326, 18)); Font(status, 12);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        Action(Native<Button>("name_change_accept"), "btn_ok", "OK");
        Action(Native<Button>("name_change_cancel"), "btn_cancel", "Cancel");
    }
    private void Disguise()
    {
        var groupRows = Native<VBoxContainer>("disguise_groups");
        var formRows = Native<VBoxContainer>("disguise_forms");
        var groups = (ScrollContainer)groupRows.GetParent();
        _forms = (ScrollContainer)formRows.GetParent();
        Place(groups, ClassicIdentityLayout.Field(Window.Id, "list_group")); Scroll(groups);
        Place(_forms, ClassicIdentityLayout.Field(Window.Id, "list_monster")); Scroll(_forms);
        foreach (var rows in new[] { groupRows, formRows })
        {
            rows.CustomMinimumSize = new Vector2(1, 0); rows.AddThemeConstantOverride("separation", 0);
            Watch(rows);
        }
        _note = Native<Label>("disguise_note");
        Place(_note, new Rect2(17, 468, 141, 40)); Font(_note, 11);
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart; _note.ClipContents = true;
        _note.CustomMaximumSize = new Vector2(141, 40);
        Action(Native<Button>("disguise_accept"), "btn_ok", "OK");
        Action(Native<Button>("disguise_cancel"), "btn_close", "Close");
    }
    private static void Scroll(ScrollContainer scroll)
    {
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        var bar = scroll.GetVScrollBar();
        var track = new StyleBoxFlat { BgColor = new Color("15120f"), BorderColor = new Color("92785b") };
        track.SetBorderWidthAll(1); bar.AddThemeStyleboxOverride("scroll", track);
        foreach (string state in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
        {
            var grip = ClassicDesign.ButtonBox(state == "grabber_highlight" ? "hover" : state == "grabber_pressed" ? "pressed" : "normal");
            grip.SetContentMarginAll(0); bar.AddThemeStyleboxOverride(state, grip);
        }
        bar.CustomMinimumSize = new Vector2(10, 0); bar.FocusMode = FocusModeEnum.None;
    }
    private static void Watch(Node root)
    {
        void Visit(Node node)
        {
            if (node.HasMeta("identity_watched")) return;
            node.SetMeta("identity_watched", true);
            if (node is Button button)
            {
                Font(button, 13); button.CustomMinimumSize = new Vector2(0, 19);
                button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                button.ClipText = true;
                bool selected = button.GetMeta("identity_selected", false).AsBool();
                foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
                {
                    var box = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = state != "normal" || selected ? new Color("dfc893") : Colors.Transparent };
                    box.SetBorderWidthAll(1); box.SetContentMarginAll(1); button.AddThemeStyleboxOverride(state, box);
                }
                foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
                    button.AddThemeColorOverride(state, selected ? new Color("ffffa0") : new Color("eee3cf"));
            }
            node.ChildEnteredTree += child => Callable.From(() => { if (GodotObject.IsInstanceValid(child)) Visit(child); }).CallDeferred();
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(root);
    }
    public override void _Process(double delta)
    {
        if (_forms == null || _note == null) return;
        bool note = _note.Text.Length > 0; _note.Visible = note;
        _note.TooltipText = _note.Text;
        var bounds = ClassicIdentityLayout.Field(Window.Id, "list_monster");
        if (note) bounds.Size = new Vector2(bounds.Size.X, bounds.Size.Y - 44);
        _forms.Size = bounds.Size; _forms.SetMeta("identity_expected_rect", bounds);
        var accept = Native<Button>("disguise_accept");
        accept.Disabled = !Native<VBoxContainer>("disguise_forms").GetChildren().OfType<Button>().Any(b => !b.IsQueuedForDeletion());
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape)
        {
            Native<Button>(Window.Id == "creat_clan" ? "clan_create_cancel" : Window.Id == "namechange" ? "name_change_cancel" : "disguise_cancel")
                .EmitSignal(BaseButton.SignalName.Pressed);
            GetViewport().SetInputAsHandled(); return;
        }
        if (key.Keycode is not (Key.Enter or Key.KpEnter) || GetViewport().GuiGetFocusOwner() is LineEdit) return;
        var accept = Native<Button>(Window.Id == "creat_clan" ? "clan_create_accept" : Window.Id == "namechange" ? "name_change_accept" : "disguise_accept");
        if (!accept.Disabled) accept.EmitSignal(BaseButton.SignalName.Pressed);
        GetViewport().SetInputAsHandled();
    }
    public override void _Draw()
    {
        var artworkBounds = _art.Children.Where(n => n.IsImage)
            .Select(n => new Rect2(n.Position - _art.Position, n.SizeVec)).Aggregate((a, b) => a.Merge(b));
        DrawRect(artworkBounds.Grow(-4), Colors.Black);
        foreach (var piece in _art.Children.Where(n => n.IsImage))
            DrawTextureRectRegion(Plugin.Kit.Texture(piece.Texture!), new Rect2(piece.Position - _art.Position, piece.SizeVec), new Rect2(piece.SrcX, piece.SrcY, piece.SrcW, piece.SrcH));
        foreach (var field in _art.All().Where(n => n.Type == "edit"))
            foreach (var piece in field.Children.Where(n => n.IsImage))
                DrawTextureRectRegion(Plugin.Kit.Texture(piece.Texture!), new Rect2(piece.Position - _art.Position, piece.SizeVec), new Rect2(piece.SrcX, piece.SrcY, piece.SrcW, piece.SrcH));
    }
}
