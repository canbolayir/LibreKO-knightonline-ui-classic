using Godot;

namespace KnightOnlineUiClassic.Layout;

// Live service controls use one-pixel coordinates and the inventory's 49-pixel pitch.
public static class ClassicServiceLayout
{
    public static Vector2 Size(string id) => id switch
    {
        "warp" => new(320, 482),
        "repair" => new(366, 488),
        "seal" => new(366, 538),
        "piecechange" => new(366, 538),
        "itemcombine" => new(366, 414),
        "combinerecipes" => new(790, 490),
        "class_change" => new(327, 240),
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
    public static Rect2 Cell(int index, int columns, int x, int y) => new(x + index % columns * 49, y + index / columns * 49, 45, 45);
}
