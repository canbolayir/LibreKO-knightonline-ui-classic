using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// Classic skin for the item windows of the 2619 review: EXP Seal, Temporary Item List, Loot Filter,
/// Character Seal and the Cypher Ring sheet. Each is a plain-frame Classic window composed from the plugin's
/// existing art (message-box frame, service buttons, inventory cells, chat check boxes) with the native
/// controls reparented onto the declarative rectangles of <see cref="ClassicItemsLayout"/>.
/// </summary>
public static class ClassicItemsSkin
{
    public static readonly string[] WindowIds = NativeItems.WindowIds;
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicItemsPanel? Apply(Control body)
    {
        Node? owner = body;
        while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_items")) return null;
        if (!window.HasMeta(NativeItems.PreparedMeta))
        {
            ClassicSkin.Apply(window);
            return null;
        }
        window.SetMeta("classic_items", true);
        var panel = new ClassicItemsPanel(window, body); window.AddChild(panel); window.ResetSize();
        return panel;
    }
}

public partial class ClassicItemsPanel : Control
{
    public const string ExpectedRect = "items_expected_rect";
    private static readonly Color CaptionInk = new("e2c68b"), ButtonInk = new("efd9b4"), DisabledInk = new("918576");
    private static readonly Color Separator = new("4a4232");

    private static readonly StringName PageMeta = NativeItems.PageMeta, PickedMeta = "cs_picked", ExpStateMeta = "exp_seal_state", ExpectedRectMeta = ExpectedRect;

    public HudWindow Window { get; }
    private readonly ControlIndex _native;
    private readonly ClassicFrame _frame;
    private readonly Label _title;
    private readonly Button _close;
    private readonly StyleBoxTexture _cell;
    private readonly List<ItemSlotView> _cells = new();
    private readonly Dictionary<string, List<Control>> _pages = new();
    private readonly List<Control> _dynamic = new();
    private string _page = "";
    private readonly Label?[] _helpTitles = new Label?[2], _helpTexts = new Label?[2];
    private readonly ScrollContainer?[] _helpScrolls = new ScrollContainer?[2];
    private readonly Button?[] _helpButtons = new Button?[2];
    private float[]? _helpHeights;

    public ClassicItemsPanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_items"; TextureFilter = TextureFilterEnum.Nearest;
        _native = new ControlIndex(CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray());
        _cell = InventoryCell();
        var grip = window.Header!.GetChildren().OfType<HBoxContainer>().First();
        _title = grip.GetChildren().OfType<Label>().Last();
        _close = grip.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _frame = new ClassicFrame { BackgroundAlpha = 1, BackgroundColor = Colors.Black };
        AddChild(_frame);
        Resize(PageOf());
        ClassicPartySkin.Drag(this, grip, new Rect2(16, 8, Size.X - 52, 30));
        _title.HorizontalAlignment = HorizontalAlignment.Left;
        _title.AddThemeColorOverride("font_color", ClassicDesign.FrameTitle);
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, ClassicFrame.Close(Size.X), "");
        switch (window.Id)
        {
            case "exp_seal": ExpSeal(); break;
            case "timed_items": TimedList(); break;
            case "loot_filter": LootFilter(); break;
            case "char_seal": CharacterSeal(); break;
            case "char_seal_sheet": Sheet(); break;
        }
        PlaceFrameControls();
        ShownOnly.Gate(this, Sync);
    }

    private string PageOf() => Window.GetMeta(PageMeta, "").AsString();

    private void Resize(string page)
    {
        _page = page;
        Size = CustomMinimumSize = Window.Id == "char_seal" && page is "" or "help" ? HelpSize : ClassicItemsLayout.Size(Window.Id, page);
        _frame.Size = Size;
    }

    private void PlaceFrameControls()
    {
        Place(_title, ClassicFrame.Title(Size.X));
        _close.Position = ClassicFrame.Close(Size.X).Position;
        _close.SetMeta(ExpectedRect, ClassicFrame.Close(Size.X));
        Window.ResetSize();
        QueueRedraw();
    }

    // Shared Classic pieces --------------------------------------------------------------------

    // Native controls the client may free later (keypad keys) are skipped.
    private T Find<T>(string name) where T : Control => _native.Single<T>(name);
    private T? Maybe<T>(string name) where T : Control => _native.Maybe<T>(name);

    private static void Font(Control control, int size = 12)
    {
        control.AddThemeFontOverride("font", Plugin.Kit.Bold); control.AddThemeFontSizeOverride("font_size", size);
        control.AddThemeConstantOverride("outline_size", 0);
        control.AddThemeColorOverride("font_shadow_color", Colors.Transparent);
    }

    private void Place(Control control, Rect2 rect, string? page = null)
    {
        bool visible = control.Visible;
        ClassicVendorSkin.Move(control, this, rect); Font(control);
        if (control is Label label) { label.VerticalAlignment = VerticalAlignment.Center; label.ClipText = true; }
        if (page != null) { control.Visible = visible; Page(page, control); }
        control.SetMeta(ExpectedRect, rect);
    }

    private void Page(string page, Control control)
    {
        if (!_pages.TryGetValue(page, out var list)) _pages[page] = list = new();
        list.Add(control);
    }

    private Label Caption(string text, Rect2 rect, string? page = null)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore, Name = "caption_" + text.ToLowerInvariant().Replace(' ', '_') };
        AddChild(label); Font(label); label.Position = rect.Position; label.Size = rect.Size;
        // The default font's taller minimum applies until the overrides settle.
        Callable.From(() => label.Size = rect.Size).CallDeferred();
        label.VerticalAlignment = VerticalAlignment.Center; label.ClipText = true;
        label.AddThemeColorOverride("font_color", CaptionInk); label.SetMeta(ExpectedRect, rect);
        if (page != null) Page(page, label);
        return label;
    }

    private void Ink(Label label, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        label.AddThemeColorOverride("font_color", color); label.HorizontalAlignment = align;
    }

    /// <summary>The service windows' button art, light ink on the dark plain frame for both nations.</summary>
    private void Button(Button button, Rect2 rect, string? text = null, string? page = null)
    {
        bool visible = button.Visible;
        ClassicMerchantSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!, this, rect, text ?? button.Text);
        Font(button);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, ButtonInk);
        button.AddThemeColorOverride("font_disabled_color", DisabledInk);
        button.FocusMode = FocusModeEnum.None; button.Icon = null;
        button.SetMeta(ExpectedRect, rect);
        if (page != null) { button.Visible = visible; Page(page, button); }
    }

    private static void Check(CheckBox check)
    {
        Font(check, 12);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            check.AddThemeColorOverride(state, ClassicDarkFrame.Value);
        var art = Plugin.Kit.Layout("{nation}_chat_us").Find("btn_check_normal")!;
        foreach (string state in new[] { "unchecked", "checked", "unchecked_disabled", "checked_disabled" })
            check.AddThemeIconOverride(state, ClassicChatControls.Atlas(art.Images.First(i => i.Tag == (state.StartsWith("unchecked") ? 0 : 1))));
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" }) check.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        check.AddThemeConstantOverride("h_separation", 6);
        check.FocusMode = FocusModeEnum.None;
    }

    private static void Input(LineEdit input)
    {
        Font(input, 12); input.AddThemeColorOverride("font_color", ClassicDarkFrame.Value);
        input.AddThemeColorOverride("font_placeholder_color", ClassicDesign.Muted);
        foreach (string state in new[] { "normal", "focus", "read_only" }) input.AddThemeStyleboxOverride(state, ClassicDarkFrame.InputBox());
    }

    private static StyleBoxTexture InventoryCell()
    {
        bool ka = ClassicDesign.Karus;
        var box = new StyleBoxTexture { Texture = Plugin.Kit.Texture(ka ? "ui_ka_inven_us.png" : "ui_el_inven_us.png"),
            RegionRect = new Rect2(ka ? 159 : 157, ka ? 326 : 333, 51, 52) };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
        { box.SetTextureMargin(side, 4); box.SetContentMargin(side, 0); }
        box.ExpandMarginLeft = 4; box.ExpandMarginTop = 3; box.ExpandMarginRight = 2; box.ExpandMarginBottom = 4;
        return box;
    }

    /// <summary>An inventory-framed item cell: icon inset by 2 px and the inventory's bottom-right count.</summary>
    private void Cell(ItemSlotView cell)
    {
        _cells.Add(cell);
        cell.CustomMinimumSize = Vector2.Zero;
        cell.AddThemeStyleboxOverride("panel", _cell);
        var children = cell.GetChildren().OfType<Control>().ToArray();
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore }; cell.AddChild(overlay);
        foreach (var icon in children.OfType<TextureRect>())
        {
            icon.Reparent(overlay, false); icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            icon.OffsetLeft = icon.OffsetTop = ItemCountStyle.Inset; icon.OffsetRight = icon.OffsetBottom = -ItemCountStyle.Inset;
            icon.StretchMode = TextureRect.StretchModeEnum.Scale;
        }
        ItemCountStyle.Apply(cell.CountLabel, overlay);
        cell.MoveChild(overlay, 0);
    }

    private static void StylePopup(PopupMenu menu)
    {
        var panel = ClassicDarkFrame.InputBox(); panel.BgColor = Colors.Black; panel.SetContentMarginAll(4);
        menu.AddThemeStyleboxOverride("panel", panel);
        var hover = new StyleBoxFlat { BgColor = ClassicDarkFrame.SelectedRow, BorderColor = CaptionInk }; hover.SetBorderWidthAll(1);
        menu.AddThemeStyleboxOverride("hover", hover);
        menu.AddThemeFontOverride("font", Plugin.Kit.Bold); menu.AddThemeFontSizeOverride("font_size", 12);
        menu.AddThemeColorOverride("font_color", ClassicDarkFrame.Value); menu.AddThemeColorOverride("font_hover_color", ClassicDesign.FrameTitle);
    }

    private static void Scroll(ScrollBar bar) => ClassicDarkFrame.ScrollBar(bar, bar.CustomMinimumSize.X > 0 ? bar.CustomMinimumSize.X : 12);

    /// <summary>Styles controls the client builds later (rule rows, keypad keys) as they enter the tree.</summary>
    private void Watch(Node root, Action<Control> style)
    {
        void Visit(Node node)
        {
            if (node is not Control control || control.HasMeta("classic_items_watch")) return;
            control.SetMeta("classic_items_watch", true);
            style(control);
            foreach (var child in node.GetChildren()) Visit(child);
        }
        foreach (var child in root.GetChildren()) Visit(child);
        root.ChildEnteredTree += child => Callable.From(() => { if (GodotObject.IsInstanceValid(child)) Visit(child); }).CallDeferred();
    }

    // EXP Seal ------------------------------------------------------------------------------------

    private void ExpSeal()
    {
        var socket = new Panel { Name = "exp_seal_socket", MouseFilter = MouseFilterEnum.Ignore };
        socket.AddThemeStyleboxOverride("panel", _cell); AddChild(socket);
        socket.Position = ClassicItemsLayout.ExpJar.Position; socket.Size = ClassicItemsLayout.ExpJar.Size;
        var jar = Find<TextureRect>("exp_seal_jar");
        var jarRect = ClassicItemsLayout.ExpJar.Grow(-ItemCountStyle.Inset);
        Place(jar, jarRect); jar.StretchMode = TextureRect.StretchModeEnum.Scale; jar.MouseFilter = MouseFilterEnum.Stop;
        var percent = Find<Label>("exp_seal_percent"); Place(percent, ClassicItemsLayout.ExpPercent); Ink(percent, ClassicDarkFrame.Value);
        var amount = Find<Label>("exp_seal_amount"); Place(amount, ClassicItemsLayout.ExpAmount); Ink(amount, ClassicDarkFrame.Caption);
        if (Maybe<Label>("exp_seal_caption") is { } caption) caption.Visible = false;
        Caption("EXP Seal", ClassicItemsLayout.ExpCaption);
        Button(Find<Button>("exp_seal_on"), ClassicItemsLayout.ExpOn, "ON");
        Button(Find<Button>("exp_seal_off"), ClassicItemsLayout.ExpOff, "OFF");
        foreach (string name in new[] { "exp_seal_on", "exp_seal_off" }) NormalOf(Find<Button>(name));
        var pick = Find<OptionButton>("exp_seal_pick");
        Button(pick, ClassicItemsLayout.ExpPick);
        pick.Alignment = HorizontalAlignment.Left; pick.AddThemeConstantOverride("arrow_margin", 6);
        StylePopup(pick.GetPopup());
        Button(Find<Button>("exp_seal_obtain"), ClassicItemsLayout.ExpObtain);
    }

    private void SyncExpSeal()
    {
        int state = Window.GetMeta(ExpStateMeta, 0).AsInt32();
        foreach (var (name, on) in new[] { ("exp_seal_on", (byte)ExpSealState.On), ("exp_seal_off", (byte)ExpSealState.Off) })
        {
            var button = Find<Button>(name);
            // The native buttons tint the active switch; Classic shows it pressed and keeps the light ink.
            button.Modulate = Colors.White;
            var wanted = state == on ? button.GetThemeStylebox(ThemeNames.Pressed) : NormalOf(button);
            if (button.GetThemeStylebox(ThemeNames.Normal) != wanted) button.AddThemeStyleboxOverride(ThemeNames.Normal, wanted);
        }
    }

    private readonly Dictionary<Button, StyleBox> _normals = new();
    private StyleBox NormalOf(Button button)
    {
        if (!_normals.TryGetValue(button, out var box)) _normals[button] = box = button.GetThemeStylebox("normal");
        return box;
    }

    // Temporary Item List -------------------------------------------------------------------------

    private void TimedList()
    {
        var notice = Find<Label>("timed_notice"); Place(notice, ClassicItemsLayout.TimedNotice);
        notice.AutowrapMode = TextServer.AutowrapMode.WordSmart; notice.ClipText = false; Ink(notice, ClassicDarkFrame.Caption, HorizontalAlignment.Center);
        for (int row = 0; row < LibreKO.Domain.TimedItems.RowsPerPage; row++)
        {
            Find<Control>("timed_row_" + row).Visible = false;
            var cell = Find<ItemSlotView>("timed_cell_" + row); Place(cell, ClassicItemsLayout.TimedCell(row), "rows"); Cell(cell);
            Place(Find<Label>("timed_name_" + row), ClassicItemsLayout.TimedName(row), "rows");
            var place = Find<Label>("timed_place_" + row); Place(place, ClassicItemsLayout.TimedPlace(row), "rows"); place.AddThemeFontSizeOverride("font_size", 11);
            Place(Find<Label>("timed_time_" + row), ClassicItemsLayout.TimedTime(row), "rows");
        }
        var bar = Find<VScrollBar>("timed_scroll"); Place(bar, ClassicItemsLayout.TimedScroll, "rows"); Scroll(bar);
        var hide = Find<Label>("timed_hide_label"); Place(hide, ClassicItemsLayout.TimedHideLabel); Ink(hide, ClassicDarkFrame.Caption, HorizontalAlignment.Right);
        var check = Find<CheckBox>("timed_hide"); Place(check, ClassicItemsLayout.TimedHide); Check(check);
    }

    // Loot Filter ---------------------------------------------------------------------------------

    private void LootFilter()
    {
        var enabled = Find<CheckBox>("loot_enabled"); Place(enabled, ClassicItemsLayout.LootEnabled); Check(enabled);
        Caption("Item groups", ClassicItemsLayout.LootGroupsCaption);
        for (int i = 0; Maybe<CheckBox>("loot_group_" + i) is { } group; i++) { Place(group, ClassicItemsLayout.LootGroup(i)); Check(group); }
        Caption("Equipment grades", ClassicItemsLayout.LootGradesCaption);
        for (int i = 0; Maybe<CheckBox>("loot_grade_" + i) is { } grade; i++)
        {
            // Grades keep their item-grade colours; only the type and the check art become Classic.
            var colour = grade.GetThemeColor("font_color");
            Place(grade, ClassicItemsLayout.LootGrade(i)); Check(grade);
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
                grade.AddThemeColorOverride(state, colour);
        }
        var priceLabel = Find<Label>("loot_price_label"); Place(priceLabel, ClassicItemsLayout.LootPriceLabel); Ink(priceLabel, ClassicDarkFrame.Value);
        var price = Find<SpinBox>("loot_price"); Place(price, ClassicItemsLayout.LootPrice); Input(price.GetLineEdit());
        // A plain Classic number field: typed or wheeled, without the engine's arrow buttons.
        foreach (string icon in new[] { "updown", "up", "down", "up_hover", "down_hover", "up_pressed", "down_pressed", "up_disabled", "down_disabled" })
            price.AddThemeIconOverride(icon, new ImageTexture());
        price.AddThemeConstantOverride("buttons_width", 0);
        price.AddThemeFontOverride("font", Plugin.Kit.Bold);
        Caption("Item rules (right-click an item in a loot box)", ClassicItemsLayout.LootRulesCaption);
        var rules = Find<VBoxContainer>("loot_rules");
        var scroll = new ScrollContainer { Name = "loot_rules_scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll); scroll.Position = ClassicItemsLayout.LootRules.Position; scroll.Size = ClassicItemsLayout.LootRules.Size;
        scroll.SetMeta(ExpectedRect, ClassicItemsLayout.LootRules); Scroll(scroll.GetVScrollBar());
        rules.Reparent(scroll, false); rules.Visible = true; rules.SizeFlagsHorizontal = SizeFlags.ExpandFill; rules.CustomMinimumSize = Vector2.Zero;
        rules.AddThemeConstantOverride("separation", 2);
        Watch(rules, control =>
        {
            if (control is Label label)
            {
                Font(label); label.VerticalAlignment = VerticalAlignment.Center;
                if (label.Text == "No item rules yet.") Ink(label, ClassicDarkFrame.Caption, HorizontalAlignment.Center);
                // Long item names end in an ellipsis inside the list instead of widening it.
                if (label.SizeFlagsHorizontal.HasFlag(SizeFlags.Expand))
                {
                    label.CustomMinimumSize = Vector2.Zero; label.ClipText = true;
                    label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; label.TooltipText = label.Text;
                }
            }
            if (control is HBoxContainer row) row.CustomMinimumSize = new Vector2(0, 22);
            if (control is Button button && control is not CheckBox)
            {
                ClassicReportDesign.StyleButton(button, false, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")); Font(button);
                foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, ButtonInk);
                button.CustomMinimumSize = new Vector2(64, 20); button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            }
        });
    }

    // Character Seal ------------------------------------------------------------------------------

    private void CharacterSeal()
    {
        var help = ClassicItemsLayout.SealHelpDefault;
        for (int i = 0; i < 2; i++)
        {
            var title = Find<Label>("cs_help_title_" + i); Place(title, ClassicItemsLayout.SealHelpTitle(help, i), "help");
            Ink(title, ClassicDesign.FrameTitle, HorizontalAlignment.Center);
            var text = Find<Label>("cs_help_text_" + i);
            var scroll = new ScrollContainer { Name = "cs_help_scroll_" + i, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            AddChild(scroll); var rect = ClassicItemsLayout.SealHelpText(help, i); scroll.Position = rect.Position; scroll.Size = rect.Size;
            scroll.SetMeta(ExpectedRect, rect); Scroll(scroll.GetVScrollBar()); Page("help", scroll);
            text.Reparent(scroll, false); text.CustomMinimumSize = Vector2.Zero; text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart; Font(text); Ink(text, ClassicDarkFrame.Value);
            _helpTitles[i] = title; _helpScrolls[i] = scroll; _helpTexts[i] = text;
        }
        float buttons = ClassicItemsLayout.SealHelpButtonsTop(help);
        Button(_helpButtons[0] = Find<Button>("cs_seal"), ClassicItemsLayout.SealPageLeft(buttons), null, "help");
        Button(_helpButtons[1] = Find<Button>("cs_unseal"), ClassicItemsLayout.SealPageRight(buttons), null, "help");

        var selectTitle = Find<Label>("cs_select_title"); Place(selectTitle, ClassicItemsLayout.SealTitle, "select");
        Ink(selectTitle, ClassicDesign.FrameTitle, HorizontalAlignment.Center);
        for (int i = 0; Maybe<Button>("cs_slot_" + i) is { } slot; i++)
        {
            Place(slot, ClassicItemsLayout.SealSlot(i), "select");
            slot.Alignment = HorizontalAlignment.Left; slot.ClipText = true; slot.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
        var prompt = Find<Label>("cs_select_prompt"); Place(prompt, ClassicItemsLayout.SealPrompt, "select"); Ink(prompt, ClassicDarkFrame.Caption, HorizontalAlignment.Center);
        Button(Find<Button>("cs_select_ok"), ClassicItemsLayout.SealPageLeft(ClassicItemsLayout.SealSelectButtons), null, "select");
        Button(Find<Button>("cs_select_cancel"), ClassicItemsLayout.SealPageRight(ClassicItemsLayout.SealSelectButtons), null, "select");

        var codeTitle = Find<Label>("cs_code_title"); Place(codeTitle, ClassicItemsLayout.SealTitle, "code"); Ink(codeTitle, ClassicDesign.FrameTitle, HorizontalAlignment.Center);
        var stars = Find<Label>("cs_code_stars"); Place(stars, ClassicItemsLayout.SealStars, "code");
        stars.AddThemeStyleboxOverride("normal", ClassicDarkFrame.InputBox()); Ink(stars, ClassicDarkFrame.Value, HorizontalAlignment.Center);
        stars.AddThemeFontSizeOverride("font_size", 16);
        var keys = Find<GridContainer>("cs_keys"); Place(keys, ClassicItemsLayout.SealKeys, "code");
        keys.AddThemeConstantOverride("h_separation", (int)ClassicItemsLayout.SealKeyGap); keys.AddThemeConstantOverride("v_separation", (int)ClassicItemsLayout.SealKeyGap);
        Watch(keys, control =>
        {
            if (control is not Button key) return;
            ClassicMerchantSkin.Button(key, Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!, keys, new Rect2(0, 0, ClassicItemsLayout.SealKey, ClassicItemsLayout.SealKey), key.Text);
            // The grid lays the keys out; keep them in it at the keypad's 40 px.
            if (key.GetParent() != keys) key.Reparent(keys, false);
            key.CustomMinimumSize = new Vector2(ClassicItemsLayout.SealKey, ClassicItemsLayout.SealKey);
            Font(key, 13);
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) key.AddThemeColorOverride(state, ButtonInk);
        });
        Button(Find<Button>("cs_shuffle"), ClassicItemsLayout.SealPageLeft(ClassicItemsLayout.SealCodeEdit), null, "code");
        Button(Find<Button>("cs_delete"), ClassicItemsLayout.SealPageRight(ClassicItemsLayout.SealCodeEdit), null, "code");
        Button(Find<Button>("cs_code_ok"), ClassicItemsLayout.SealPageLeft(ClassicItemsLayout.SealCodeButtons), null, "code");
        Button(Find<Button>("cs_code_cancel"), ClassicItemsLayout.SealPageRight(ClassicItemsLayout.SealCodeButtons), null, "code");
    }

    /// <summary>The help page at the measured height of its two texts (each up to the layout's maximum, then scrolled).</summary>
    private void LayoutHelp()
    {
        var heights = new float[2];
        for (int i = 0; i < 2; i++)
        {
            if (_helpTexts[i] is not { } text || !IsInstanceValid(text) || text.Size.X <= 0) return;
            heights[i] = Mathf.Min(ClassicItemsLayout.SealHelpTextMax, Mathf.Ceil(text.GetCombinedMinimumSize().Y));
        }
        if (_helpHeights != null && heights.SequenceEqual(_helpHeights)) return;
        _helpHeights = heights;
        for (int i = 0; i < 2; i++)
        {
            Move(_helpTitles[i]!, ClassicItemsLayout.SealHelpTitle(heights, i));
            Move(_helpScrolls[i]!, ClassicItemsLayout.SealHelpText(heights, i));
        }
        float buttons = ClassicItemsLayout.SealHelpButtonsTop(heights);
        Move(_helpButtons[0]!, ClassicItemsLayout.SealPageLeft(buttons));
        Move(_helpButtons[1]!, ClassicItemsLayout.SealPageRight(buttons));
        Resize(_page);
        PlaceFrameControls();
    }

    private static void Move(Control control, Rect2 rect)
    {
        control.Position = rect.Position; control.Size = rect.Size; control.SetMeta(ExpectedRectMeta, rect);
    }

    /// <summary>The help page's size: its measured content once the texts are laid out.</summary>
    public Vector2 HelpSize => _helpHeights != null ? ClassicItemsLayout.SealHelpSize(_helpHeights) : ClassicItemsLayout.Size("char_seal");

    private static readonly StringName[] SealRowStates = { ThemeNames.Normal, ThemeNames.Hover, ThemeNames.Pressed, ThemeNames.HoverPressed, ThemeNames.Disabled };
    private StyleBoxFlat? _sealSelected, _sealPlain;
    private readonly StyleBoxEmpty _sealFocus = new();

    private static StyleBoxFlat SealRow(bool selected)
    {
        var row = new StyleBoxFlat { BgColor = selected ? ClassicDarkFrame.SelectedRow : new Color(0, 0, 0, 0),
            BorderColor = selected ? CaptionInk : Separator };
        row.BorderWidthBottom = 1; if (selected) row.SetBorderWidthAll(1);
        row.SetContentMargin(Side.Left, 8); row.SetContentMargin(Side.Right, 8);
        return row;
    }

    /// <summary>
    /// The slot rows' looks are built once and applied when the pick changes or when the client restyled a row: its
    /// refresh resets the normal box and the font colour, which is what the check below looks at.
    /// </summary>
    private void SyncCharacterSeal()
    {
        int picked = Window.GetMeta(PickedMeta, -1).AsInt32();
        _sealSelected ??= SealRow(true);
        _sealPlain ??= SealRow(false);
        for (int i = 0; Maybe<Button>("cs_slot_" + i) is { } slot; i++)
        {
            bool selected = i == picked;
            var row = selected ? _sealSelected : _sealPlain;
            var ink = selected ? ClassicDesign.FrameTitle : ClassicDarkFrame.Value;
            if (slot.GetThemeStylebox(ThemeNames.Normal) == row && slot.HasThemeColorOverride(ThemeNames.FontColor)
                && slot.GetThemeColor(ThemeNames.FontColor) == ink) continue;
            foreach (var state in SealRowStates) slot.AddThemeStyleboxOverride(state, row);
            slot.AddThemeStyleboxOverride(ThemeNames.Focus, _sealFocus);
            slot.AddThemeColorOverride(ThemeNames.FontColor, ink);
            slot.AddThemeColorOverride(ThemeNames.FontHoverColor, ClassicDesign.FrameTitle);
            slot.AddThemeColorOverride(ThemeNames.FontPressedColor, ClassicDesign.FrameTitle);
            slot.AddThemeColorOverride(ThemeNames.FontDisabledColor, ClassicDesign.Muted * new Color(1, 1, 1, .8f));
        }
    }

    // Cypher Ring sheet ---------------------------------------------------------------------------

    private void Sheet()
    {
        var name = Find<Label>("sheet_name"); Place(name, ClassicItemsLayout.SheetName); name.HorizontalAlignment = HorizontalAlignment.Center;
        name.AddThemeFontSizeOverride("font_size", 14);
        foreach (var (id, rect) in new[] { ("sheet_identity", ClassicItemsLayout.SheetIdentity), ("sheet_stats", ClassicItemsLayout.SheetStats), ("sheet_wealth", ClassicItemsLayout.SheetWealth) })
        {
            var block = Find<Container>(id); Place(block, rect);
            foreach (var label in CharacterDetailsSkin.Tree(block).OfType<Label>())
            {
                Font(label); label.VerticalAlignment = VerticalAlignment.Center;
                // Captions in the original caption ink, values in the value ink, as on the Character Report.
                bool caption = label.GetIndex() % 2 == 0;
                label.AddThemeColorOverride("font_color", caption ? ClassicDarkFrame.Caption : ClassicDarkFrame.Value);
            }
        }
        Button(Find<Button>("sheet_achievements_button"), ClassicItemsLayout.SheetAchievementsButton);
        foreach (var (id, rect) in new[] { ("sheet_equip", ClassicItemsLayout.SheetEquip), ("sheet_bag", ClassicItemsLayout.SheetBag) })
        {
            var grid = Find<GridContainer>(id); Place(grid, rect);
            grid.AddThemeConstantOverride("h_separation", (int)ClassicItemsLayout.SheetGap); grid.AddThemeConstantOverride("v_separation", (int)ClassicItemsLayout.SheetGap);
            foreach (var cell in grid.GetChildren().OfType<ItemSlotView>())
            {
                Cell(cell); cell.CustomMinimumSize = new Vector2(ClassicItemsLayout.SheetCell, ClassicItemsLayout.SheetCell);
            }
        }
        var achievements = Find<Label>("sheet_achievements"); Place(achievements, ClassicItemsLayout.SheetAchievements, "achievements");
        achievements.AutowrapMode = TextServer.AutowrapMode.WordSmart; achievements.ClipText = false; achievements.VerticalAlignment = VerticalAlignment.Top;
        Ink(achievements, ClassicDarkFrame.Caption);
    }

    // Frame and state -----------------------------------------------------------------------------

    public override void _Process(double delta) => Sync();

    /// <summary>Keeps the Classic composition on the native state; runs every frame while shown.</summary>
    private void Sync()
    {
        foreach (var cell in _cells) if (cell.GetThemeStylebox(ThemeNames.Panel) != _cell) cell.AddThemeStyleboxOverride(ThemeNames.Panel, _cell);
        if (Window.Id is "char_seal" or "char_seal_sheet")
        {
            string page = PageOf();
            if (page != _page) { Resize(page); PlaceFrameControls(); }
            foreach (var (key, controls) in _pages)
                if (Window.Id == "char_seal") foreach (var control in controls) control.Visible = key == (page == "" ? "help" : page);
        }
        if (Window.Id == "char_seal") { SyncCharacterSeal(); if (_page is "" or "help") LayoutHelp(); }
        if (Window.Id == "exp_seal") SyncExpSeal();
        if (Window.Id == "timed_items")
        {
            for (int row = 0; Maybe<Control>("timed_row_" + row) is { } panel; row++) panel.Visible = false;
            QueueRedraw();
        }
        if (Size != CustomMinimumSize) Size = CustomMinimumSize;
    }

    public override void _Draw()
    {
        if (Window.Id != "timed_items") return;
        // Thin separators between the item rows, as in the Classic lists.
        for (int row = 1; row < LibreKO.Domain.TimedItems.RowsPerPage; row++)
        {
            if (Maybe<Control>("timed_cell_" + row) is not { Visible: true }) continue;
            float y = ClassicItemsLayout.TimedRowTop + row * ClassicItemsLayout.TimedRowPitch - 3;
            DrawRect(new Rect2(16, y, ClassicItemsLayout.TimedScroll.Position.X - 22, 1), Separator);
        }
    }
}

