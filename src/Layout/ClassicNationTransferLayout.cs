using Godot;
namespace KnightOnlineUiClassic.Layout;

// Account candidates, unchanged native preview and appearance controls share a simple service frame.
public static class ClassicNationTransferLayout
{
    public static readonly Vector2 Size = new(760, 520);
    public static readonly Rect2 Header = new(16, 50, 728, 40);
    public static readonly Rect2 CharactersHeading = new(16, 98, 180, 22);
    public static readonly Rect2 Characters = new(16, 124, 180, 322);
    public static readonly Rect2 Preview = new(210, 124, 220, 300);
    public static readonly Rect2 Editor = new(454, 124, 290, 268);
    public static readonly Rect2 Status = new(454, 402, 290, 60);
    public static readonly Rect2 Accept = new(454, 475, 138, 29);
    public static readonly Rect2 Cancel = new(606, 475, 138, 29);
    public static readonly Rect2 TurnLeft = new(288, 436, 19, 19);
    public static readonly Rect2 TurnRight = new(333, 436, 19, 19);
}
