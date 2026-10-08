using Godot;

namespace KnightOnlineUiClassic.Layout;

// The original auction workflow has four pages; all pages share the same content bounds.
public static class ClassicAuctionLayout
{
    public static readonly Vector2 Size = new(880, 610);
    public static readonly Rect2 Body = new(16, 54, 848, 540);
    public static readonly Rect2 Lots = new(0, 0, 848, 160);
    public static readonly Rect2 Selection = new(0, 168, 848, 30);
    public static Rect2 Section(int column) => new(column * 286, 206, 276, 210);
}
