using Godot;

namespace KnightOnlineUiClassic.Layout;

public static class ClassicCapeLayout
{
    public static readonly Vector2 Size = new(593, 468);
    public static readonly Rect2 Preview = new(22, 22, 165, 217);
    public static readonly Rect2 Extension = new(381, 0, 212, 468);
    public static readonly Rect2 Price = new(185, 295, 169, 33);
    public static readonly Rect2 Chosen = new(184, 329, 169, 32);
    public static readonly Rect2 Requirement = new(183, 361, 169, 32);
    public static readonly Rect2 Ticket = new(395, 229, 184, 48);
    public static readonly Rect2 Hint = new(395, 293, 184, 76);
    public static readonly Rect2 Status = new(395, 380, 184, 76);
    public static Rect2 Pattern(int slot) => new(205 + slot % 2 * 80, 22 + slot / 2 * 116, 67, 103);
    public static Rect2 Colour(int slot) => new(20 + slot % 3 * 53, 295 + slot / 3 * 53, 45, 45);
    public static Rect2 Slider(int row) => new(418, 107 + row * 36, 117, 24);
    public static Rect2 Value(int row) => new(541, 107 + row * 36, 38, 24);
}
