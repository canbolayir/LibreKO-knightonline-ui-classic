using Godot;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The familiar plate in the Classic party member style. The client keeps the plate's visibility, tooltip,
/// click (opens the Familiar window) and dragging; the Classic view draws the summoned familiar's state.
/// </summary>
public static class ClassicPetPlateSkin
{
    public static void Apply(Control root)
    {
        if (!root.HasMeta("pet_plate_controls") || root.HasMeta("classic_pet_plate")) return;
        root.SetMeta("classic_pet_plate", true);
        if (root is PanelContainer panel) panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        foreach (var child in root.GetChildren().OfType<Control>()) child.Visible = false;
        root.CustomMinimumSize = ClassicPetPlateLayout.Size;
        root.AddChild(new ClassicPetPlate { Name = "classic_pet_plate" });
        root.Size = ClassicPetPlateLayout.Size;
    }
}

public partial class ClassicPetPlate : Control
{
    private static readonly Color NameInk = new("8080ff"), LevelInk = new("ffff80");
    private readonly LayoutNode _party = Plugin.Kit.Layout("co_party_us");
    private readonly LayoutNode _state = Plugin.Kit.Layout("{nation}_statebar_us");
    private readonly Label _name, _level;
    private (int, int, int, int, int, int, int, string)? _shown;

    public ClassicPetPlate()
    {
        MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest;
        CustomMinimumSize = Size = ClassicPetPlateLayout.Size;
        _name = Caption(ClassicPetPlateLayout.Name, NameInk, HorizontalAlignment.Left); _name.Name = "pet_plate_name";
        _level = Caption(ClassicPetPlateLayout.Level, LevelInk, HorizontalAlignment.Right); _level.Name = "pet_plate_level";
    }

    private Label Caption(Rect2 rect, Color ink, HorizontalAlignment align)
    {
        var label = new Label { Position = rect.Position, Size = rect.Size, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", Plugin.Kit.Bold); label.AddThemeFontSizeOverride("font_size", 10);
        label.AddThemeColorOverride("font_color", ink); label.AddThemeConstantOverride("outline_size", 0);
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", 1); label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.SetMeta("pet_plate_expected_rect", rect);
        AddChild(label);
        return label;
    }

    /// <summary>Theme sizes settle once the captions are in the tree; the declared rectangles win.</summary>
    public override void _Ready()
    {
        foreach (var label in new[] { _name, _level })
        {
            var rect = label.GetMeta("pet_plate_expected_rect").AsRect2();
            label.Position = rect.Position; label.Size = rect.Size;
        }
    }

    private static PetSheet? Pet => Game.Session.Pet.Pet;

    public override void _Process(double delta)
    {
        if (Pet is not { } pet) return;
        var state = (pet.Hp, pet.MaxHp, pet.Mp, pet.MaxMp, pet.Satisfaction, pet.ExpPercent, pet.Level, pet.Name);
        if (Equals(state, _shown)) return;
        _shown = state;
        _name.Text = pet.Name; _name.TooltipText = pet.Name;
        _level.Text = "Lv " + pet.Level;
        QueueRedraw();
    }

    private static float Fraction(int value, int max) => max > 0 ? Mathf.Clamp(value / (float)max, 0, 1) : 0;

    private void Bar(LayoutNode source, Rect2 rect, float value, bool fillOnly = false)
    {
        foreach (var art in source.Images.OrderBy(a => a.Tag))
        {
            if (fillOnly && art.Tag == 0) continue;
            var dest = rect; var src = new Rect2(art.SrcX, art.SrcY, art.SrcW, art.SrcH);
            if (art.Tag == 1) { dest.Size *= new Vector2(value, 1); src.Size *= new Vector2(value, 1); }
            if (dest.Size.X > 0) DrawTextureRectRegion(Plugin.Kit.Texture(art.Texture!), dest, src);
        }
    }

    public override void _Draw()
    {
        if (Pet is not { } pet) return;
        Bar(_party.Find("progress_hp_0")!, ClassicPetPlateLayout.Hp, Fraction(pet.Hp, pet.MaxHp));
        Bar(_party.Find("progress_mp_0_curse")!, ClassicPetPlateLayout.Mp, Fraction(pet.Mp, pet.MaxMp));
        Bar(_party.Find("progress_hp_0_slow")!, ClassicPetPlateLayout.Satisfaction, Mathf.Clamp(pet.SatisfactionFraction, 0, 1));
        // The experience strip uses the party bar's track under the status bar's experience fill.
        var track = _party.Find("progress_hp_0")!.Images.First(a => a.Tag == 0);
        DrawTextureRectRegion(Plugin.Kit.Texture(track.Texture!), ClassicPetPlateLayout.Exp, new Rect2(track.SrcX, track.SrcY, track.SrcW, track.SrcH));
        Bar(_state.Find("Progress_ExpP")!, ClassicPetPlateLayout.Exp, Mathf.Clamp(pet.ExpFraction, 0, 1), fillOnly: true);
    }
}
