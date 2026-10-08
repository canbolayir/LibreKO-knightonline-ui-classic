using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicPetHatchSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static void Apply(Control body)
    {
        Node? node = body; while (node != null && node is not HudWindow) node = node.GetParent();
        if (node is not HudWindow window || window.HasMeta("classic_pet_hatch") || !window.HasMeta("classic_pet_hatch_controls")) return;
        window.SetMeta("classic_pet_hatch", true); window.AddChild(new ClassicPetHatchPanel(window, body)); window.ResetSize();
    }
}

public partial class ClassicPetHatchPanel : Control
{
    private readonly Button _close;
    private readonly Button _accept;
    private readonly ServiceTabs _tabs;
    private readonly Control[][] _pages;
    public ClassicPetHatchPanel(HudWindow window, Control body)
    {
        Name = "classic_pet_hatch"; Size = CustomMinimumSize = ClassicPetHatchLayout.Size; TextureFilter = TextureFilterEnum.Nearest;
        var native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => native.OfType<T>().Single(c => c.Name == name);
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        var title = header.GetChildren().OfType<Label>().Last();
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black });
        ClassicPartySkin.Drag(this, header, new Rect2(14, 8, Size.X - 62, 32));
        Place(title, ClassicPetHatchLayout.Title); title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        Action(_close, new Rect2(Size.X - 38, 14, 24, 24), Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, "");
        _tabs = Find<ServiceTabs>("pet_hatch_tabs");
        var buttons = _tabs.GetChildren().OfType<Button>().ToArray();
        for (int i = 0; i < 2; i++) Action(buttons[i], ClassicPetHatchLayout.Tab(i), Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!, buttons[i].Text);
        Place(Find<Label>("pet_hatch_description"), ClassicPetHatchLayout.Description);
        var hatch = new List<Control>(); var transform = new List<Control>();
        var frame = CellFrame();
        for (int stage = 0; stage < 3; stage++)
        {
            var group = stage == 0 ? hatch : transform;
            var cell = Find<ItemSlotView>("pet_hatch_stage_" + stage); Cell(cell, ClassicPetHatchLayout.Stage(stage), frame); group.Add(cell);
            var heading = Find<Label>("pet_hatch_stage_heading_" + stage); Place(heading, ClassicPetHatchLayout.StageHeading(stage));
            heading.AddThemeColorOverride("font_color", ClassicDesign.Heading); group.Add(heading);
            var pick = Find<Label>("pet_hatch_pick_" + stage); Place(pick, ClassicPetHatchLayout.Pick(stage));
            pick.CustomMaximumSize = ClassicPetHatchLayout.Pick(stage).Size; pick.AutowrapMode = TextServer.AutowrapMode.WordSmart; group.Add(pick);
        }
        var caption = Find<Label>("pet_hatch_name_caption"); Place(caption, ClassicPetHatchLayout.NameCaption); hatch.Add(caption);
        caption.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        var name = Find<LineEdit>("pet_hatch_name"); Place(name, ClassicPetHatchLayout.Name); hatch.Add(name);
        var field = new StyleBoxFlat { BgColor = Colors.Black, BorderColor = new Color("897552") }; field.SetBorderWidthAll(1); field.SetContentMarginAll(4);
        name.AddThemeStyleboxOverride("normal", field); name.AddThemeStyleboxOverride("read_only", field);
        var focus = (StyleBoxFlat)field.Duplicate(); focus.BorderColor = new Color("d7be83"); name.AddThemeStyleboxOverride("focus", focus);
        var note = Find<Label>("pet_transform_note"); Place(note, ClassicPetHatchLayout.TransformNote); note.CustomMaximumSize = ClassicPetHatchLayout.TransformNote.Size;
        note.AddThemeColorOverride("font_color", new Color("b5ab94")); transform.Add(note);
        var inventoryHeading = Find<Label>("pet_hatch_inventory_heading"); Place(inventoryHeading, ClassicPetHatchLayout.InventoryHeading); inventoryHeading.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        for (int i = 0; i < 28; i++) Cell(Find<ItemSlotView>("pet_hatch_inventory_" + i), ClassicPetHatchLayout.Inventory(i), frame);
        var status = Find<Label>("pet_hatch_status"); Place(status, ClassicPetHatchLayout.Status); status.CustomMaximumSize = ClassicPetHatchLayout.Status.Size;
        _accept = Find<Button>("pet_hatch_accept");
        Action(_accept, ClassicPetHatchLayout.Action(true), Plugin.Kit.Layout("co_change_bill_us").Find("btn_ok")!);
        Action(Find<Button>("pet_hatch_cancel"), ClassicPetHatchLayout.Action(false), Plugin.Kit.Layout("co_change_bill_us").Find("btn_cancel")!);
        _pages = new[] { hatch.ToArray(), transform.ToArray() }; _tabs.Selected += ShowPage; ShowPage(_tabs.Current);
    }
    private void ShowPage(int page)
    { for (int i = 0; i < _pages.Length; i++) foreach (var control in _pages[i]) control.Visible = i == page; }
    private void Place(Control control, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, this, rect); control.SetMeta("pet_hatch_expected_rect", rect);
        control.AddThemeFontOverride("font", Plugin.Kit.Bold); control.AddThemeFontSizeOverride("font_size", 13); control.AddThemeConstantOverride("outline_size", 0);
        if (control is Label label) { label.VerticalAlignment = VerticalAlignment.Center; label.ClipContents = true; label.AddThemeConstantOverride("line_spacing", 0); }
    }
    private void Action(Button button, Rect2 rect, LayoutNode art, string? text = null)
    { ClassicMerchantSkin.Button(button, art, this, rect, text ?? button.Text); Place(button, rect); }
    private static StyleBoxTexture CellFrame()
    {
        bool karus = Plugin.Kit.Nation == 1;
        var frame = new StyleBoxTexture { Texture = Plugin.Kit.Texture(karus ? "ui_ka_inven_us.png" : "ui_el_inven_us.png"), RegionRect = new Rect2(karus ? 159 : 157, karus ? 326 : 333, 51, 52) };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom }) { frame.SetTextureMargin(side, 4); frame.SetContentMargin(side, 0); }
        frame.ExpandMarginLeft = 4; frame.ExpandMarginTop = 3; frame.ExpandMarginRight = 2; frame.ExpandMarginBottom = 4; return frame;
    }
    private void Cell(ItemSlotView cell, Rect2 rect, StyleBox frame)
    {
        Place(cell, rect); cell.AddThemeStyleboxOverride("panel", frame); cell.AddChild(new ClassicPetHatchCellSkin(cell, frame));
        var icon = cell.GetChildren().OfType<TextureRect>().Single();
        var overlay = new Control { Name = "pet_hatch_item_overlay", MouseFilter = MouseFilterEnum.Ignore }; cell.AddChild(overlay);
        icon.Reparent(overlay, false); icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.OffsetLeft = icon.OffsetTop = 2; icon.OffsetRight = icon.OffsetBottom = -2; icon.StretchMode = TextureRect.StretchModeEnum.Scale;
        ItemCountStyle.Apply(cell.CountLabel, overlay, Plugin.Kit.Bold);
    }
}

public partial class ClassicPetHatchCellSkin : Control
{
    private readonly ItemSlotView _cell; private readonly StyleBox _frame; private bool _selected;
    public ClassicPetHatchCellSkin(ItemSlotView cell, StyleBox frame) { _cell = cell; _frame = frame; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Process(double delta)
    {
        if (_cell.GetThemeStylebox("panel") != _frame) _cell.AddThemeStyleboxOverride("panel", _frame);
        bool selected = _cell.Look == SlotLook.Selected; if (selected == _selected) return; _selected = selected; QueueRedraw();
    }
    public override void _Draw()
    { if (_selected) DrawRect(new Rect2(Vector2.One * .5f, Size - Vector2.One), new Color("ead292"), false, 1); }
}
