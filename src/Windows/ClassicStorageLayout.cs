using Godot;

namespace KnightOnlineUiClassic.Windows;

internal static class ClassicStorageLayout
{
    public static readonly Vector2 Size = new(366, 553);
    public static readonly Rect2 Title = new(14, 16, 338, 24);
    public static readonly Rect2 Status = new(14, 253, 336, 18);
    public static readonly Rect2 StoredCaption = new(14, 280, 178, 20);
    public static Rect2 PageUp(bool vip) => new(318, vip ? 61 : 98, 40, 17);
    public static Rect2 PageDown(bool vip) => new(318, vip ? 120 : 181, 40, 17);
    public static Rect2 Page(bool vip) => new(318, vip ? 83 : 132, 40, 32);
    public static readonly Rect2 StoredGold = new(231, 280, 119, 20);
    public static readonly Rect2 Weight = new(14, 319, 181, 20);
    public static readonly Rect2 Gold = new(231, 319, 119, 20);
    public static Rect2 Stored(int index) => new(13 + index % 6 * 49, 52 + index / 6 * 49, 45, 45);
    public static Rect2 Bag(int index) => new(13 + index % 7 * 49, 349 + index / 7 * 49, 45, 45);
}
