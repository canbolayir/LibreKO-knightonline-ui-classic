using Godot;

namespace KnightOnlineUiClassic.Layout;

/// <summary>Shared count geometry and type for Classic item-slot overlays.</summary>
public static class ItemCountStyle
{
    public const int FontSize = 11;
    public const int Inset = 2;

    public static void Apply(Label label, Control slot, Font? font = null)
    {
        if (label.GetParent() != slot) label.Reparent(slot, false);
        label.Name = "item_count";
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetRight = -Inset;
        label.OffsetBottom = -Inset;
        label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        label.AddThemeFontSizeOverride("font_size", FontSize);
        if (font != null) label.AddThemeFontOverride("font", font);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        label.AddThemeColorOverride("font_shadow_color", Colors.Transparent);
        label.AddThemeConstantOverride("shadow_offset_x", 0);
        label.AddThemeConstantOverride("shadow_offset_y", 0);
    }
}
