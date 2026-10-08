using Godot;

namespace KnightOnlineUiClassic.Layout;

public static class ClassicPetHatchLayout
{
    public static readonly Vector2 Size = new(366, 566);
    public static readonly Rect2 Title = new(22, 14, 288, 24);
    public static readonly Rect2 Description = new(14, 85, 338, 22);
    public static readonly Rect2 NameCaption = new(14, 195, 165, 26);
    public static readonly Rect2 Name = new(196, 195, 156, 26);
    public static readonly Rect2 TransformNote = new(14, 195, 338, 34);
    public static readonly Rect2 InventoryHeading = new(14, 234, 338, 20);
    public static readonly Rect2 Status = new(14, 457, 338, 51);
    public static Rect2 Tab(int tab) => new(14 + tab * 173, 50, 165, 26);
    public static Rect2 Stage(int stage) => new(stage == 2 ? 196 : 14, 137, 45, 45);
    public static Rect2 StageHeading(int stage) => new(stage == 2 ? 196 : 14, 112, 156, 22);
    public static Rect2 Pick(int stage) => new(stage == 2 ? 248 : 66, 137, stage == 2 ? 104 : 110, 54);
    public static Rect2 Inventory(int index) => new(14 + index % 7 * 49, 259 + index / 7 * 49, 45, 45);
    public static Rect2 Action(bool accept) => new(accept ? 14 : 255, 520, 97, 29);
}
