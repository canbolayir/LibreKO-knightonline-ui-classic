using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicMailSkin
{
    public static readonly string[] WindowIds = { "mail", "mailread", "mailcompose" };
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicMailPanel? Apply(Control body)
    {
        Node? root = body; while (root != null && root is not HudWindow) root = root.GetParent();
        if (root is not HudWindow window || window.HasMeta("classic_mail") || window.GetMeta("classic_mail_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_mail", true);
        var panel = new ClassicMailPanel(window, body); window.AddChild(panel); window.ResetSize(); return panel;
    }
}

public partial class ClassicMailPanel : Control
{
    public HudWindow Window { get; }
    private readonly Control[] _native;
    private readonly Button _close;
    private readonly List<Rect2> _sections = new();
    private readonly List<(ScrollContainer Scroll, Rect2 Rect)> _fixedScrolls = new();
    private Button? _cancel;
    public ClassicMailPanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_mail"; Size = CustomMinimumSize = ClassicMailLayout.Size(window.Id);
        TextureFilter = TextureFilterEnum.Nearest;
        _native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        var title = header.GetChildren().OfType<Label>().Last();
        _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black, ShowBehindParent = true });
        ClassicPartySkin.Drag(this, header, new Rect2(14, 8, 352, 32));
        Place(title, new Rect2(22, 16, 332, 24)); Font(title, true); title.Text = window.Id == "mailcompose" ? "Write a Letter" : window.Id == "mailread" ? "Read a Letter" : "Mail";
        title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, new Rect2(380, 15, 24, 24), "");
        _close.SetMeta("mail_expected_rect", new Rect2(380, 15, 24, 24));
        if (window.Id == "mail") Inbox(); else if (window.Id == "mailread") Read(); else Compose();
    }
    private T Find<T>(string name) where T : Control => _native.OfType<T>().Single(c => c.Name == name);
    private static void Font(Control control, bool bold = true, int size = 13)
    {
        control.AddThemeFontOverride("font", bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        control.AddThemeFontSizeOverride("font_size", size); control.AddThemeConstantOverride("outline_size", 0);
    }
    private void Place(Control control, Rect2 rect, bool keepVisibility = false)
    {
        bool visible = control.Visible; ClassicVendorSkin.Move(control, this, rect); Font(control);
        if (keepVisibility) control.Visible = visible;
        control.SetMeta("mail_expected_rect", rect);
        if (control is Label label) { label.ClipText = true; label.ClipContents = true; label.CustomMaximumSize = rect.Size; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; label.AddThemeColorOverride("font_color", ClassicDesign.Text); }
    }
    private Label Caption(string text, Rect2 rect)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore }; AddChild(label); Place(label, rect);
        label.AddThemeColorOverride("font_color", ClassicDesign.Heading); return label;
    }
    private void Action(Button button, Rect2 rect, string text)
    {
        bool visible = button.Visible;
        ClassicMerchantSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!, this, rect, text);
        button.Visible = visible;
        Font(button); button.Icon = null; button.SetMeta("mail_expected_rect", rect);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, new Color("efd9b4"));
        button.AddThemeColorOverride("font_disabled_color", new Color("887e70"));
        button.FocusMode = FocusModeEnum.All;
    }
    private void Scroll(ScrollContainer scroll, Rect2 rect, bool keepVisibility = false)
    {
        Place(scroll, rect, keepVisibility); scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        StyleScroll(scroll); _sections.Add(rect.Grow(1)); _fixedScrolls.Add((scroll, rect));
    }
    private static void StyleScroll(ScrollContainer scroll)
    {
        var bar = scroll.GetVScrollBar(); var track = new StyleBoxFlat { BgColor = new Color("17130e"), BorderColor = new Color("8b795c") };
        track.SetBorderWidthAll(1); bar.AddThemeStyleboxOverride("scroll", track);
        foreach (string state in new[] { "grabber", "grabber_highlight", "grabber_pressed" }) { var box = ClassicDesign.ButtonBox(state == "grabber_highlight" ? "hover" : state == "grabber_pressed" ? "pressed" : "normal"); box.SetContentMarginAll(0); bar.AddThemeStyleboxOverride(state, box); }
        bar.CustomMinimumSize = new Vector2(10, 0); bar.FocusMode = FocusModeEnum.None;
    }
    private void Inbox()
    {
        Action(Find<Button>("mail_compose"), new Rect2(16, 52, 94, 26), "Write");
        Action(Find<Button>("mail_refresh"), new Rect2(118, 52, 78, 26), "Refresh");
        var filter = Find<CheckButton>("mail_unread_only"); Place(filter, new Rect2(204, 52, 92, 26)); Font(filter, false);
        var checkbox = Plugin.Kit.Layout("{nation}_chat_us").Find("btn_check_normal")!;
        foreach (var state in new[] { "unchecked", "checked", "unchecked_disabled", "checked_disabled" })
            filter.AddThemeIconOverride(state, ClassicChatControls.Atlas(checkbox.Images.First(i => i.Tag == (state.StartsWith("unchecked") ? 0 : 1))));
        Place(Find<Label>("mail_unread_count"), new Rect2(300, 52, 104, 26));
        Find<Label>("mail_unread_count").HorizontalAlignment = HorizontalAlignment.Right;
        Caption("Subject", new Rect2(38, 88, 176, 20)); Caption("Sender", new Rect2(214, 88, 108, 20)); Caption("Date", new Rect2(326, 88, 56, 20));
        Scroll(Find<ScrollContainer>("mail_list_scroll"), ClassicMailLayout.Inbox);
        var rows = Find<VBoxContainer>("mail_list"); rows.AddThemeConstantOverride("separation", 0); rows.CustomMinimumSize = new Vector2(1, 0);
        Watch(rows, StyleInboxRow);
        Place(Find<Label>("mail_status"), new Rect2(16, 356, 388, 28)); Find<Label>("mail_status").AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
    private static void StyleInboxRow(Control control)
    {
        if (control is Label empty) { Font(empty, false); empty.AddThemeColorOverride("font_color", ClassicDesign.Muted); }
        if (control is not PanelContainer row || !row.HasMeta("mail_row") || row.HasMeta("classic_mail_row")) return;
        row.SetMeta("classic_mail_row", true); row.CustomMinimumSize = new Vector2(1, 26);
        bool read = row.GetMeta("mail_read").AsBool(), selected = row.GetMeta("mail_selected").AsBool(), store = row.GetMeta("mail_store").AsBool();
        var box = new StyleBoxFlat { BgColor = selected ? new Color("26251f") : Colors.Transparent, BorderColor = selected ? new Color("e7c773") : new Color("544a37") };
        box.BorderWidthBottom = 1; if (selected) box.SetBorderWidthAll(1); box.SetContentMarginAll(0); row.AddThemeStyleboxOverride("panel", box);
        var fields = CharacterDetailsSkin.Tree(row).OfType<Control>().Where(c => c.Name.ToString().StartsWith("mail_row_")).ToArray();
        foreach (var child in row.GetChildren().OfType<Control>()) child.Visible = false;
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore }; row.AddChild(overlay);
        foreach (var field in fields)
        {
            field.Reparent(overlay, false); field.Visible = true; field.CustomMinimumSize = Vector2.Zero; field.MouseFilter = MouseFilterEnum.Ignore;
            var rect = field.Name.ToString() switch
            { "mail_row_marker" => new Rect2(4, 4, 14, 18), "mail_row_subject" => new Rect2(22, 4, 170, 18), "mail_row_sender" => new Rect2(198, 4, 108, 18), "mail_row_date" => new Rect2(310, 4, 46, 18), _ => new Rect2(358, 5, 16, 16) };
            field.Position = rect.Position; field.Size = rect.Size;
            field.CustomMaximumSize = rect.Size; field.ClipContents = true;
            Callable.From(() => field.Size = rect.Size).CallDeferred();
            field.SetMeta("mail_row_expected_rect", rect);
            if (field is Label label)
            {
                Font(label, !read, 12); label.AutowrapMode = TextServer.AutowrapMode.Off; label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                label.AddThemeColorOverride("font_color", store ? ClassicDesign.Heading : read ? ClassicDesign.Muted : ClassicDesign.Text);
                if (label.Name == "mail_row_sender" && label.Text.StartsWith("from ")) label.Text = label.Text[5..];
            }
        }
    }
    private void Read()
    {
        var subject = Find<Label>("mail_read_subject"); Place(subject, new Rect2(16, 60, 388, 34)); subject.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        subject.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        Place(Find<Label>("mail_read_meta"), new Rect2(16, 96, 388, 20)); Font(Find<Label>("mail_read_meta"), false, 12);
        var scroll = new ScrollContainer(); AddChild(scroll); Scroll(scroll, ClassicMailLayout.Message);
        var text = Find<Label>("mail_read_body"); text.Reparent(scroll, false); text.Visible = true; text.CustomMinimumSize = new Vector2(1, 0); text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Font(text, false); text.AutowrapMode = TextServer.AutowrapMode.WordSmart; text.AddThemeColorOverride("font_color", ClassicDesign.Text);
        Place(Find<Label>("mail_read_attachment_title"), new Rect2(16, 278, 388, 34), true); Font(Find<Label>("mail_read_attachment_title"), true, 12);
        Scroll(Find<ScrollContainer>("mail_read_attachment_scroll"), ClassicMailLayout.ReadAttachments, true);
        Watch(Find<VBoxContainer>("mail_read_attachments"), StyleAttachment);
        Action(Find<Button>("mail_claim"), new Rect2(16, 468, 186, 26), "Claim attachments");
        Action(Find<Button>("mail_delete"), new Rect2(218, 468, 186, 26), "Delete");
    }
    private void Edit(LineEdit input, Rect2 rect)
    {
        Place(input, rect); input.TextDirection = TextDirection.Ltr;
        FocusedEscape(input);
        foreach (var state in new[] { "normal", "focus", "read_only" }) input.AddThemeStyleboxOverride(state, ClassicDesign.InputBox());
        input.AddThemeColorOverride("font_color", ClassicDesign.Text); input.AddThemeColorOverride("font_placeholder_color", ClassicDesign.Muted);
    }
    private void Compose()
    {
        Caption("To", new Rect2(16, 60, 64, 24)); Edit(Find<LineEdit>("mail_to"), new Rect2(88, 60, 316, 24));
        Caption("Subject", new Rect2(16, 92, 64, 24)); Edit(Find<LineEdit>("mail_subject"), new Rect2(88, 92, 316, 24));
        Caption("Message", new Rect2(16, 122, 300, 20)); Place(Find<Label>("mail_body_remaining"), new Rect2(344, 122, 60, 20));
        Find<Label>("mail_body_remaining").HorizontalAlignment = HorizontalAlignment.Right;
        var message = Find<TextEdit>("mail_body"); Place(message, ClassicMailLayout.DraftMessage); message.TextDirection = TextDirection.Ltr;
        FocusedEscape(message);
        foreach (var state in new[] { "normal", "focus", "read_only" }) message.AddThemeStyleboxOverride(state, ClassicDesign.InputBox());
        message.AddThemeColorOverride("font_color", ClassicDesign.Text); StyleScrollText(message);
        Find<LineEdit>("mail_to").TextSubmitted += _ => Find<LineEdit>("mail_subject").GrabFocus();
        Find<LineEdit>("mail_subject").TextSubmitted += _ => message.GrabFocus();
        Caption("Coins", new Rect2(44, 274, 60, 24)); Edit(Find<MoneyEdit>("mail_gold"), new Rect2(112, 274, 292, 24));
        var coin = Plugin.Kit.Layout("el_inventory_us").Find("btn_gold")!.Images.First();
        AddChild(new TextureRect { Texture = ClassicChatControls.Atlas(coin), Position = new Vector2(16, 274), Size = new Vector2(22, 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore });
        Place(Find<Label>("mail_attach_title"), new Rect2(16, 310, 388, 20));
        var drop = Find<PanelContainer>("mail_drop_zone"); Place(drop, ClassicMailLayout.DraftAttachments);
        var stack = drop.GetChildren().OfType<Control>().Single(); stack.CustomMinimumSize = Vector2.Zero;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; drop.AddChild(scroll); stack.Reparent(scroll, false); stack.SizeFlagsHorizontal = SizeFlags.ExpandFill; StyleScroll(scroll);
        var inset = ClassicDesign.InputBox(); inset.SetContentMarginAll(4); drop.AddThemeStyleboxOverride("panel", inset);
        var hot = (StyleBoxFlat)inset.Duplicate(); hot.BorderColor = new Color("f3d78b"); hot.SetBorderWidthAll(2); hot.SetContentMarginAll(4);
        drop.SetMeta("classic_mail_idle", inset); drop.SetMeta("classic_mail_hot", hot); drop.TooltipText = "Drag items here from your inventory";
        var rows = Find<VBoxContainer>("mail_attach_rows"); rows.AddThemeConstantOverride("separation", 2); Watch(stack, c => { StyleAttachment(c); FocusedEscape(c); });
        Place(Find<Label>("mail_compose_status"), new Rect2(16, 488, 388, 26));
        Action(Find<Button>("mail_send"), new Rect2(16, 522, 186, 26), "Send");
        _cancel = Find<Button>("mail_cancel"); Action(_cancel, new Rect2(218, 522, 186, 26), "Cancel");
        var suggestions = Find<VBoxContainer>("mail_to_suggest");
        var suggestScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false }; AddChild(suggestScroll);
        var popup = new PanelContainer { Position = new Vector2(88, 84), Size = new Vector2(316, 142), ZIndex = 10, Visible = false };
        popup.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Colors.Black }); AddChild(popup); suggestScroll.Reparent(popup, false); StyleScroll(suggestScroll);
        suggestions.Reparent(suggestScroll, false); suggestions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        suggestions.VisibilityChanged += () => popup.Visible = suggestScroll.Visible = suggestions.Visible;
        Watch(suggestions, c => { if (c is Button b) { Font(b); b.Flat = false; b.CustomMinimumSize = new Vector2(0, 22); foreach (var state in new[] { "normal", "hover", "pressed" }) { var box = ClassicDesign.InputBox(); box.BgColor = Colors.Black; b.AddThemeStyleboxOverride(state, box); } } });
    }
    private static void StyleScrollText(TextEdit edit)
    {
        var bar = edit.GetVScrollBar(); bar.CustomMinimumSize = new Vector2(10, 0);
        foreach (var state in new[] { "grabber", "grabber_highlight", "grabber_pressed" }) bar.AddThemeStyleboxOverride(state, ClassicDesign.ButtonBox(state == "grabber_highlight" ? "hover" : state == "grabber_pressed" ? "pressed" : "normal"));
    }
    private void FocusedEscape(Control input)
    {
        if (input is not (LineEdit or TextEdit) || input.HasMeta("classic_mail_escape")) return;
        input.SetMeta("classic_mail_escape", true);
        input.GuiInput += ev =>
        {
            if (ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
            // Escape does what the header X does: compose hides and keeps its draft; only Cancel clears it.
            _close.EmitSignal(BaseButton.SignalName.Pressed); input.AcceptEvent();
        };
    }
    private static void StyleAttachment(Control control)
    {
        Font(control);
        Node? row = control;
        while (row != null && !row.HasMeta("mail_attachment_claimed")) row = row.GetParent();
        bool claimed = row?.GetMeta("mail_attachment_claimed", false).AsBool() ?? false;
        if (control is TextureRect icon && claimed) icon.SelfModulate = new Color(.55f, .55f, .55f);
        if (control is Label label)
        {
            label.AddThemeColorOverride("font_color", claimed ? ClassicDesign.Muted : ClassicDesign.Text); label.AutowrapMode = TextServer.AutowrapMode.Off; label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            if (label.GetParent() is HBoxContainer && (label.SizeFlagsHorizontal & SizeFlags.Expand) == 0)
                label.CustomMinimumSize = new Vector2(label.Text.Length == 0 ? 0 : label.Text.Length < 6 ? 44 : 96, 0);
        }
        if (control is SpinBox spin) { spin.CustomMinimumSize = new Vector2(68, 28); Font(spin.GetLineEdit()); spin.GetLineEdit().TextDirection = TextDirection.Ltr; }
        if (control is Button button) { ClassicDesign.StyleButton(button); button.AddThemeConstantOverride("icon_max_width", 12); button.CustomMinimumSize = new Vector2(22, 24); }
    }
    private static void Watch(Control root, Action<Control> style)
    {
        void Visit(Node node)
        {
            if (node.HasMeta("classic_mail_watch")) return; node.SetMeta("classic_mail_watch", true);
            if (node is Control control) style(control);
            node.ChildEnteredTree += child => Callable.From(() => { if (GodotObject.IsInstanceValid(child) && !child.IsQueuedForDeletion()) Visit(child); }).CallDeferred();
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(root);
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        _close.EmitSignal(BaseButton.SignalName.Pressed); GetViewport().SetInputAsHandled();
    }
    public override void _Process(double delta)
    {
        // Native responses rebuild rows before deferred styling; restore fixed viewports after their minimum sizes settle.
        foreach (var (scroll, rect) in _fixedScrolls) { scroll.CustomMinimumSize = Vector2.Zero; scroll.Size = rect.Size; }
        foreach (var label in _native.OfType<Label>().Where(l => GodotObject.IsInstanceValid(l) && l.Name.ToString() is "mail_status" or "mail_compose_status" or "mail_read_subject" or "mail_read_meta")) label.TooltipText = label.Text;
    }
    public override void _Draw()
    {
        foreach (var section in _sections) DrawStyleBox(ClassicDesign.SectionBox(0), section);
    }
}
