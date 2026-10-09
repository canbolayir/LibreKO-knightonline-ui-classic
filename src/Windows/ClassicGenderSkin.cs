using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicGenderSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicGenderPanel? Apply(Control body)
    {
        Node? node = body; while (node != null && node is not HudWindow) node = node.GetParent();
        if (node is not HudWindow window || window.HasMeta("classic_gender")) return null;
        if (window.GetMeta("classic_appearance_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_gender", true);
        var panel = new ClassicGenderPanel(window, body); window.AddChild(panel); window.ResetSize(); return panel;
    }
}

public partial class ClassicGenderPanel : Control
{
    private readonly Button _close;
    private readonly ColorPickerButton _colour;
    private readonly Button _accept;
    private readonly bool _beauty;
    private readonly bool _transfer;
    public ClassicGenderPanel(HudWindow window, Control body)
    {
        _beauty = window.Id == "changehair";
        _transfer = window.Id == "nationtransfer";
        Name = _transfer ? "classic_nationtransfer" : _beauty ? "classic_beauty" : "classic_gender";
        Size = CustomMinimumSize = _transfer ? ClassicNationTransferLayout.Size : ClassicGenderLayout.Size; TextureFilter = TextureFilterEnum.Nearest;
        var native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => native.OfType<T>().Single(c => c.Name == name);
        var bar = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        var title = bar.GetChildren().OfType<Label>().Last();
        _close = bar.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black });
        ClassicPartySkin.Drag(this, bar, new Rect2(14, 8, Size.X - 62, 32));
        Place(title, new Rect2(22, 14, Size.X - 76, 24)); title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, new Rect2(Size.X - 38, 14, 24, 24), "");
        Place(Find<LookPreview>("look_preview"), _transfer ? ClassicNationTransferLayout.Preview : ClassicGenderLayout.Preview);
        if (_transfer)
        {
            Place(Find<Label>("transfer_header"), ClassicNationTransferLayout.Header);
            Place(Find<Label>("transfer_characters_heading"), ClassicNationTransferLayout.CharactersHeading);
            Find<Label>("transfer_characters_heading").AddThemeColorOverride("font_color", ClassicDesign.Heading);
            var scroll = Find<ScrollContainer>("transfer_scroll"); Place(scroll, ClassicNationTransferLayout.Characters);
            scroll.CustomMinimumSize = ClassicNationTransferLayout.Characters.Size;
            var rail = scroll.GetVScrollBar(); rail.CustomMinimumSize = new Vector2(12, 0);
            var track = new StyleBoxFlat { BgColor = new Color("17130e"), BorderColor = new Color("8b795c") }; track.SetBorderWidthAll(1);
            rail.AddThemeStyleboxOverride("scroll", track); rail.FocusMode = FocusModeEnum.None;
            foreach (var state in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
            { var box = ClassicDesign.ButtonBox(state == "grabber_highlight" ? "hover" : state == "grabber_pressed" ? "pressed" : "normal"); box.SetContentMarginAll(0); rail.AddThemeStyleboxOverride(state, box); }
            var candidates = Find<VBoxContainer>("transfer_characters"); candidates.ChildEnteredTree += StyleCandidate;
            foreach (var child in candidates.GetChildren()) StyleCandidate(child);
        }
        if (_beauty)
        {
            Place(Find<Label>("look_name"), ClassicBeautyLayout.Name);
            Place(Find<Label>("look_identity"), ClassicBeautyLayout.Identity);
            Find<Label>("look_name").AddThemeColorOverride("font_color", ClassicDesign.Heading);
        }
        var editor = Find<LookEditor>("look_editor"); Place(editor, _transfer ? ClassicNationTransferLayout.Editor : _beauty ? ClassicBeautyLayout.Editor : ClassicGenderLayout.Editor);
        editor.AddThemeConstantOverride("separation", 8);
        var races = Find<VBoxContainer>("look_races"); races.AddThemeConstantOverride("separation", 4);
        races.ChildEnteredTree += StyleRace;
        foreach (var child in races.GetChildren()) StyleRace(child);
        foreach (var label in CharacterDetailsSkin.Tree(editor).OfType<Label>())
        {
            Font(label); label.VerticalAlignment = VerticalAlignment.Center;
            if (label.Name.ToString().EndsWith("_heading")) { label.AddThemeColorOverride("font_color", ClassicDesign.Heading); label.CustomMinimumSize = new Vector2(0, 22); }
        }
        foreach (var name in new[] { "face", "hair" })
        {
            var row = Find<HBoxContainer>("look_" + name + "_row"); row.CustomMinimumSize = new Vector2(0, 26);
            row.AddThemeConstantOverride("separation", 6);
            var caption = row.GetChildren().OfType<Label>().First(); caption.CustomMinimumSize = new Vector2(170, 26);
            foreach (var side in new[] { "previous", "next" }) Arrow(Find<Button>("look_" + name + "_" + side), side == "previous");
            var value = Find<Label>("look_" + name + "_value"); value.CustomMinimumSize = new Vector2(64, 26);
        }
        _colour = Find<ColorPickerButton>("look_colour");
        _colour.CustomMinimumSize = new Vector2(80, 26);
        // Enter belongs to the service, as on the other look buttons: a focused colour button would also open its picker.
        _colour.FocusMode = FocusModeEnum.None;
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" }) _colour.AddThemeStyleboxOverride(state, ClassicDesign.InputBox());
        var picker = _colour.GetPicker();
        picker.ColorModesVisible = false; picker.SlidersVisible = false; picker.SamplerVisible = false; picker.PresetsVisible = false;
        picker.AddThemeFontOverride("font", Plugin.Kit.Bold); picker.AddThemeFontSizeOverride("font_size", 13);
        IEnumerable<Node> PickerTree(Node n) { yield return n; foreach (var child in n.GetChildren(true)) foreach (var item in PickerTree(child)) yield return item; }
        foreach (var control in PickerTree(picker).OfType<Control>())
        {
            if (control is LineEdit input) { ClassicVendorSkin.Input(input); Font(input); }
            else if (control is Label) Font(control);
            else if (control is Button button)
            {
                ClassicDesign.StyleButton(button);
                if (ClassicDesign.Karus)
                    foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color", "icon_disabled_color" })
                        button.AddThemeColorOverride(state, new Color("17130f"));
            }
        }
        _colour.GetPopup().AddThemeStyleboxOverride("panel", ClassicDesign.InputBox());
        var colourCaption = Find<HBoxContainer>("look_colour_row").GetChildren().OfType<Label>().Single(); colourCaption.CustomMinimumSize = new Vector2(170, 26);
        var statusRect = _transfer ? ClassicNationTransferLayout.Status : ClassicGenderLayout.Status;
        Place(Find<Label>("look_status"), statusRect);
        var status = Find<Label>("look_status"); status.VerticalAlignment = VerticalAlignment.Top;
        status.CustomMaximumSize = statusRect.Size; status.ClipContents = true;
        foreach (var entry in new[] { ("look_accept", _transfer ? ClassicNationTransferLayout.Accept : ClassicGenderLayout.Accept, "btn_ok"), ("look_cancel", _transfer ? ClassicNationTransferLayout.Cancel : ClassicGenderLayout.Cancel, "btn_cancel") })
        {
            var button = Find<Button>(entry.Item1);
            ClassicMerchantSkin.Button(button, Plugin.Kit.Layout("co_change_bill_us").Find(entry.Item3)!, this, entry.Item2);
            Font(button); button.SetMeta("look_expected_rect", entry.Item2);
        }
        foreach (var entry in new[] { ("look_turn_left", _transfer ? ClassicNationTransferLayout.TurnLeft : ClassicGenderLayout.TurnLeft, true), ("look_turn_right", _transfer ? ClassicNationTransferLayout.TurnRight : ClassicGenderLayout.TurnRight, false) })
        {
            var button = Find<Button>(entry.Item1); Place(button, entry.Item2); Arrow(button, entry.Item3);
            button.TooltipText = entry.Item3 ? "Turn left" : "Turn right";
        }
        _accept = Find<Button>("look_accept");
        SetProcessUnhandledKeyInput(true);
    }
    private static void Font(Control control)
    {
        control.AddThemeFontOverride("font", Plugin.Kit.Bold); control.AddThemeFontSizeOverride("font_size", 13);
        control.AddThemeConstantOverride("outline_size", 0); control.AddThemeColorOverride("font_color", ClassicDesign.Text);
    }
    private void Place(Control control, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, this, rect); Font(control); control.SetMeta("look_expected_rect", rect);
        if (control is Label label) label.MouseFilter = MouseFilterEnum.Ignore;
    }
    private static void Arrow(Button button, bool previous)
    {
        ClassicVendorSkin.Button(button, Plugin.Kit.Layout("co_charactercreate_us").Find(previous ? "btn_face_left" : "btn_face_right")!);
        button.Text = ""; button.CustomMinimumSize = new Vector2(19, 19); button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        button.FocusMode = FocusModeEnum.None; button.TooltipText = previous ? "Previous" : "Next";
        if (button.GetThemeStylebox("disabled") is StyleBoxTexture texture)
        {
            var dim = (StyleBoxTexture)texture.Duplicate(); dim.ModulateColor = new Color(1, 1, 1, .45f);
            button.AddThemeStyleboxOverride("disabled", dim);
        }
    }
    private static void StyleRace(Node node)
    {
        if (node is not Button button) return;
        ClassicDesign.StyleButton(button, true); button.CustomMinimumSize = new Vector2(0, 26);
        ClassicPartySkin.KarusButtonContrast(button);
        foreach (var state in new[] { "pressed", "hover_pressed" })
            if (button.GetThemeStylebox(state) is StyleBoxTexture texture) texture.ModulateColor = new Color(1, .88f, .62f);
    }
    private static void StyleCandidate(Node node)
    {
        if (node is not Button button) return;
        StyleRace(button);
        button.CustomMinimumSize = new Vector2(0, 52);
        button.ClipText = true;
        button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (_beauty && key.Keycode is Key.Enter or Key.KpEnter && !_colour.GetPopup().Visible)
        {
            GetViewport().SetInputAsHandled();
            if (!_accept.Disabled) _accept.EmitSignal(BaseButton.SignalName.Pressed);
            return;
        }
        if (key.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled();
        if (_colour.GetPopup().Visible) _colour.GetPopup().Hide();
        else _close.EmitSignal(BaseButton.SignalName.Pressed);
    }
}
