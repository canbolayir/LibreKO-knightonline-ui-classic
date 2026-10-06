using Godot;
namespace KnightOnlineUiClassic.Layout;

/// <summary>Editable shop geometry; catalogue and inventory retain their native trade ownership.</summary>
public static class ClassicVendorLayout
{
    public static readonly Vector2 Size = new(363, 578);
    public static readonly Rect2 Title = new(16, 10, 292, 26);
    public static readonly Rect2 DragHandle = new(2, 0, 328, 94);
    public static Rect2 Close => new(336, ClassicDesign.Karus ? 18 : 17, 20, 20);
    public static readonly Rect2 Banner = new(86, 47, 191, 41), Empty = new(24, 165, 276, 38);
    public static readonly Rect2 PageLabel = new(314, 183, 40, 24);
    public static readonly Rect2 WalletCaption = new(202, 315, 34, 22), Coin = new(206, 314, 22, 24), WalletValue = new(240, 315, 106, 22);
    public static readonly Rect2 Capacity = new(17, 315, 180, 22), Divider = new(2, 339, 359, 1);
    public static readonly Rect2 InventoryHeading = new(17, 340, 175, 20), Status = new(17, 552, 329, 18);
    public const int Columns = 6, CellSize = 45, CellPitch = 49, BagColumns = 7, BagSize = 44, BagPitch = 47;
    public static Rect2 CatalogueCell(int index) => new(17 + index % Columns * CellPitch, 101 + index / Columns * CellPitch, CellSize, CellSize);
    public static Rect2 BagCell(int index) => new(17 + index % BagColumns * BagPitch, 362 + index / BagColumns * BagPitch, BagSize, BagSize);
    public static Rect2 PageArrow(int index) => new(314, index == 0 ? 142 : 231, 40, 18);
}
