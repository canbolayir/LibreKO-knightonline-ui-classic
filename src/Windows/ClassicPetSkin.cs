using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicPetSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static void Apply(Control body)
    {
        Node? owner = body; while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_pet") || !window.HasMeta("classic_pet_controls")) return;
        window.SetMeta("classic_pet", true); window.AddChild(new ClassicPetPanel(window, body)); window.ResetSize();
    }
}

public partial class ClassicPetPanel : Control
{
    private readonly Button _close;
    private readonly Control[][] _pages;
    private readonly ServiceTabs _tabs;
    private readonly HudWindow _window;
    private readonly Panel _statusPanel;
    private readonly Panel _statusJoin;
    private readonly Label _status;
    private readonly Label _name;
    public ClassicPetPanel(HudWindow window, Control body)
    {
        _window = window;
        Name = "classic_pet"; Size = CustomMinimumSize = ClassicPetLayout.BaseSize; TextureFilter = TextureFilterEnum.Nearest;
        var controls = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => controls.OfType<T>().Single(c => c.Name == name);
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var reference = Plugin.Kit.Layout("co_monster_us");
        AddChild(new TextureRect { Texture = ClassicChatControls.Atlas(reference.Images.First()), Size = new Vector2(286, 457),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore });
        _statusJoin = new Panel { Position = new Vector2(0, 443), Size = new Vector2(286, 14), MouseFilter = MouseFilterEnum.Ignore };
        var joinBorder = new StyleBoxFlat { BgColor = Colors.Black, BorderColor = new Color("a99b81"), BorderWidthLeft = 1, BorderWidthRight = 1 };
        _statusJoin.AddThemeStyleboxOverride("panel", joinBorder); AddChild(_statusJoin);
        _statusPanel = new Panel { Position = new Vector2(0, 457), Size = new Vector2(286, 68), MouseFilter = MouseFilterEnum.Ignore };
        var statusBorder = new StyleBoxFlat { BgColor = Colors.Black, BorderColor = new Color("a99b81") }; statusBorder.SetBorderWidthAll(1);
        _statusPanel.AddThemeStyleboxOverride("panel", statusBorder); AddChild(_statusPanel);
        ClassicPartySkin.Drag(this, header, new Rect2(35, 4, 241, 32));
        Action(_close, new Rect2(9, 16, 20, 20), "", reference.Find("btn_cancel")!);
        Caption("Familiar", new Rect2(126, 56, 148, 26));
        var portrait = Find<FamiliarPortraitView>("pet_portrait");
        Place(portrait, new Rect2(13, 51, 89, 89)); portrait.AddChild(new ClassicFamiliarPortrait(portrait));
        Place(Find<Label>("pet_name"), ClassicPetLayout.Name);
        _name = Find<Label>("pet_name"); _name.AutowrapMode = TextServer.AutowrapMode.Off;
        _name.ClipText = true; _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _name.AddThemeFontSizeOverride("font_size", 11);
        Place(Find<Label>("pet_level"), ClassicPetLayout.Level);
        _pages = new Control[3][];
        _pages[0] = Enumerable.Range(0, 8).Select(i => (Control)Find<Label>("pet_detail_" + i)).ToArray();
        _pages[1] = Enumerable.Range(0, 4).Select(i => (Control)Find<ItemSlotView>("pet_item_" + i))
            .Concat(new Control[] { Find<Button>("pet_feed"), Find<Button>("pet_dismiss") }).ToArray();
        _pages[2] = Enumerable.Range(0, 8).Select(i => (Control)Find<Button>("pet_skill_" + i))
            .Concat(new Control[] { Find<Button>("pet_skill_previous"), Find<Label>("pet_skill_page"), Find<Button>("pet_skill_next") }).ToArray();
        for (int row = 0; row < 4; row++)
        {
            string[] ids = { "pet_hp", "pet_mp", "pet_exp", "pet_satisfaction" };
            string[] captions = { "HP", "MP", "EXP", "Satisfaction" };
            var bar = Find<StatBar>(ids[row]);
            var value = CharacterDetailsSkin.Tree(bar).OfType<Label>().Single(); value.Name = ids[row] + "_value";
            Place(value, ClassicPetLayout.Bar(row)); value.AddThemeFontSizeOverride("font_size", 11);
            value.HorizontalAlignment = HorizontalAlignment.Right; bar.Visible = false;
            Caption(captions[row], new Rect2(17, 165 + row * 15, 94, 14));
        }
        _tabs = Find<ServiceTabs>("pet_pages");
        var tabButtons = _tabs.GetChildren().OfType<Button>().ToArray();
        for (int i = 0; i < 3; i++) Action(tabButtons[i], ClassicPetLayout.Tab(i), tabButtons[i].Text);
        string[] modes = { "pet_attack", "pet_defend", "pet_loot" };
        for (int i = 0; i < 3; i++) Action(Find<Button>(modes[i]), ClassicPetLayout.Mode(i), Find<Button>(modes[i]).Text);
        for (int i = 0; i < 8; i++) Place(_pages[0][i], ClassicPetLayout.Detail(i));
        for (int i = 0; i < 4; i++)
        {
            Place(_pages[1][i], ClassicPetLayout.Item(i));
            var cell = (ItemSlotView)_pages[1][i];
            var frame = new StyleBoxFlat { BgColor = Colors.Black, BorderColor = new Color("a99b81") }; frame.SetBorderWidthAll(1); frame.SetContentMarginAll(0);
            cell.AddThemeStyleboxOverride("panel", frame); cell.AddChild(new ClassicAnvilCellSkin(cell, frame));
            var icon = cell.GetChildren().OfType<TextureRect>().Single();
            var overlay = new Control { Name = "pet_item_overlay", MouseFilter = MouseFilterEnum.Ignore }; cell.AddChild(overlay);
            icon.Reparent(overlay, false);
            icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); icon.OffsetLeft = icon.OffsetTop = 2; icon.OffsetRight = icon.OffsetBottom = -2;
            icon.StretchMode = TextureRect.StretchModeEnum.Scale; ItemCountStyle.Apply(cell.CountLabel, overlay, Plugin.Kit.Bold);
        }
        Action(Find<Button>("pet_feed"), new Rect2(25, 380, 106, 26), "Feed");
        Action(Find<Button>("pet_dismiss"), new Rect2(141, 380, 106, 26), "Dismiss");
        for (int i = 0; i < 8; i++)
        {
            var skill = Find<Button>("pet_skill_" + i); Place(skill, ClassicPetLayout.Skill(i)); skill.ExpandIcon = true;
            skill.AddThemeConstantOverride("icon_max_width", 32);
            var frame = new StyleBoxFlat { BgColor = Colors.Black, BorderColor = new Color("a99b81") }; frame.SetBorderWidthAll(1);
            foreach (string state in new[] { "normal", "disabled", "focus" }) skill.AddThemeStyleboxOverride(state, frame);
            foreach (string state in new[] { "hover", "pressed", "hover_pressed" })
            { var selected = (StyleBoxFlat)frame.Duplicate(); selected.BorderColor = new Color("f0d48e"); skill.AddThemeStyleboxOverride(state, selected); }
        }
        Action(Find<Button>("pet_skill_previous"), new Rect2(83, 399, 18, 18), "", Plugin.Kit.Layout("co_charactercreate_us").Find("btn_face_left")!);
        Place(Find<Label>("pet_skill_page"), new Rect2(108, 399, 70, 18)); Find<Label>("pet_skill_page").HorizontalAlignment = HorizontalAlignment.Center;
        Action(Find<Button>("pet_skill_next"), new Rect2(185, 399, 18, 18), "", Plugin.Kit.Layout("co_charactercreate_us").Find("btn_face_right")!);
        Place(Find<Label>("pet_status"), ClassicPetLayout.Status);
        var status = Find<Label>("pet_status"); status.ClipText = false; status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status = status;
        status.CustomMaximumSize = ClassicPetLayout.Status.Size;
        _tabs.Selected += ShowPage; ShowPage(_tabs.Current);
    }
    public override void _Process(double delta)
    {
        if (_name.TooltipText != _name.Text) _name.TooltipText = _name.Text;
        bool message = _status.Text.Length > 0;
        _status.Visible = message;
        _statusPanel.Visible = _statusJoin.Visible = message;
        var size = message ? ClassicPetLayout.Size : ClassicPetLayout.BaseSize;
        if (CustomMinimumSize == size) return;
        CustomMinimumSize = Size = size; _window.ResetSize();
    }
    private void ShowPage(int page)
    {
        for (int i = 0; i < _pages.Length; i++) foreach (var control in _pages[i]) control.Visible = i == page;
    }
    private static void Font(Control control)
    { control.AddThemeFontOverride("font", Plugin.Kit.Bold); control.AddThemeFontSizeOverride("font_size", 13); control.AddThemeConstantOverride("outline_size", 0); }
    private void Place(Control control, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, this, rect); Font(control); control.SetMeta("pet_expected_rect", rect);
        if (control is Label label) { label.VerticalAlignment = VerticalAlignment.Center; label.ClipContents = true; label.AddThemeConstantOverride("line_spacing", 0); }
    }
    private void Caption(string text, Rect2 rect)
    { var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore }; AddChild(label); Place(label, rect); if (rect.Size.Y < 16) label.AddThemeFontSizeOverride("font_size", 11); label.AddThemeColorOverride("font_color", new Color("e4c58b")); }
    private void Action(Button button, Rect2 rect, string text, LayoutNode? art = null)
    {
        ClassicMerchantSkin.Button(button, art ?? Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!, this, rect, text);
        Place(button, rect);
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled(); _close.EmitSignal(BaseButton.SignalName.Pressed);
    }
}
