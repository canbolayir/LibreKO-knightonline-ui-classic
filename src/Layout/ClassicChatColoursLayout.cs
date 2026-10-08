using Godot;

namespace KnightOnlineUiClassic.Layout;

// No dedicated colour-editor UIF was identified; use the original service materials with a simple fixed list.
public static class ClassicChatColoursLayout
{
    public static Vector2 Size => new(360, 428);
    public static Rect2 Label(int slot) => new(22, 60 + slot * 28, 232, 24);
    public static Rect2 Pick(int slot) => new(264, 60 + slot * 28, 74, 24);
    public static Rect2 Rows => new(16, 56, 328, 312);
    public static Rect2 Default => new(16, 384, 156, 26);
    public static Rect2 Apply => new(188, 384, 156, 26);
}
