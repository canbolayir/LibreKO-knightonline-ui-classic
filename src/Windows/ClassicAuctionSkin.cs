using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicAuctionSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicAuctionPanel? Apply(Control body)
    {
        Node? owner = body;
        while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_auction") || window.GetMeta("classic_auction_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_auction", true);
        var panel = new ClassicAuctionPanel(window, body); window.AddChild(panel); window.ResetSize(); return panel;
    }
}

public partial class ClassicAuctionPanel : Control
{
    public HudWindow Window { get; }
    private readonly List<ClassicVendorSlot> _frames = new();
    private readonly Button _close;
    private readonly Control _body;
    private readonly Label _selected;
    public ClassicAuctionPanel(HudWindow window, Control body)
    {
        Window = window; _body = body; Name = "classic_auction";
        Size = CustomMinimumSize = ClassicAuctionLayout.Size; TextureFilter = TextureFilterEnum.Nearest;
        Theme = new Theme { DefaultFont = Plugin.Kit.Bold, DefaultFontSize = 13 };
        var buttonArt = Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!;
        var sample = new Button(); ClassicVendorSkin.Button(sample, buttonArt);
        Theme.SetStylebox("auction_tab_normal", "Button", sample.GetThemeStylebox("normal"));
        Theme.SetStylebox("auction_tab_selected", "Button", sample.GetThemeStylebox("pressed"));
        Theme.SetStylebox("auction_tab_hover", "Button", sample.GetThemeStylebox("hover")); sample.Free();
        window.Theme = Theme;
        var native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => native.OfType<T>().Single(c => c.Name == name);
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        var title = header.GetChildren().OfType<Label>().Last(); _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black, ShowBehindParent = true });
        ClassicPartySkin.Drag(this, header, new Rect2(14, 8, 816, 32));
        Place(title, this, new Rect2(22, 16, 808, 24)); title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, new Rect2(840, 15, 24, 24), "");
        _close.SetMeta("auction_expected_rect", new Rect2(840, 15, 24, 24));
        Place(body, this, ClassicAuctionLayout.Body);
        var today = Find<VBoxContainer>("auction_page_Today");
        var canvas = new Control { Name = "auction_today_composition", CustomMinimumSize = new Vector2(0, 430), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        today.AddChild(canvas);
        foreach (var child in today.GetChildren().OfType<Control>().Where(c => c != canvas).ToArray()) { child.Reparent(canvas); child.Visible = false; }
        Place(Find<GridContainer>("auction_lots_grid"), canvas, ClassicAuctionLayout.Lots);
        _selected = Find<Label>("auction_selected_name"); Place(_selected, canvas, ClassicAuctionLayout.Selection);
        _selected.ClipText = true; _selected.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var balance = Find<PanelContainer>("auction_balance");
        var balanceLabels = CharacterDetailsSkin.Tree(balance).OfType<Label>().ToArray();
        var balanceIcons = CharacterDetailsSkin.Tree(balance).OfType<TextureRect>().ToArray();
        Place(balanceLabels[0], canvas, new Rect2(8, 214, 260, 20));
        Place(Find<Label>("auction_gold"), canvas, new Rect2(8, 244, 168, 24));
        Place(balanceIcons[0], canvas, new Rect2(188, 245, 22, 22));
        Place(Find<Label>("auction_checks"), canvas, new Rect2(218, 244, 50, 24));
        Place(balanceLabels[3], canvas, new Rect2(8, 284, 260, 20));
        Place(Find<SpinBox>("auction_millions"), canvas, new Rect2(8, 318, 88, 28));
        Place(balanceLabels[4], canvas, new Rect2(100, 318, 58, 28));
        Place(balanceIcons[1], canvas, new Rect2(162, 321, 22, 22));
        Place(Find<SpinBox>("auction_check_input"), canvas, new Rect2(188, 318, 80, 28));
        var bidLabels = CharacterDetailsSkin.Tree(Find<PanelContainer>("auction_bid")).OfType<Label>().ToArray();
        Place(bidLabels[0], canvas, new Rect2(294, 214, 260, 20));
        Place(Find<Label>("auction_total"), canvas, new Rect2(294, 244, 260, 28));
        var words = Find<Label>("auction_words"); Place(words, canvas, new Rect2(294, 280, 260, 62));
        words.AutowrapMode = TextServer.AutowrapMode.WordSmart; words.CustomMaximumSize = words.Size; words.ClipContents = true;
        Place(Find<Button>("auction_place_bid"), canvas, new Rect2(294, 380, 260, 28));
        var statusLabels = CharacterDetailsSkin.Tree(Find<PanelContainer>("auction_status_section")).OfType<Label>().ToArray();
        Place(statusLabels[0], canvas, new Rect2(580, 214, 260, 20));
        Place(Find<Label>("auction_current"), canvas, new Rect2(580, 244, 260, 24));
        Place(statusLabels[2], canvas, new Rect2(580, 280, 260, 20));
        Place(Find<Label>("auction_minimum"), canvas, new Rect2(580, 310, 260, 24));
        Place(statusLabels[4], canvas, new Rect2(692, 348, 148, 20));
        Place(Find<Label>("auction_clock"), canvas, new Rect2(692, 380, 148, 28));
        Place(Find<Button>("auction_refresh"), canvas, new Rect2(580, 380, 104, 28));
        canvas.Draw += () => { for (int i = 0; i < 3; i++) canvas.DrawStyleBox(Box(0), ClassicAuctionLayout.Section(i)); canvas.DrawStyleBox(Box(0), ClassicAuctionLayout.Selection); };
        Watch(body);
        Find<HBoxContainer>("auction_tabs").CustomMinimumSize = new Vector2(0, 28);
        foreach (var tab in native.OfType<Button>().Where(c => c.HasMeta("auction_tab_selected")))
            tab.AddThemeStyleboxOverride("normal", tab.GetThemeStylebox(tab.GetMeta("auction_tab_selected").AsBool() ? "auction_tab_selected" : "auction_tab_normal"));
    }
    private static StyleBoxFlat Box(int inset)
    {
        var box = ClassicDesign.InputBox(); box.BgColor = Colors.Black;
        foreach (var side in new[] { Side.Left, Side.Right, Side.Top, Side.Bottom }) box.SetContentMargin(side, inset);
        return box;
    }
    private static void Font(Control c) { c.AddThemeFontOverride("font", Plugin.Kit.Bold); c.AddThemeFontSizeOverride("font_size", 13); c.AddThemeConstantOverride("outline_size", 0); }
    private static void Place(Control c, Control parent, Rect2 rect)
    {
        ClassicVendorSkin.Move(c, parent, rect); Font(c); c.SetMeta("auction_expected_rect", rect);
        if (c is Label label) { label.VerticalAlignment = VerticalAlignment.Center; label.CustomMaximumSize = rect.Size; }
    }
    private void Watch(Node node)
    {
        if (node.HasMeta("classic_auction_control")) return; node.SetMeta("classic_auction_control", true);
        if (node is Control c)
        {
            Font(c);
            if (c is PanelContainer panel && c is not ItemSlotView) panel.AddThemeStyleboxOverride("panel", Box(8));
            if (c is ItemSlotView slot)
            {
                var frame = new ClassicVendorSlot(slot); _frames.Add(frame); slot.AddChild(frame);
                var overlay = new Control { Name = "auction_count_overlay", MouseFilter = MouseFilterEnum.Ignore }; slot.AddChild(overlay);
                ItemCountStyle.Apply(slot.CountLabel, overlay, Plugin.Kit.Bold);
                overlay.SetMeta("auction_count_slot", true);
            }
            if (c is LineEdit edit)
            {
                ClassicVendorSkin.Input(edit); Font(edit); edit.TextDirection = TextDirection.Ltr; edit.KeepEditingOnTextSubmit = true;
                edit.GuiInput += ev => { if (ev is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { Close(); edit.AcceptEvent(); } };
            }
            if (c is Button button)
            {
                ClassicVendorSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!); Font(button);
                foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, ClassicDesign.Heading);
                button.AddThemeColorOverride("font_disabled_color", ClassicDesign.Muted); button.FocusMode = FocusModeEnum.All; button.Shortcut = null;
                button.CustomMinimumSize = new Vector2(button.HasMeta("auction_row_action") ? 112 : button.CustomMinimumSize.X, 28);
            }
            if (c is ScrollContainer scroll)
            {
                scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
                var bar = scroll.GetVScrollBar(); bar.CustomMinimumSize = new Vector2(10, 0);
                bar.AddThemeStyleboxOverride("scroll", Box(0));
                foreach (string state in new[] { "grabber", "grabber_highlight", "grabber_pressed" }) bar.AddThemeStyleboxOverride(state, ClassicDesign.ButtonBox(state == "grabber" ? "normal" : "hover"));
            }
        }
        foreach (var child in node.GetChildren()) Watch(child);
        if (node is ItemSlotView counted) ItemCountStyle.Apply(counted.CountLabel, (Control)counted.CountLabel.GetParent(), Plugin.Kit.Bold);
        node.ChildEnteredTree += Watch;
    }
    private void Close() => _close.EmitSignal(BaseButton.SignalName.Pressed);
    public override void _Process(double delta)
    {
        _selected.TooltipText = _selected.Text;
        _frames.RemoveAll(f => !IsInstanceValid(f));
        foreach (var frame in _frames)
        {
            frame.UpdateFrame(); var count = frame.OwnerCell.GetNode<Control>("auction_count_overlay");
            count.Position = Vector2.Zero; count.Size = frame.OwnerCell.Size;
        }
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        Close(); GetViewport().SetInputAsHandled();
    }
}
