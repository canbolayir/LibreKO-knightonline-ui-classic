using Godot;
namespace KnightOnlineUiClassic.Layout;

// Original service artwork surrounds the native five-stat allocation controls.
public static class ClassicRebirthLayout
{
    public static readonly Vector2 Size = new(380, 432);
    public static readonly Rect2 Level = new(16, 52, 348, 22);
    public static readonly Rect2 Points = new(16, 80, 348, 22);
    public static readonly Rect2 Status = new(16, 326, 348, 50);
    public static readonly Rect2 Accept = new(16, 388, 167, 29);
    public static readonly Rect2 Cancel = new(197, 388, 167, 29);
    public static Rect2 Stat(int row) => new(16, 144 + row * 34, 74, 26);
    public static Rect2 Current(int row) => new(104, 144 + row * 34, 66, 26);
    public static Rect2 Remove(int row) => new(206, 148 + row * 34, 19, 19);
    public static Rect2 Picked(int row) => new(230, 144 + row * 34, 39, 26);
    public static Rect2 Add(int row) => new(274, 148 + row * 34, 19, 19);
    public static Rect2 Total(int row) => new(306, 144 + row * 34, 58, 26);
}
