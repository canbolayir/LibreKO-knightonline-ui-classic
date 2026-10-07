using Godot;
namespace KnightOnlineUiClassic.Layout;

// Editable composition measured against co_itemupgrade/co_ringupgrade references.
public static class ClassicAnvilLayout
{
    public static readonly Vector2 Size = new(368,553);
    public static readonly Rect2 Title = new(76,43,215,24), Drag = new(1,39,328,40);
    public static readonly Rect2 Item = new(44,133,45,45), Result = new(277,133,45,45);
    public static readonly Rect2 Status = new(26,234,310,23), Wallet = new(236,319,112,20), Weight = new(14,319,181,20);
    public static readonly Rect2 Ok = new(41,265,115,15), Cancel = new(209,265,115,15), Back = new(126,289,116,15);
    public static readonly Rect2 Scan = new(20,81,325,150);
    public static Rect2 Material(int i) => new(112+i%3*48,84+i/3*49,45,45);
    public static Rect2 Bag(int i) => new(13+i%7*49,348+i/7*49,45,45);
    public static Rect2 Accessory(int i) => i switch { 0=>new(164,99,45,45),1=>new(125,161,45,45),_=>new(198,161,45,45) };
    public static Rect2 CompoundMaterial(int i) => new(37,102+i*59,45,45);
}
