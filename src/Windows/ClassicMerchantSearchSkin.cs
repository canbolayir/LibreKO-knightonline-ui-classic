using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicMerchantSearchSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicMerchantSearchPanel? Apply(Control body)
    {
        Node? root = body; while (root != null && root is not HudWindow) root = root.GetParent();
        if (root is not HudWindow window || window.HasMeta("classic_merchant_search") || window.GetMeta("classic_merchant_search_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_merchant_search", true);
        var panel = new ClassicMerchantSearchPanel(window, body); window.AddChild(panel); window.ResetSize(); return panel;
    }
}

public partial class ClassicMerchantSearchPanel : Control
{
    public HudWindow Window { get; }
    private readonly Button _close;
    private readonly OptionButton _scope;
    public ClassicMerchantSearchPanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_merchant_search"; Size = CustomMinimumSize = ClassicMerchantSearchLayout.Size; TextureFilter = TextureFilterEnum.Nearest;
        var native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => native.OfType<T>().Single(c => c.Name == "merchant_search_" + name);
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        var title = header.GetChildren().OfType<Label>().Last(); _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black, ShowBehindParent = true });
        ClassicPartySkin.Drag(this, header, new Rect2(14, 8, 706, 32));
        Place(title, new Rect2(22, 16, 696, 24)); title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        if (string.IsNullOrWhiteSpace(title.Text)) title.Text = "Merchant Search";
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, new Rect2(740, 15, 24, 24), "");
        _close.SetMeta("merchant_search_expected_rect", new Rect2(740, 15, 24, 24));
        Place(Find<Label>("tip"), new Rect2(16, 48, 748, 20));
        Place(Find<Label>("query_label"), new Rect2(16, 76, 52, 24));
        var query = Find<LineEdit>("query"); Place(query, new Rect2(76, 74, 244, 28));
        ClassicVendorSkin.Input(query); Font(query); query.TextDirection = TextDirection.Ltr;
        query.GuiInput += ev => { if (ev is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { Close(); query.AcceptEvent(); } };
        Action(Find<Button>("find"), new Rect2(328, 74, 80, 28));
        Action(Find<Button>("all"), new Rect2(416, 74, 80, 28));
        Place(Find<Label>("scope_label"), new Rect2(504, 76, 48, 24));
        _scope = Find<OptionButton>("scope"); Action(_scope, new Rect2(560, 74, 204, 28));
        var popup = _scope.GetPopup(); popup.AddThemeFontOverride("font", Plugin.Kit.Bold); popup.AddThemeFontSizeOverride("font_size", 13);
        var popupBox = ClassicDesign.InputBox(); popupBox.BgColor = Colors.Black;
        popup.AddThemeStyleboxOverride("panel", popupBox); popup.AddThemeColorOverride("font_color", ClassicDesign.Text);
        popup.AddThemeStyleboxOverride("hover", ClassicDesign.ButtonBox("hover"));
        foreach (var entry in new[] { ("chat", 16, 80), ("location", 104, 96), ("history", 208, 80) })
        {
            var caption = Find<Label>("header_" + entry.Item1); Place(caption, new Rect2(entry.Item2, 116, entry.Item3, 28));
            caption.HorizontalAlignment = HorizontalAlignment.Center; caption.AddThemeColorOverride("font_color", ClassicDesign.Heading);
            Font(caption, 12);
        }
        Action(Find<Button>("name_sort"), new Rect2(328, 116, 268, 28));
        var priceCaption = Find<Label>("header_price"); Place(priceCaption, new Rect2(604, 110, 160, 18)); priceCaption.HorizontalAlignment = HorizontalAlignment.Center;
        priceCaption.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        Action(Find<Button>("low_sort"), new Rect2(604, 132, 78, 22), 11);
        Action(Find<Button>("high_sort"), new Rect2(686, 132, 78, 22), 11);
        for (int index = 0; index < 10; index++)
        {
            int column = 0;
            foreach (string part in new[] { "whisper", "move", "view", "icon", "name", "price" })
            {
                var control = Find<Control>(part + "_" + index); var rect = ClassicMerchantSearchLayout.Row(index, column++);
                bool visible = control.Visible;
                if (control is Button button) Action(button, rect); else Place(control, rect);
                control.Visible = visible;
                if (part == "price") control.AddThemeColorOverride("font_color", ClassicDesign.Heading);
            }
        }
        Place(Find<Label>("status"), new Rect2(104, 480, 188, 26)); Font(Find<Label>("status"), 12);
        var refresh = Find<Button>("refresh"); refresh.Text = "Refresh"; Action(refresh, new Rect2(16, 480, 80, 26));
        var previous = Find<Button>("previous"); previous.TooltipText = "Previous page"; Action(previous, new Rect2(300, 480, 26, 26));
        var next = Find<Button>("next"); next.TooltipText = "Next page"; Action(next, new Rect2(604, 480, 26, 26));
        var pages = Find<HBoxContainer>("pages"); Place(pages, new Rect2(334, 480, 262, 26)); pages.Alignment = BoxContainer.AlignmentMode.Center;
        void Page(Node node)
        {
            if (node is not Button button) return; button.CustomMinimumSize = new Vector2(28, 26); button.SizeFlagsVertical = SizeFlags.Fill;
            StyleButton(button, 12); button.FocusMode = FocusModeEnum.All; button.ClipText = false;
        }
        foreach (var node in pages.GetChildren()) Page(node); pages.ChildEnteredTree += Page;
        var cancel = new Button { Name = "merchant_search_cancel", Text = "Cancel" }; AddChild(cancel); cancel.Pressed += Close;
        Action(cancel, new Rect2(668, 480, 96, 26));
        window.VisibilityChanged += () => { if (!window.Visible) popup.Hide(); };
    }
    private void Close() { _scope.GetPopup().Hide(); _close.EmitSignal(BaseButton.SignalName.Pressed); }
    private static void Font(Control control, int size = 13)
    {
        control.AddThemeFontOverride("font", Plugin.Kit.Bold); control.AddThemeFontSizeOverride("font_size", size); control.AddThemeConstantOverride("outline_size", 0);
    }
    private void Place(Control control, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, this, rect); Font(control); control.SetMeta("merchant_search_expected_rect", rect);
        if (control is Label label)
        {
            label.AutowrapMode = TextServer.AutowrapMode.Off; label.VerticalAlignment = VerticalAlignment.Center;
            label.CustomMaximumSize = rect.Size; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.AddThemeColorOverride("font_color", ClassicDesign.Text);
        }
    }
    private static void StyleButton(Button button, int size)
    {
        ClassicVendorSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!); Font(button, size);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, ClassicDesign.Heading);
        button.AddThemeColorOverride("font_disabled_color", ClassicDesign.Muted);
    }
    private void Action(Button button, Rect2 rect, int size = 13)
    {
        Place(button, rect); StyleButton(button, size); button.FocusMode = FocusModeEnum.All; button.Shortcut = null;
        button.SetMeta("merchant_search_expected_rect", rect);
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (_scope.GetPopup().Visible) _scope.GetPopup().Hide(); else Close(); GetViewport().SetInputAsHandled();
    }
    public override void _Draw()
    {
        DrawStyleBox(ClassicDesign.SectionBox(0), ClassicMerchantSearchLayout.List);
        var border = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = ClassicDesign.InputBox().BorderColor };
        border.SetBorderWidthAll(1); DrawStyleBox(border, ClassicMerchantSearchLayout.List);
        for (int row = 1; row < 10; row++) DrawLine(new Vector2(18, 160 + row * 30), new Vector2(762, 160 + row * 30), new Color("8b795a", .3f));
    }
}
