using Godot;
namespace KnightOnlineUiClassic.Layout;

public static class ClassicMarketPriceLayout
{
    public static readonly Vector2 Size = new(840, 566);
    public static readonly Rect2 Results = new(16, 240, 316, 194);
    public static readonly Rect2 Chart = new(344, 78, 480, 372);
    public static readonly Rect2 Selected = new(16, 450, 46, 46);
    public static readonly Rect2 Status = new(16, 516, 700, 30);
    public static readonly Rect2 Cancel = new(728, 518, 96, 26);
}
