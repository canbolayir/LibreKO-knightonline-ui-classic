using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicServiceSkin
{
    public static readonly string[] WindowIds = { "repair", "warp", "seal", "piecechange", "itemcombine", "combinerecipes", "class_change" };
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicServicePanel? Apply(Control body)
    {
        Node? owner = body;
        while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_service")) return null;
        if (window.GetMeta("classic_service_controls", 0).AsInt32() != 1)
        {
            ClassicSkin.Apply(window);
            return null;
        }
        window.SetMeta("classic_service", true);
        if (window.Id == "repair")
        {
            window.SetMeta("classic_inventory_repair", true);
            return null;
        }
        var panel = new ClassicServicePanel(window, body); window.AddChild(panel); window.ResetSize();
        return panel;
    }
}

public partial class ClassicServicePanel : Control
{
    public HudWindow Window { get; }
    private readonly Control[] _native;
    private readonly List<Control> _slots = new();
    private Control? _secretRow, _secretField;
    private Control? _secretCaption;
    private readonly StyleBoxTexture _cell;
    private ScrollContainer? _warpList;
    private readonly StyleBoxEmpty _warpEmpty = new();
    private static void WarpFont(Control control, bool bold = false, int size = 13)
    {
        control.AddThemeFontOverride("font", bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        control.AddThemeFontSizeOverride("font_size", size);
    }

    public ClassicServicePanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_service"; Size = CustomMinimumSize = ClassicServiceLayout.Size(window.Id);
        TextureFilter = TextureFilterEnum.Nearest;
        _native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        _cell = InventoryCell();
        var grip = window.Header!.GetChildren().OfType<HBoxContainer>().First();
        var title = grip.GetChildren().OfType<Label>().Last();
        var close = grip.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        if (window.Id is not ("warp" or "class_change"))
        {
            AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black });
        }
        ClassicPartySkin.Drag(this, grip, new Rect2(14, 8, Size.X - 50, 30));
        if (window.Id != "class_change") Place(title, new Rect2(22, window.Id == "warp" ? 6 : 16, Size.X - 70, 24));
        title.HorizontalAlignment = HorizontalAlignment.Left;
        if (window.Id != "seal") title.Text = Title(window.Id);
        if (window.Id != "class_change")
        {
            ClassicMerchantSkin.Button(close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this,
                new Rect2(Size.X - 40, 15, 24, 24), "");
            close.SetMeta("service_expected_rect", new Rect2(Size.X - 40, 15, 24, 24));
        }
        if (window.Id == "warp")
        {
            Place(title, new Rect2(11, 11, 240, 19)); WarpFont(title, true, 16);
            title.AddThemeColorOverride("font_color", Plugin.Kit.Nation == 1 ? Colors.White : new Color("f9e486"));
            close.Visible = false;
        }
        switch (window.Id)
        {
            case "warp": Warp(); break;
            case "seal": Seal(); break;
            case "piecechange": Piece(); break;
            case "itemcombine": Combine(); break;
            case "combinerecipes": RecipeBook(); break;
            case "class_change": Redistribution(close); break;
        }
        if (window.Id is "repair" or "seal")
        {
            var coin = Plugin.Kit.Layout("el_inventory_us").Find("btn_gold")!.Images.First();
            AddChild(new TextureRect { Position = new Vector2(14, window.Id == "repair" ? 416 : 464), Size = new Vector2(22, 25),
                Texture = ClassicChatControls.Atlas(coin), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore });
        }
    }
    private void Redistribution(Button close)
    {
        Place(Find<Label>("redistribution_description"), new Rect2(15, 20, 293, 64));
        var choices = _native.OfType<Button>().Where(b => b.Text is "Stat points" or "Mastery points").ToArray();
        void TextButton(Button button, Rect2 rect, string text)
        {
            Place(button, rect); button.Text = text; button.Icon = null; button.Alignment = HorizontalAlignment.Left;
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            button.AddThemeColorOverride("font_color", new Color("e4d9bf")); button.AddThemeColorOverride("font_hover_color", new Color("ffe094"));
            button.AddThemeColorOverride("font_pressed_color", new Color("e4bf68")); button.FocusMode = FocusModeEnum.None;
        }
        for (int i = 0; i < choices.Length; i++) TextButton(choices[i], new Rect2(20, 100 + i * 32, 293, 23), "Redistribute " + choices[i].Text.ToLowerInvariant());
        TextButton(close, new Rect2(20, 164, 293, 23), "Close");
    }
    private static string Title(string id) => id switch
    {
        "warp" => "Zone List", "repair" => "Repair", "seal" => "Item Seal / Unseal",
        "piecechange" => "Chaotic Generator", "itemcombine" => "Item Combination", _ => "Book of Transformation"
    };
    private T Find<T>(string name) where T : Control => _native.OfType<T>().Single(n => n.Name == name);
    private void Place(Control control, Rect2 rect, bool keepVisibility = false)
    {
        bool visible = control.Visible;
        ClassicVendorSkin.Move(control, this, rect); ClassicVendorSkin.Font(control);
        if (keepVisibility) control.Visible = visible;
        control.SetMeta("service_expected_rect", rect);
    }
    private Label Caption(string text, Rect2 rect)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore }; AddChild(label);
        label.Position = rect.Position; label.Size = rect.Size; ClassicVendorSkin.Font(label);
        label.AddThemeColorOverride("font_color", new Color("e2c68b")); return label;
    }
    private void Button(Button button, Rect2 rect, string? text = null)
    {
        ClassicMerchantSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!, this, rect, text ?? button.Text);
        button.FocusMode = FocusModeEnum.None;
        ClassicVendorSkin.Font(button);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, new Color("efd9b4"));
        button.AddThemeColorOverride("font_disabled_color", new Color("918576"));
        button.SetMeta("service_expected_rect", rect);
    }
    private static StyleBoxTexture InventoryCell()
    {
        bool ka = Plugin.Kit.Nation == 1;
        var box = new StyleBoxTexture { Texture = Plugin.Kit.Texture(ka ? "ui_ka_inven_us.png" : "ui_el_inven_us.png"),
            RegionRect = new Rect2(ka ? 159 : 157, ka ? 326 : 333, 51, 52) };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
        { box.SetTextureMargin(side, 4); box.SetContentMargin(side, 0); }
        box.ExpandMarginLeft = 4; box.ExpandMarginTop = 3; box.ExpandMarginRight = 2; box.ExpandMarginBottom = 4;
        return box;
    }
    private void Slot(Control control, Rect2 rect)
    {
        Place(control, rect); _slots.Add(control);
        control.AddThemeStyleboxOverride("panel", _cell);
        // A Control overlay avoids PanelContainer recentering count labels independently of the icon.
        var children = control.GetChildren().OfType<Control>().ToArray();
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore }; control.AddChild(overlay);
        foreach (var icon in children.OfType<TextureRect>())
        {
            icon.Reparent(overlay, false); icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            icon.OffsetLeft = icon.OffsetTop = 2; icon.OffsetRight = icon.OffsetBottom = -2;
            icon.StretchMode = TextureRect.StretchModeEnum.Scale;
        }
        foreach (var count in children.OfType<Label>().Where(l => l.HorizontalAlignment == HorizontalAlignment.Right)) ItemCountStyle.Apply(count, overlay);
        control.MoveChild(overlay, 0);
        if (control is ItemSlotView item) control.AddChild(new ClassicServiceSelection(item));
    }
    private void Warp()
    {
        var art = Plugin.Kit.Layout("{nation}_warp_us");
        var scroll = Find<ScrollContainer>("warp_list_scroll"); Place(scroll, new Rect2(17, 39, 286, 185));
        _warpList = scroll;
        scroll.GetChildren().OfType<VBoxContainer>().Single().AddThemeConstantOverride("separation", 0);
        Scroll(scroll);
        var image = Find<TextureRect>("warp_image"); Place(image, new Rect2(17, 241, 99, 114));
        image.Visible = false;
        var levels = Find<Label>("warp_levels"); Place(levels, new Rect2(17, 384, 125, 17)); WarpFont(levels);
        var descriptionScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; AddChild(descriptionScroll);
        descriptionScroll.Position = new Vector2(15, 243); descriptionScroll.Size = new Vector2(290, 135);
        Scroll(descriptionScroll);
        var descriptionInset = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        descriptionInset.AddThemeConstantOverride("margin_right", 16); descriptionScroll.AddChild(descriptionInset);
        var desc = Find<Label>("warp_description"); desc.Reparent(descriptionInset, false); desc.Visible = true;
        desc.CustomMinimumSize = Vector2.Zero; desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart; WarpFont(desc, true);
        desc.AddThemeColorOverride("font_color", new Color("ff7979"));
        desc.ClipText = false;
        var gold = Find<Label>("warp_gold"); Place(gold, new Rect2(146, 384, 157, 17)); WarpFont(gold, true); gold.HorizontalAlignment = HorizontalAlignment.Right;
        var status = Find<Label>("warp_status"); Place(status, new Rect2(17, 408, 286, 15)); WarpFont(status, true);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        void WarpButton(Button button, string id, string text)
        {
            var node = art.Find(id)!;
            ClassicMerchantSkin.Button(button, node, this, new Rect2(node.Position, node.SizeVec), text);
            WarpFont(button, true, 15); button.FocusMode = FocusModeEnum.None;
            button.SetMeta("service_expected_rect", new Rect2(node.Position, node.SizeVec));
        }
        WarpButton(Find<Button>("warp_travel"), "Btn_Ok", "OK");
        WarpButton(_native.OfType<Button>().Single(b => b.Text == "Close"), "Btn_Cancel", "Cancel");
        Watch(scroll, row =>
        {
            if (row is Label label) { WarpFont(label); label.CustomMinimumSize = Vector2.Zero; }
            if (row is MarginContainer margin) foreach (string side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 0);
            if (row is PanelContainer panel)
            {
                panel.AddThemeStyleboxOverride("panel", _warpEmpty);
                panel.AddChild(new ClassicWarpSelection(panel));
            }
        });
    }
    private void Seal()
    {
        Slot(Find<Control>("seal_socket"), new Rect2(14, 66, 45, 45));
        Place(Find<Label>("seal_headline"), new Rect2(72, 62, 280, 43));
        Place(Find<Label>("seal_prompt"), new Rect2(72, 105, 280, 22));
        _secretRow = Find<Control>("seal_code_row"); _secretField = Find<Control>("seal_code");
        _secretCaption = Caption("Secret answer", new Rect2(14, 142, 160, 22));
        Place(_secretField, new Rect2(183, 141, 169, 26)); ClassicVendorSkin.Input((LineEdit)_secretField);
        var pad = Find<Control>("seal_keypad"); Place(pad, new Rect2(65, 177, 236, 173), true);
        pad.ZIndex = 2; Watch(pad, StyleDynamic);
        Button(Find<Button>("seal_confirm"), new Rect2(14, 180, 156, 29));
        Button(_native.OfType<Button>().Single(b => b.Text == "Cancel"), new Rect2(196, 180, 156, 29));
        Caption("Inventory", new Rect2(14, 225, 338, 20));
        for (int i = 0; i < 28; i++) Slot(Find<Control>("seal_bag_" + i), ClassicServiceLayout.Cell(i, 7, 13, 253));
        Place(Find<Label>("seal_gold"), new Rect2(183, 464, 169, 24));
        Caption("Right-click an item to select it.", new Rect2(14, 503, 338, 22));
    }
    private void Piece()
    {
        Window.SetMeta("piece_full_inventory", true);
        Caption("Piece", new Rect2(14, 58, 88, 20));
        Caption("Reward", new Rect2(134, 58, 218, 20));
        Slot(Find<Control>("piece_socket"), new Rect2(28, 83, 45, 45));
        for (int i = 0; i < 3; i++) Slot(Find<Control>("piece_reward_" + i), new Rect2(142 + i * 64, 83, 45, 45));
        Place(Find<Label>("piece_message"), new Rect2(14, 141, 338, 38));
        Place(Find<Label>("piece_status"), new Rect2(14, 181, 338, 22));
        Button(Find<Button>("piece_start"), new Rect2(14, 213, 102, 29));
        Button(Find<Button>("piece_stop"), new Rect2(132, 213, 102, 29));
        Button(Find<Button>("piece_talk"), new Rect2(250, 213, 102, 29));
        Caption("Exchange pieces", new Rect2(14, 256, 338, 20));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; AddChild(scroll);
        scroll.Position = new Vector2(9, 282); scroll.Size = new Vector2(348, 236); Scroll(scroll);
        // Inventory borders extend outside their hit rectangles, so leave room inside the clipping viewport.
        var inset = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        inset.AddThemeConstantOverride("margin_left", 4); inset.AddThemeConstantOverride("margin_right", 4);
        inset.AddThemeConstantOverride("margin_top", 3); inset.AddThemeConstantOverride("margin_bottom", 4);
        scroll.AddChild(inset);
        var bag = Find<Control>("piece_bag"); bag.Reparent(inset, false); bag.Visible = true; bag.CustomMinimumSize = Vector2.Zero;
        bag.SizeFlagsHorizontal = SizeFlags.ExpandFill; Watch(bag, StyleDynamic);
    }
    private void Combine()
    {
        Caption("Result", new Rect2(14, 57, 338, 20));
        Slot(Find<ItemSlotView>("combine_result"), new Rect2(160, 82, 45, 45));
        Caption("Combination materials", new Rect2(14, 144, 247, 20));
        Caption("Shadow", new Rect2(281, 144, 71, 20));
        foreach (var slot in _native.OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("combine_material_")))
            Slot(slot, slot.Index < 10 ? ClassicServiceLayout.Cell(slot.Index, 5, 14, 170) : new Rect2(298, 170, 45, 45));
        var strip = Find<DetailStrip>("combine_detail");
        Place(strip.Title, new Rect2(14, 276, 338, 24)); Place(strip.Sub, new Rect2(14, 302, 338, 35)); strip.Sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Button(_native.OfType<Button>().Single(b => b.Text == "Formula"), new Rect2(14, 343, 102, 29));
        Button(_native.OfType<Button>().Single(b => b.Text == "Cancel"), new Rect2(132, 343, 102, 29));
        Button(Find<Button>("combine_action"), new Rect2(250, 343, 102, 29));
        var footer = Find<FooterBand>("combine_footer");
        var status = CharacterDetailsSkin.Tree(footer).OfType<StatusLabel>().Single(); status.Hint = "";
        Place(status, new Rect2(14, 380, 338, 22));
    }
    private void RecipeBook()
    {
        var columns = _native.OfType<HBoxContainer>().First(c => c.GetChildren().OfType<PanelContainer>().Count() == 3);
        Place(columns, new Rect2(14, 57, 762, 410));
        int index = 0;
        foreach (var section in columns.GetChildren().OfType<PanelContainer>())
        {
            int width = new[] { 206, 248, 292 }[index++];
            section.CustomMinimumSize = new Vector2(width, 0);
            section.AddThemeStyleboxOverride("panel", new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 4 });
            var scroll = CharacterDetailsSkin.Tree(section).OfType<ScrollContainer>().Single();
            var list = scroll.GetChildren().OfType<VBoxContainer>().Single();
            list.CustomMinimumSize = new Vector2(width - 22, 0);
        }
        foreach (var scroll in CharacterDetailsSkin.Tree(columns).OfType<ScrollContainer>()) Scroll(scroll);
        Watch(columns, StyleDynamic);
    }
    private void StyleDynamic(Control control)
    {
        if (control is Label label && label.HorizontalAlignment != HorizontalAlignment.Right)
        {
            ClassicVendorSkin.Font(label);
            label.ClipText = false;
            if (label.GetParent() is HBoxContainer)
            {
                label.ClipText = false;
                label.SizeFlagsVertical = SizeFlags.Fill;
                if (label.AutowrapMode != TextServer.AutowrapMode.Off) label.CustomMinimumSize = new Vector2(1, 34);
            }
        }
        if (control is Button button)
        {
            ClassicVendorSkin.Button(button);
            if (button.HasMeta("service_selected") && button.GetMeta("service_selected").AsBool() && button.GetThemeStylebox("normal") is StyleBoxTexture source)
            {
                var selected = (StyleBoxTexture)source.Duplicate(); selected.ModulateColor = new Color(1, .78f, .45f);
                button.AddThemeStyleboxOverride("normal", selected);
            }
        }
        if (control is PanelContainer panel && control.GetType().Name == "UpgradeBackpackCell")
        {
            panel.CustomMinimumSize = new Vector2(45, 45); panel.AddThemeStyleboxOverride("panel", _cell);
            var children = panel.GetChildren().OfType<Control>().ToArray();
            var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore }; panel.AddChild(overlay);
            foreach (var icon in children.OfType<TextureRect>())
            {
                icon.Reparent(overlay, false); icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                icon.OffsetLeft = icon.OffsetTop = 2; icon.OffsetRight = icon.OffsetBottom = -2;
                icon.StretchMode = TextureRect.StretchModeEnum.Scale;
            }
            foreach (var count in children.OfType<Label>()) ItemCountStyle.Apply(count, overlay);
            panel.MoveChild(overlay, 0);
        }
        if (control is ItemSlotView item) item.AddThemeStyleboxOverride("panel", _cell);
    }
    private static void Scroll(ScrollContainer scroll)
    {
        var track = new StyleBoxFlat { BgColor = new Color("171715"), BorderColor = new Color("bfa777") };
        track.SetBorderWidthAll(1); track.SetContentMarginAll(0);
        var bar = scroll.GetVScrollBar(); bar.AddThemeStyleboxOverride("scroll", track);
        foreach (var state in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
        {
            var box = ClassicDesign.ButtonBox(state == "grabber_highlight" ? "hover" : state == "grabber_pressed" ? "pressed" : "normal");
            box.SetContentMarginAll(0); bar.AddThemeStyleboxOverride(state, box);
        }
        bar.CustomMinimumSize = new Vector2(12, 0); bar.FocusMode = FocusModeEnum.None;
    }
    private static void Watch(Control root, Action<Control> style)
    {
        void Visit(Node node)
        {
            if (node.HasMeta("classic_service_watch")) return;
            node.SetMeta("classic_service_watch", true);
            if (node is Control control) style(control);
            node.ChildEnteredTree += child => Callable.From(() => { if (GodotObject.IsInstanceValid(child)) Visit(child); }).CallDeferred();
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(root);
    }
    public override void _Process(double delta)
    {
        if (_warpList != null)
            foreach (var row in CharacterDetailsSkin.Tree(_warpList).OfType<PanelContainer>())
            {
                if (row.GetThemeStylebox("panel") != _warpEmpty) row.AddThemeStyleboxOverride("panel", _warpEmpty);
            }
        foreach (var slot in _slots) if (slot.GetThemeStylebox("panel") != _cell) slot.AddThemeStyleboxOverride("panel", _cell);
        if (_secretField != null && _secretRow != null) _secretField.Visible = _secretRow.Visible;
        if (_secretCaption != null && _secretRow != null) _secretCaption.Visible = _secretRow.Visible;
        if (Window.Id == "class_change")
            foreach (var button in _native.OfType<Button>().Where(b => b.Text.StartsWith("Redistribute ")))
                button.Disabled = Window.GetMeta("redistribution_pending", false).AsBool();
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (Window.Id != "warp" || !Window.IsVisibleInTree() || GetViewport().GuiGetFocusOwner() is LineEdit) return;
        if (ev is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode is not (Key.Enter or Key.KpEnter)) return;
        var travel = Find<Button>("warp_travel");
        if (!travel.Disabled) travel.EmitSignal(BaseButton.SignalName.Pressed);
        GetViewport().SetInputAsHandled();
    }
    public override void _Draw()
    {
        if (Window.Id is not ("warp" or "class_change")) return;
        var art = Plugin.Kit.Layout(Window.Id == "warp" ? "{nation}_warp_us" : "co_change_us");
        DrawRect(new Rect2(3, 3, Size.X - 6, Size.Y - 6), Colors.Black);
        foreach (var piece in art.Children.Where(n => n.IsImage))
            DrawTextureRectRegion(Plugin.Kit.Texture(piece.Texture!), new Rect2(piece.Position, piece.SizeVec), new Rect2(piece.SrcX, piece.SrcY, piece.SrcW, piece.SrcH));
    }
}

public partial class ClassicWarpSelection : Control
{
    private readonly PanelContainer _row;
    private bool _selected;
    public ClassicWarpSelection(PanelContainer row) { _row = row; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Process(double delta)
    {
        bool selected = _row.GetMeta("service_selected", false).AsBool();
        if (_selected != selected) { _selected = selected; QueueRedraw(); }
    }
    public override void _Draw()
    {
        if (_selected) DrawRect(new Rect2(Vector2.Zero, Size - Vector2.One), new Color("00ff00"), false, 1);
    }
}

public partial class ClassicServiceSelection : Control
{
    private readonly ItemSlotView _cell;
    private SlotLook _look;
    public ClassicServiceSelection(ItemSlotView cell) { _cell = cell; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Process(double delta)
    {
        if (Size != _cell.Size) { Size = _cell.Size; QueueRedraw(); }
        if (_look != _cell.Look) { _look = _cell.Look; QueueRedraw(); }
    }
    public override void _Draw()
    {
        if (_cell.Look == SlotLook.Selected) DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color("ffe094"), false, 1);
    }
}
