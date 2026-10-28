using Godot;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The status bar's plate-less icon row: game-icons.net glyphs (CC BY 3.0, see THIRD_PARTY_NOTICES.md) recoloured per
/// nation by <c>tools/make_hud_icons.py</c> into <c>assets/textures/hud-icons/&lt;nation&gt;-&lt;state&gt;.png</c>, one
/// 28-pixel cell per icon in <see cref="Order"/>.
/// </summary>
public static class ClassicHudIcons
{
    public const int Cell = 28;

    /// <summary>Cell order of the strips; the generator's ICONS list must match.</summary>
    public static readonly string[] Order =
        { "quests", "store", "achievements", "attendance", "genie", "events", "daily-rank", "time", "mail", "discord" };

    public const string Normal = "normal", Hover = "hover", Pressed = "pressed", Disabled = "disabled", Glow = "glow";

    /// <summary>Seconds of one attention pulse (the glow rises and falls once).</summary>
    public const double PulsePeriod = 1.2;

    private static string Nation => Plugin.Kit.Nation == 1 ? "karus" : "human";

    /// <summary>The state strip for a button state (Disabled, Pressed, Hover or Normal).</summary>
    public static string StateOf(BaseButton button, bool hovered) =>
        button.Disabled ? Disabled : button.IsPressed() || button.ButtonPressed ? Pressed : hovered ? Hover : Normal;

    /// <summary>0..1 brightness of the attention pulse after <paramref name="seconds"/>.</summary>
    public static float Pulse(double seconds) => (float)(0.5 - 0.5 * Math.Cos(seconds * Math.Tau / PulsePeriod));

    /// <summary>Draws one icon cell at <paramref name="at"/>; false when the icon or its strip is missing.</summary>
    public static bool Draw(CanvasItem canvas, string icon, string state, Vector2 at, Color? modulate = null)
    {
        int index = Array.IndexOf(Order, icon);
        if (index < 0 || Plugin.Kit.Texture($"hud-icons/{Nation}-{state}.png") is not { } strip) return false;
        canvas.DrawTextureRectRegion(strip, new Rect2(at, new Vector2(Cell, Cell)),
            new Rect2(index * Cell, 0, Cell, Cell), modulate ?? Colors.White);
        return true;
    }

    /// <summary>
    /// Draws a row button: the state's glyph, pressed one pixel lower; while <paramref name="pulse"/> is set, a halo
    /// behind the normal glyph and the brighter hover glyph fading in and out with it.
    /// </summary>
    public static void DrawButton(CanvasItem canvas, string icon, string state, float? pulse = null)
    {
        var at = state == Pressed ? new Vector2(0, 1) : Vector2.Zero;
        if (pulse is { } p && state == Normal)
        {
            Draw(canvas, icon, Glow, at, new Color(1, 1, 1, 0.25f + 0.75f * p));
            Draw(canvas, icon, Normal, at);
            Draw(canvas, icon, Hover, at, new Color(1, 1, 1, p));
            return;
        }
        if (pulse != null && state == Hover) Draw(canvas, icon, Glow, at);
        Draw(canvas, icon, state, at);
    }
}
