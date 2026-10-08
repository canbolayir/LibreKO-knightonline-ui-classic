using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicPetBarSkin
{
    public static void Apply(Control root)
    {
        if (!root.HasMeta("pet_bar_controls") || root.HasMeta("classic_pet_bar")) return;
        root.SetMeta("classic_pet_bar", true);
        root.SetMeta("native_drag_handle_only", true);
        root.SetMeta("hud_default_position", Callable.From(() => DefaultPosition(root)));
        root.Size = root.CustomMinimumSize = ClassicPetBarLayout.Size;
        root.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        if (root is PanelContainer panel) panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var controls = CharacterDetailsSkin.Tree(root).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => controls.OfType<T>().Single(c => c.Name == name);
        var body = new Control { Name = "classic_pet_bar_body", Size = ClassicPetBarLayout.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(body);
        body.AddChild(new ClassicPetBarArtwork { Name = "pet_bar_artwork", Size = ClassicPetBarLayout.Size });
        void Place(Control control, Rect2 rect)
        {
            control.Reparent(body, false); control.CustomMinimumSize = Vector2.Zero;
            control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft); control.Position = rect.Position; control.Size = rect.Size;
            control.SetMeta("pet_bar_expected_rect", rect);
        }
        var grip = Find<Control>("pet_bar_grip"); Place(grip, ClassicPetBarLayout.Grip);
        grip.SelfModulate = new Color(1, 1, 1, 0);
        var cells = new[] { Find<PanelContainer>("pet_bar_attack") }.Concat(Enumerable.Range(0, 8).Select(i => Find<PanelContainer>("pet_bar_skill_" + i))).ToArray();
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i]; Place(cell, i == 0 ? ClassicPetBarLayout.Attack : ClassicPetBarLayout.Skill(i - 1));
            cell.SetMeta("tooltip_builder", Callable.From<string, Control>(SkillTooltip));
            var frame = new StyleBoxEmpty(); cell.SetMeta("slot_frame", frame); cell.AddThemeStyleboxOverride("panel", frame);
            var overlay = new Control { Name = "pet_bar_slot_overlay", MouseFilter = Control.MouseFilterEnum.Ignore }; cell.AddChild(overlay);
            foreach (var child in cell.GetChildren().OfType<Control>().Where(c => c != overlay).ToArray())
            { child.Reparent(overlay, false); child.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); }
        }
        var page = Find<Button>("pet_bar_page"); Place(page, ClassicPetBarLayout.Page);
        page.FocusMode = Control.FocusModeEnum.None;
        page.AddThemeFontOverride("font", Plugin.Kit.Bold); page.AddThemeFontSizeOverride("font_size", 13);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            var frame = new StyleBoxEmpty(); page.AddThemeStyleboxOverride(state, frame);
            page.AddThemeColorOverride("font_color" + (state == "normal" ? "" : "_" + state), HotbarVisuals.Ink);
        }
        page.AddChild(new ClassicPetPageArtwork { Name = "pet_bar_page_arrows", Size = page.Size });
        page.Size = ClassicPetBarLayout.Page.Size;
        var nativeRow = Find<HBoxContainer>("pet_bar_native_row");
        nativeRow.Reparent(body, false); nativeRow.Visible = false;
        root.UpdateMinimumSize();
        root.Size = ClassicPetBarLayout.Size;
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(root)) return;
            foreach (var control in body.GetChildren().OfType<Control>().Where(c => c.HasMeta("pet_bar_expected_rect")))
            {
                var rect = control.GetMeta("pet_bar_expected_rect").AsRect2();
                control.Position = rect.Position; control.Size = rect.Size;
            }
            root.Size = ClassicPetBarLayout.Size;
        }).CallDeferred();
    }

    private static Vector2 DefaultPosition(Control root)
    {
        var room = root.GetViewportRect().Size;
        var hotbar = CharacterDetailsSkin.Tree(root.GetTree().Root).OfType<HotkeyBar>().FirstOrDefault(c => c.IsVisibleInTree());
        if (hotbar != null && hotbar.Size.X > hotbar.Size.Y)
        {
            var rect = hotbar.GetGlobalRect();
            float x = Mathf.Clamp(rect.Position.X, 0, Mathf.Max(0, room.X - ClassicPetBarLayout.Size.X));
            float y = rect.Position.Y - ClassicPetBarLayout.Size.Y - 6;
            if (y < 0 && rect.End.Y + 6 + ClassicPetBarLayout.Size.Y <= room.Y) y = rect.End.Y + 6;
            return new Vector2(Mathf.Round(x), Mathf.Round(Mathf.Clamp(y, 0, Mathf.Max(0, room.Y - ClassicPetBarLayout.Size.Y))));
        }
        return new Vector2(Mathf.Round((room.X - ClassicPetBarLayout.Size.X) / 2), room.Y - Taskbar.BarHeight - ClassicPetBarLayout.Size.Y - 12);
    }

    private static Control SkillTooltip(string text)
    {
        var panel = new PanelContainer { Name = "classic_pet_bar_tooltip", MouseFilter = Control.MouseFilterEnum.Ignore };
        var frame = new StyleBoxFlat { BgColor = new Color("090907"), BorderColor = new Color("75633d") };
        frame.SetBorderWidthAll(1); frame.SetContentMarginAll(8); panel.AddThemeStyleboxOverride("panel", frame);
        var label = new Label { Name = "pet_bar_tooltip_text", Text = text, CustomMinimumSize = new Vector2(260, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", Plugin.Kit.Bold); label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color("e7dfcc")); panel.AddChild(label); return panel;
    }
}

public partial class ClassicPetBarArtwork : Control
{
    private readonly LayoutNode _source = Plugin.Kit.Layout("{nation}_hotkey_us");
    private readonly StyleBoxTexture _cell;
    public ClassicPetBarArtwork() { _cell = HotbarVisuals.Cell(_source); MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        var canvas = GetCanvasItem();
        _cell.Draw(canvas, new Rect2(28, 2, 36, 36));
        for (int i = 0; i < 8; i++) _cell.Draw(canvas, new Rect2(68 + i * 36, 2, 36, 36));
        _cell.Draw(canvas, new Rect2(356, 2, 28, 36));
        var image = _source.Images.First(); var texture = Plugin.Kit.Texture(image.Texture!);
        if (texture != null) DrawTextureRectRegion(texture, new Rect2(0, 6, 28, 28), new Rect2(image.SrcX + 12, image.SrcY, 29, 30));
    }
}

public partial class ClassicPetPageArtwork : Control
{
    private readonly Texture2D? _up = ClassicChatControls.Atlas(Plugin.Kit.Layout("{nation}_hotkey_us").Find("btn_up")!.Images.First());
    private readonly Texture2D? _down = ClassicChatControls.Atlas(Plugin.Kit.Layout("{nation}_hotkey_us").Find("btn_down")!.Images.First());
    public ClassicPetPageArtwork() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        if (_up != null) DrawTextureRect(_up, new Rect2(4, 0, 16, 8), false);
        if (_down != null) DrawTextureRect(_down, new Rect2(4, 24, 16, 8), false);
    }
}
