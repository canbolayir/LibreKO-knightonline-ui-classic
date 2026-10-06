using Godot;
namespace KnightOnlineUiClassic.Layout;

/// <summary>Editable trade summary with separate item, quantity and total bands.</summary>
public static class ClassicTradeApprovalLayout
{
    public const int SummaryHeight = 66;
    public static Vector2 Size => ClassicQuantityLayout.Size + new Vector2(0, SummaryHeight);
    private static Rect2 Row(int x, int y, int width, int height) => new(ClassicQuantityLayout.Origin + new Vector2(x, y), new Vector2(width, height));
    public static Rect2 Title => ClassicQuantityLayout.Message;
    public static Rect2 Icon => Row(14, 36, 42, 42);
    public static Rect2 ItemName => Row(64, 35, 230, 34);
    public static Rect2 QuantityCaption => Row(64, 72, 120, 18);
    public static Rect2 QuantityValue => Row(214, 72, 80, 18);
    public static Rect2 Separator => Row(14, 97, 280, 1);
    public static Rect2 TotalCaption => Row(14, 104, 280, 18);
    public static Rect2 Coin => Row(14, 124, 22, 24);
    public static Rect2 TotalValue => Row(44, 123, 250, 26);
    public static Rect2 Confirm => new(ClassicQuantityLayout.Confirm.Position + new Vector2(0, SummaryHeight), ClassicQuantityLayout.Confirm.Size);
    public static Rect2 Cancel => new(ClassicQuantityLayout.Cancel.Position + new Vector2(0, SummaryHeight), ClassicQuantityLayout.Cancel.Size);
}
