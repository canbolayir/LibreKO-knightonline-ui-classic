using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicRebirthSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static void Apply(Control body)
    {
        Node? node = body;
        while (node != null && node is not HudWindow) node = node.GetParent();
        if (node is not HudWindow window || window.HasMeta("classic_rebirth")
            || window.GetMeta("classic_rebirth_controls", 0).AsInt32() != 1) return;
        window.SetMeta("classic_rebirth", true);
        window.AddChild(new ClassicRebirthPanel(window, body));
        window.ResetSize();
    }
}

public partial class ClassicRebirthPanel : Control
{
    private readonly Button _close;
    public ClassicRebirthPanel(HudWindow window, Control body)
    {
        Name = "classic_rebirth";
        Size = CustomMinimumSize = ClassicRebirthLayout.Size;
        TextureFilter = TextureFilterEnum.Nearest;
        var controls = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Native<T>(string name) where T : Control => controls.OfType<T>().Single(c => c.Name == name);
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        var title = header.GetChildren().OfType<Label>().Last();
        _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black });
        ClassicPartySkin.Drag(this, header, new Rect2(14, 8, Size.X - 62, 32));
        Place(title, new Rect2(22, 14, Size.X - 76, 24));
        title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, new Rect2(Size.X - 38, 14, 24, 24), "");
        Place(Native<Label>("rebirth_level"), ClassicRebirthLayout.Level);
        Place(Native<Label>("rebirth_points"), ClassicRebirthLayout.Points);
        Heading("Stat", new Rect2(16, 110, 74, 22));
        Heading("Current", new Rect2(104, 110, 66, 22));
        Heading("Add", new Rect2(206, 110, 87, 22));
        Heading("Total", new Rect2(306, 110, 58, 22));
        for (int row = 0; row < 5; row++)
        {
            Place(Native<Label>("rebirth_stat_" + row), ClassicRebirthLayout.Stat(row));
            Place(Native<Label>("rebirth_current_" + row), ClassicRebirthLayout.Current(row));
            Place(Native<Label>("rebirth_picked_" + row), ClassicRebirthLayout.Picked(row));
            Place(Native<Label>("rebirth_total_" + row), ClassicRebirthLayout.Total(row));
            foreach (string field in new[] { "current", "picked", "total" })
                Native<Label>("rebirth_" + field + "_" + row).HorizontalAlignment = HorizontalAlignment.Center;
            Arrow(Native<Button>("rebirth_remove_" + row), ClassicRebirthLayout.Remove(row), true);
            Arrow(Native<Button>("rebirth_add_" + row), ClassicRebirthLayout.Add(row), false);
            Native<Label>("rebirth_total_" + row).AddThemeColorOverride("font_color", ClassicDesign.Heading);
            Native<Label>("rebirth_current_" + row).AddThemeColorOverride("font_color", ClassicDesign.Muted);
        }
        var status = Native<Label>("rebirth_status");
        Place(status, ClassicRebirthLayout.Status);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.HorizontalAlignment = HorizontalAlignment.Left;
        status.VerticalAlignment = VerticalAlignment.Center;
        status.CustomMaximumSize = ClassicRebirthLayout.Status.Size;
        status.Size = ClassicRebirthLayout.Status.Size;
        status.ClipContents = true;
        var art = Plugin.Kit.Layout("co_change_bill_us");
        Action(Native<Button>("rebirth_accept"), art.Find("btn_ok")!, ClassicRebirthLayout.Accept, "Rebirth");
        Action(Native<Button>("rebirth_cancel"), art.Find("btn_cancel")!, ClassicRebirthLayout.Cancel, "Not yet");
        foreach (int y in new[] { 104, 134, 319 })
            AddChild(new ColorRect { Color = new Color("6d5c3d"), Position = new Vector2(16, y), Size = new Vector2(Size.X - 32, 1), MouseFilter = MouseFilterEnum.Ignore });
    }
    private void Place(Control control, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, this, rect);
        control.AddThemeFontOverride("font", Plugin.Kit.Bold);
        control.AddThemeFontSizeOverride("font_size", 13);
        control.AddThemeConstantOverride("outline_size", 0);
        control.SetMeta("rebirth_expected_rect", rect);
        if (control is Label label) label.VerticalAlignment = VerticalAlignment.Center;
    }
    private void Heading(string text, Rect2 rect)
    {
        var label = new Label { Text = text, HorizontalAlignment = text == "Stat" ? HorizontalAlignment.Left : HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); Place(label, rect);
        label.AddThemeColorOverride("font_color", ClassicDesign.Heading);
    }
    private void Action(Button button, LayoutNode art, Rect2 rect, string text)
    {
        ClassicMerchantSkin.Button(button, art, this, rect, text);
        Place(button, rect);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, new Color("efd9b4"));
        button.AddThemeColorOverride("font_disabled_color", ClassicDesign.Muted);
    }
    private void Arrow(Button button, Rect2 rect, bool previous)
    {
        ClassicMerchantSkin.Button(button, Plugin.Kit.Layout("co_charactercreate_us").Find(previous ? "btn_face_left" : "btn_face_right")!, this, rect, "");
        Place(button, rect);
        button.TooltipText = previous ? "Remove bonus point" : "Add bonus point";
        if (button.GetThemeStylebox("disabled") is StyleBoxTexture texture)
        {
            var dim = (StyleBoxTexture)texture.Duplicate(); dim.ModulateColor = new Color(1, 1, 1, .45f);
            button.AddThemeStyleboxOverride("disabled", dim);
        }
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode is Key.Enter or Key.KpEnter)
        { GetViewport().SetInputAsHandled(); return; }
        if (key.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled();
        _close.EmitSignal(BaseButton.SignalName.Pressed);
    }
}
