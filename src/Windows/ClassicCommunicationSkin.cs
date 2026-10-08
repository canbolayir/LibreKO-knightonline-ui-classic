using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicCommunicationSkin
{
    public static readonly string[] WindowIds = { "messenger", "chatrooms" };
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicCommunicationPanel? Apply(Control body)
    {
        Node? root = body; while (root != null && root is not HudWindow) root = root.GetParent();
        if (root is not HudWindow window || window.HasMeta("classic_communication") || window.GetMeta("classic_communication_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_communication", true);
        var panel = new ClassicCommunicationPanel(window, body); window.AddChild(panel); window.ResetSize(); return panel;
    }
}

public partial class ClassicCommunicationPanel : Control
{
    public HudWindow Window { get; }
    private readonly Control[] _native;
    private readonly Button _close;
    private readonly List<Rect2> _sections = new();
    private readonly List<(ScrollContainer Scroll, Rect2 Rect)> _scrolls = new();
    public ClassicCommunicationPanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_communication"; Size = CustomMinimumSize = ClassicCommunicationLayout.Size(window.Id);
        TextureFilter = TextureFilterEnum.Nearest;
        _native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        var title = header.GetChildren().OfType<Label>().Last();
        _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black, ShowBehindParent = true });
        ClassicPartySkin.Drag(this, header, new Rect2(14, 8, Size.X - 64, 32));
        Place(title, new Rect2(22, 16, Size.X - 80, 24)); title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, new Rect2(Size.X - 40, 15, 24, 24), "");
        _close.SetMeta("communication_expected_rect", new Rect2(Size.X - 40, 15, 24, 24));
        if (window.Id == "messenger") Messenger(); else Rooms();
    }
    private T Find<T>(string name) where T : Control => _native.OfType<T>().Single(c => GodotObject.IsInstanceValid(c) && !c.IsQueuedForDeletion() && c.Name == name);
    private static void Font(Control control, bool bold = true, int size = 13)
    {
        control.AddThemeFontOverride("font", bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        control.AddThemeFontSizeOverride("font_size", size); control.AddThemeConstantOverride("outline_size", 0);
    }
    private void Place(Control control, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, this, rect); Font(control); control.SetMeta("communication_expected_rect", rect);
        if (control is Label label) LabelStyle(label, rect.Size);
    }
    private static void LabelStyle(Label label, Vector2 maximum)
    {
        label.ClipText = label.ClipContents = true; label.CustomMaximumSize = maximum;
        label.AutowrapMode = TextServer.AutowrapMode.Off; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.AddThemeColorOverride("font_color", ClassicDesign.Text);
    }
    private void Caption(string text, Rect2 rect)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore }; AddChild(label); Place(label, rect);
        label.AddThemeColorOverride("font_color", ClassicDesign.Heading);
    }
    private static void Action(Button button, Control owner, Rect2 rect)
    {
        ClassicMerchantSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!, owner, rect, button.Text);
        Font(button); button.Icon = null; button.FocusMode = FocusModeEnum.None; button.SetMeta("communication_expected_rect", rect);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, new Color("efd9b4"));
        button.AddThemeColorOverride("font_disabled_color", new Color("887e70"));
    }
    private void Input(LineEdit input, Rect2 rect)
    {
        Place(input, rect); input.TextDirection = TextDirection.Ltr; input.KeepEditingOnTextSubmit = true;
        foreach (var state in new[] { "normal", "focus", "read_only" }) input.AddThemeStyleboxOverride(state, ClassicDesign.InputBox());
        input.AddThemeColorOverride("font_color", ClassicDesign.Text); input.AddThemeColorOverride("font_placeholder_color", ClassicDesign.Muted);
        input.GuiInput += ev => { if (ev is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { Close(); input.AcceptEvent(); } };
    }
    private void Scroll(ScrollContainer scroll, Rect2 rect)
    {
        Place(scroll, rect); scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; _scrolls.Add((scroll, rect)); _sections.Add(rect.Grow(1));
        var bar = scroll.GetVScrollBar(); bar.CustomMinimumSize = new Vector2(10, 0); bar.FocusMode = FocusModeEnum.None;
        var track = new StyleBoxFlat { BgColor = new Color("17130e"), BorderColor = new Color("8b795c") }; track.SetBorderWidthAll(1); bar.AddThemeStyleboxOverride("scroll", track);
        foreach (var state in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
        {
            var box = ClassicDesign.ButtonBox(state == "grabber_highlight" ? "hover" : state == "grabber_pressed" ? "pressed" : "normal"); box.SetContentMarginAll(0); bar.AddThemeStyleboxOverride(state, box);
        }
    }
    private void Messenger()
    {
        Caption("Character", new Rect2(22, 64, 178, 20)); Caption("Status", new Rect2(216, 64, 80, 20));
        Scroll(Find<ScrollContainer>("messenger_scroll"), ClassicCommunicationLayout.BuddyList);
        Watch(Find<VBoxContainer>("messenger_list"), StyleRow);
        Caption("Quick Whisper", new Rect2(16, 350, 388, 20));
        Caption("To", new Rect2(16, 378, 48, 26)); Input(Find<LineEdit>("messenger_to"), new Rect2(72, 378, 332, 26));
        Input(Find<LineEdit>("messenger_message"), new Rect2(16, 416, 292, 26));
        Action(Find<Button>("messenger_send"), this, new Rect2(316, 416, 88, 26));
        Find<LineEdit>("messenger_to").TextSubmitted += _ => Find<LineEdit>("messenger_message").GrabFocus();
    }
    private void Rooms()
    {
        Caption("Room", new Rect2(22, 64, 234, 20)); Caption("Members", new Rect2(262, 64, 72, 20));
        Scroll(Find<ScrollContainer>("rooms_scroll"), ClassicCommunicationLayout.RoomList);
        Watch(Find<VBoxContainer>("rooms_list"), StyleRow);
        Input(Find<LineEdit>("rooms_name"), new Rect2(16, 266, 244, 26));
        Action(Find<Button>("rooms_create"), this, new Rect2(268, 266, 84, 26)); Action(Find<Button>("rooms_refresh"), this, new Rect2(360, 266, 84, 26));
        Place(Find<Label>("rooms_status"), new Rect2(16, 302, 428, 22)); Find<Label>("rooms_status").AddThemeColorOverride("font_color", ClassicDesign.Heading);
        Caption("Chat", new Rect2(16, 326, 428, 20)); Scroll(Find<ScrollContainer>("rooms_log_scroll"), ClassicCommunicationLayout.RoomLog);
        Watch(Find<VBoxContainer>("rooms_log"), StyleLog);
        Input(Find<LineEdit>("rooms_message"), new Rect2(16, 530, 244, 26));
        Action(Find<Button>("rooms_send"), this, new Rect2(268, 530, 84, 26)); Action(Find<Button>("rooms_leave"), this, new Rect2(360, 530, 84, 26));
    }
    private static void Watch(VBoxContainer rows, Action<Control> style)
    {
        rows.CustomMinimumSize = new Vector2(1, 0); rows.AddThemeConstantOverride("separation", 0);
        foreach (var child in rows.GetChildren().OfType<Control>()) style(child);
        rows.ChildEnteredTree += child => Callable.From(() => { if (GodotObject.IsInstanceValid(child) && !child.IsQueuedForDeletion() && child is Control control) style(control); }).CallDeferred();
    }
    private static void StyleRow(Control child)
    {
        if (child is Label empty) { Font(empty, false); empty.AddThemeColorOverride("font_color", ClassicDesign.Muted); empty.CustomMinimumSize = new Vector2(1, 26); }
        if (child is not PanelContainer row || !row.HasMeta("communication_row") || row.HasMeta("classic_communication_row")) return;
        row.SetMeta("classic_communication_row", true); row.CustomMinimumSize = new Vector2(1, 30);
        bool room = row.GetMeta("communication_row").AsString() == "room", selected = row.GetMeta("communication_selected", false).AsBool();
        var box = new StyleBoxFlat { BgColor = selected ? new Color("26251f") : Colors.Transparent, BorderColor = selected ? new Color("e7c773") : new Color("544a37") };
        box.BorderWidthBottom = 1; if (selected) box.SetBorderWidthAll(1); box.SetContentMarginAll(0); row.AddThemeStyleboxOverride("panel", box);
        var fields = CharacterDetailsSkin.Tree(row).OfType<Control>().Where(c => c.Name.ToString().StartsWith("communication_")).ToArray();
        foreach (var control in row.GetChildren().OfType<Control>()) control.Visible = false;
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore }; row.AddChild(overlay);
        foreach (var field in fields)
        {
            var rect = field.Name.ToString() switch
            {
                "communication_name" => new Rect2(6, 5, room ? 230 : 182, 20),
                "communication_count" => new Rect2(246, 5, 54, 20),
                _ => new Rect2(room ? 314 : 292, 3, room ? 90 : 84, 24),
            };
            if (!room && field.Name == "communication_state") rect = new Rect2(200, 5, 84, 20);
            if (field is Button button) Action(button, overlay, rect);
            else
            {
                ClassicVendorSkin.Move(field, overlay, rect); Font(field); field.SetMeta("communication_expected_rect", rect);
                if (field is Label label)
                {
                    LabelStyle(label, rect.Size);
                    if (label.Name == "communication_count") label.HorizontalAlignment = HorizontalAlignment.Center;
                    if (label.Name == "communication_state") label.AddThemeColorOverride("font_color", room || row.GetMeta("communication_online", false).AsBool() ? new Color("91c678") : ClassicDesign.Muted);
                    if (!room && label.Name == "communication_name" && !row.GetMeta("communication_online").AsBool()) label.AddThemeColorOverride("font_color", ClassicDesign.Muted);
                }
            }
        }
    }
    private static void StyleLog(Control control)
    {
        if (control is not HBoxContainer line || line.HasMeta("classic_communication_line")) return;
        line.SetMeta("classic_communication_line", true); line.AddThemeConstantOverride("separation", 6); line.CustomMinimumSize = new Vector2(1, 22);
        foreach (var label in line.GetChildren().OfType<Label>())
        {
            Font(label, false); label.ClipText = label.ClipContents = true; label.VerticalAlignment = VerticalAlignment.Top; label.SizeFlagsVertical = SizeFlags.Fill;
            if (label.Name == "communication_author")
            {
                label.CustomMinimumSize = new Vector2(132, 0); label.CustomMaximumSize = new Vector2(132, float.MaxValue);
                label.AutowrapMode = TextServer.AutowrapMode.Off; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                label.AddThemeColorOverride("font_color", ClassicDesign.Heading);
            }
            else { label.ClipText = false; label.SizeFlagsVertical = SizeFlags.Fill; label.AddThemeColorOverride("font_color", ClassicDesign.Text); }
        }
    }
    private void Close() => _close.EmitSignal(BaseButton.SignalName.Pressed);
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        Close(); GetViewport().SetInputAsHandled();
    }
    public override void _Process(double delta)
    {
        foreach (var (scroll, rect) in _scrolls) { scroll.CustomMinimumSize = Vector2.Zero; scroll.Size = rect.Size; }
        if (Window.Id == "chatrooms")
        {
            var status = Find<Label>("rooms_status"); status.TooltipText = status.Text;
            bool joined = Window.GetMeta("communication_room_id", 0).AsInt32() != 0; Find<Button>("rooms_send").Disabled = Find<Button>("rooms_leave").Disabled = !joined;
        }
    }
    public override void _Draw() { foreach (var section in _sections) DrawStyleBox(ClassicDesign.SectionBox(0), section); }
}
