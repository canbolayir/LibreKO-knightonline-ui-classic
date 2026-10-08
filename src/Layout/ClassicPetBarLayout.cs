using Godot;

namespace KnightOnlineUiClassic.Layout;

public static class ClassicPetBarLayout
{
    public static readonly Vector2 Size = new(384, 40);
    public static readonly Rect2 Grip = new(0, 2, 28, 36);
    public static readonly Rect2 Attack = new(30, 4, 32, 32);
    public static readonly Rect2 Page = new(358, 4, 24, 32);
    public static Rect2 Skill(int index) => new(70 + index * 36, 4, 32, 32);
}
