using Godot;
namespace KnightOnlineUiClassic.Layout;

// Native character size and original appearance arrows in a simple service frame.
public static class ClassicGenderLayout
{
    public static readonly Vector2 Size = new(560, 450);
    public static readonly Rect2 Preview = new(16, 62, 220, 300);
    public static readonly Rect2 Editor = new(254, 62, 290, 268);
    public static readonly Rect2 Status = new(254, 340, 290, 56);
    public static readonly Rect2 Accept = new(254, 405, 138, 29);
    public static readonly Rect2 Cancel = new(406, 405, 138, 29);
    public static readonly Rect2 TurnLeft = new(94, 374, 19, 19);
    public static readonly Rect2 TurnRight = new(139, 374, 19, 19);
}
