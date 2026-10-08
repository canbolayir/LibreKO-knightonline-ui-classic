using Godot;

namespace KnightOnlineUiClassic.Layout;

public static class ClassicPetLayout
{
    public static readonly Vector2 BaseSize = new(286, 457);
    public static readonly Vector2 Size = new(286, 525);
    public static readonly Rect2 Name = new(132, 94, 137, 23);
    public static readonly Rect2 Level = new(134, 130, 120, 20);
    public static readonly Rect2 Status = new(17, 467, 252, 46);
    public static Rect2 Bar(int row) => new(113, 165 + row * 15, 156, 14);
    public static Rect2 Tab(int page) => new(18 + page * 83, 234, 80, 24);
    public static Rect2 Mode(int mode) => new(18 + mode * 83, 266, 80, 24);
    public static Rect2 Detail(int index) => new(22 + index % 2 * 124, 311 + index / 2 * 24, 120, 22);
    public static Rect2 Item(int index) => new(25 + index * 58, 316, 48, 48);
    public static Rect2 Skill(int index) => new(25 + index % 4 * 58, 309 + index / 4 * 43, 40, 40);
}
