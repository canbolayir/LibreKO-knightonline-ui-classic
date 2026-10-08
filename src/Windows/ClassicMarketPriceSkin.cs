using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

public static class ClassicMarketPriceSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicMarketPricePanel? Apply(Control body)
    {
        Node? root = body; while (root != null && root is not HudWindow) root = root.GetParent();
        if (root is not HudWindow window || window.HasMeta("classic_market_price") || window.GetMeta("classic_market_price_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_market_price", true);
        var panel = new ClassicMarketPricePanel(window, body); window.AddChild(panel); window.ResetSize(); return panel;
    }
}

public partial class ClassicMarketPricePanel : Control
{
    public HudWindow Window { get; }
    private readonly Button _close;
    private readonly OptionButton[] _options;
    private readonly ClassicVendorSlot _slotFrame;
    public ClassicMarketPricePanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_market_price"; Size = CustomMinimumSize = ClassicMarketPriceLayout.Size;
        TextureFilter = TextureFilterEnum.Nearest; Theme = new Theme { DefaultFont = Plugin.Kit.Bold, DefaultFontSize = 13 };
        var native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => native.OfType<T>().Single(c => c.Name == name);
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        var title = header.GetChildren().OfType<Label>().Last(); _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black, ShowBehindParent = true });
        ClassicPartySkin.Drag(this, header, new Rect2(14, 8, 770, 32));
        Place(title, new Rect2(22, 16, 760, 24)); title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, new Rect2(800, 15, 24, 24), "");
        _close.SetMeta("market_price_expected_rect", new Rect2(800, 15, 24, 24));
        var heading = Find<HBoxContainer>("item_search_heading");
        var caption = heading.GetChildren().OfType<Label>().Last(); Place(caption, new Rect2(16, 48, 316, 20)); caption.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        var query = Find<LineEdit>("item_search_query"); Place(query, new Rect2(16, 76, 220, 28)); ClassicVendorSkin.Input(query); Font(query); query.TextDirection = TextDirection.Ltr;
        query.GuiInput += ev => { if (ev is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { Close(); query.AcceptEvent(); } };
        Action(Find<Button>("item_search_find"), new Rect2(244, 76, 88, 28));
        int index = 0; foreach (string tab in new[] { "Basic", "Property", "Reverse" }) Action(Find<Button>("item_search_tab_" + tab), new Rect2(16 + 108 * index++, 110, 100, 28));
        _options = new[] { Find<OptionButton>("item_search_name"), Find<OptionButton>("item_search_group"), Find<OptionButton>("item_search_level") };
        var optionRects = new[] { new Rect2(16, 150, 316, 26), new Rect2(16, 184, 208, 26), new Rect2(232, 184, 100, 26) };
        for (int i = 0; i < _options.Length; i++)
        {
            Action(_options[i], optionRects[i]); var popup = _options[i].GetPopup();
            popup.AddThemeFontOverride("font", Plugin.Kit.Bold); popup.AddThemeFontSizeOverride("font_size", 13);
            var box = ClassicDesign.InputBox(); box.BgColor = Colors.Black; popup.AddThemeStyleboxOverride("panel", box);
            popup.AddThemeColorOverride("font_color", ClassicDesign.Text); popup.AddThemeStyleboxOverride("hover", ClassicDesign.ButtonBox("hover"));
        }
        Place(Find<Label>("item_search_summary"), new Rect2(16, 216, 316, 20), 12);
        var scroll = Find<ScrollContainer>("item_search_scroll"); Place(scroll, new Rect2(18, 242, 292, 190));
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        var results = Find<VBoxContainer>("item_search_results"); results.AddThemeConstantOverride("separation", 2);
        var rail = new ClassicContentScrollRail(scroll) { Name = "market_price_scroll_rail", Position = new Vector2(312, 242), Size = new Vector2(18, 190) }; AddChild(rail);
        foreach (var row in results.GetChildren()) StyleRow(row); results.ChildEnteredTree += StyleRow;
        var slot = Find<ItemSlotView>("market_price_item"); Place(slot, ClassicMarketPriceLayout.Selected);
        _slotFrame = new ClassicVendorSlot(slot); slot.AddChild(_slotFrame);
        Place(Find<Label>("market_price_name"), new Rect2(70, 444, 262, 24));
        Action(Find<Button>("market_price_search"), new Rect2(70, 474, 262, 26));
        Place(Find<Label>("market_price_trades_caption"), new Rect2(344, 48, 342, 20));
        var trades = Find<Label>("market_price_trades"); Place(trades, new Rect2(692, 48, 132, 20)); trades.HorizontalAlignment = HorizontalAlignment.Right;
        trades.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        var chart = Find<PriceChart>("market_price_chart"); Place(chart, ClassicMarketPriceLayout.Chart);
        chart.AddThemeStyleboxOverride("chart_background", ClassicDesign.InputBox());
        Place(Find<Label>("market_price_updated"), new Rect2(344, 464, 480, 20), 12);
        Place(Find<Label>("market_price_latest"), new Rect2(344, 490, 480, 18), 12);
        var status = Find<Label>("market_price_status"); Place(status, ClassicMarketPriceLayout.Status, 12);
        status.HorizontalAlignment = HorizontalAlignment.Left; status.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        var cancel = new Button { Name = "market_price_cancel", Text = "Cancel" }; AddChild(cancel); Action(cancel, ClassicMarketPriceLayout.Cancel); cancel.Pressed += Close;
        window.VisibilityChanged += () => { if (!window.Visible) foreach (var option in _options) option.GetPopup().Hide(); };
    }
    private static void Font(Control control, int size = 13)
    {
        control.AddThemeFontOverride("font", Plugin.Kit.Bold); control.AddThemeFontSizeOverride("font_size", size); control.AddThemeConstantOverride("outline_size", 0);
    }
    private void Place(Control control, Rect2 rect, int size = 13)
    {
        bool visible = control.Visible; ClassicVendorSkin.Move(control, this, rect); control.Visible = visible;
        Font(control, size); control.SetMeta("market_price_expected_rect", rect);
        if (control is Label label)
        {
            label.AutowrapMode = TextServer.AutowrapMode.Off; label.VerticalAlignment = VerticalAlignment.Center;
            label.CustomMaximumSize = rect.Size; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.AddThemeColorOverride("font_color", ClassicDesign.Text);
        }
    }
    private static void StyleButton(Button button)
    {
        ClassicVendorSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!); Font(button);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, ClassicDesign.Heading);
        button.AddThemeColorOverride("font_disabled_color", ClassicDesign.Muted);
    }
    private void Action(Button button, Rect2 rect)
    {
        Place(button, rect); StyleButton(button); button.FocusMode = FocusModeEnum.All; button.Shortcut = null;
        if (button is not OptionButton) return;
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            var box = (StyleBox)button.GetThemeStylebox(state).Duplicate(); box.ContentMarginLeft = 6; box.ContentMarginRight = 6;
            button.AddThemeStyleboxOverride(state, box);
        }
        button.AddThemeConstantOverride("arrow_margin", 6);
    }
    private static void StyleRow(Node node)
    {
        if (node is not PanelContainer row || row.HasMeta("classic_market_row")) return;
        row.SetMeta("classic_market_row", true); row.CustomMinimumSize = new Vector2(0, 46); row.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var line = row.GetChildren().OfType<HBoxContainer>().Single(); var children = line.GetChildren().OfType<Control>().ToArray();
        var icon = children.OfType<TextureRect>().Single(); var labels = children.OfType<Label>().ToArray(); var action = children.OfType<Button>().Single();
        row.RemoveChild(line); var canvas = new Control { CustomMinimumSize = new Vector2(0, 46), MouseFilter = MouseFilterEnum.Pass }; row.AddChild(canvas);
        void Move(Control c) { c.Reparent(canvas); c.CustomMinimumSize = Vector2.Zero; Font(c); }
        Move(icon); Move(labels[0]); Move(action); StyleButton(action); action.FocusMode = FocusModeEnum.All;
        labels[0].TooltipText = labels[0].Text; labels[0].TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        if (labels.Length > 1) { Move(labels[1]); Font(labels[1], 11); labels[1].HorizontalAlignment = HorizontalAlignment.Left; }
        void Arrange()
        {
            float width = canvas.Size.X;
            icon.Position = new Vector2(4, 9); icon.Size = new Vector2(28, 28);
            labels[0].Position = new Vector2(38, labels.Length > 1 ? 3 : 12); labels[0].Size = new Vector2(Math.Max(1, width - 104), 22);
            if (labels.Length > 1) { labels[1].Position = new Vector2(38, 25); labels[1].Size = new Vector2(Math.Max(1, width - 104), 18); }
            action.Position = new Vector2(width - 60, 10); action.Size = new Vector2(56, 26);
        }
        canvas.Resized += Arrange; Callable.From(Arrange).CallDeferred(); line.QueueFree();
    }
    private void Close() { foreach (var option in _options) option.GetPopup().Hide(); _close.EmitSignal(BaseButton.SignalName.Pressed); }
    public override void _Process(double delta) => _slotFrame.UpdateFrame();
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        var popup = _options.Select(o => o.GetPopup()).FirstOrDefault(p => p.Visible);
        if (popup != null) popup.Hide(); else Close(); GetViewport().SetInputAsHandled();
    }
    public override void _Draw()
    {
        var border = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = ClassicDesign.InputBox().BorderColor };
        border.SetBorderWidthAll(1); DrawStyleBox(border, ClassicMarketPriceLayout.Results);
        DrawLine(new Vector2(338, 48), new Vector2(338, 500), new Color("8b795a", .35f));
    }
}
