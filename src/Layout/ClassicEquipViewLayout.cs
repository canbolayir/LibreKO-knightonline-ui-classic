using Godot;
namespace KnightOnlineUiClassic.Layout;

// Inventory socket spacing and Report typography, without local inventory actions.
public static class ClassicEquipViewLayout
{
    public static readonly Vector2 Size = new(640, 550);
    public static readonly Rect2 Name = new(16, 52, 608, 24);
    public static readonly Rect2 Summary = new(16, 80, 608, 20);
    public static readonly Rect2 Gear = new(20, 137, 147, 241);
    public static readonly Rect2 Costume = new(190, 137, 147, 143);
    public static readonly Rect2 Stats = new(356, 130, 268, 369);
    public static readonly Rect2 Status = new(16, 80, 608, 20);
}
