using Godot;
namespace KnightOnlineUiClassic.Layout;

/// <summary>Editable PersonalTradeEdit composition using the original message-frame fragments.</summary>
public static class ClassicQuantityLayout
{
    public static Vector2 Size => ClassicDesign.Karus ? new(312, 129) : new(315, 131);
    public static Vector2 Origin => new(2, ClassicDesign.Karus ? 0 : 1);
    public static Rect2 Message => new(Origin + new Vector2(8, 9), new Vector2(289, 24));
    public static Rect2 Icon => new(Origin + (ClassicDesign.Karus ? new Vector2(63, 34) : new Vector2(58, 36)), new Vector2(42, 42));
    public static Rect2 Amount => new(Origin + (ClassicDesign.Karus ? new Vector2(122, 46) : new Vector2(119, 46)), ClassicDesign.Karus ? new Vector2(114, 22) : new Vector2(116, 23));
    public static Rect2 InputFrame => new(Origin + (ClassicDesign.Karus ? new Vector2(111, 40) : new Vector2(113, 40)), ClassicDesign.Karus ? new Vector2(131, 36) : new Vector2(144, 44));
    public static Rect2 Confirm => new(Origin + (ClassicDesign.Karus ? new Vector2(20, 85) : new Vector2(21, 88)), new Vector2(97, 29));
    public static Rect2 Cancel => new(Origin + (ClassicDesign.Karus ? new Vector2(190, 85) : new Vector2(191, 88)), new Vector2(97, 29));
    public static Rect2 FramePart(int index) => index switch
    {
        0 => new(Origin, new Vector2(64, 64)),
        1 => new(Origin + new Vector2(244, 0), new Vector2(64, 64)),
        2 => new(Origin + new Vector2(0, 64), new Vector2(64, 64)),
        3 => new(Origin + new Vector2(244, 64), new Vector2(64, 64)),
        4 => new(Origin + new Vector2(64, 0), new Vector2(180, 64)),
        _ => new(Origin + new Vector2(64, 64), new Vector2(180, 64)),
    };
    public static Rect2 FramePart(int index, Vector2 size)
    {
        var part = FramePart(index);
        if (index is 2 or 3 or 5) part.Position += new Vector2(0, size.Y - Size.Y);
        return part;
    }
}
