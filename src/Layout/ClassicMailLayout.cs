using Godot;

namespace KnightOnlineUiClassic.Layout;

// No compatible mail UIF is present in the exported reference set. Reuse the simple service composition.
public static class ClassicMailLayout
{
    // Every row keeps the plain frame's even 16 px inset, the lowest one 16 px above the frame bottom.
    public static Vector2 Size(string id) => new(420, id == "mail" ? 392 : id == "mailread" ? 517 : 564);
    public const float ReadActions = 475, ComposeActions = 522;
    public static Rect2 Action(int index, float y) => new(16 + index * 202, y, 186, 26);
    public static Rect2 InboxStatus => new(16, 352, 388, 24);
    // Eight whole 26 px inbox rows; the bulk row (Select all, Delete) sits under them, above the status line.
    public static Rect2 Inbox => new(16, 112, 388, 208);
    public static Rect2 SelectAll => new(16, 324, 120, 24);
    public static Rect2 DeleteTicked => new(304, 324, 100, 24);
    public static Rect2 BulkConfirm => new(140, 324, 264, 24);
    public static Rect2 Write => new(16, 52, 80, 26);
    public static Rect2 Refresh => new(102, 52, 70, 26);
    public static Rect2 UnreadOnly => new(178, 52, 84, 26);
    public static Rect2 ParcelsOnly => new(264, 52, 82, 26);
    public static Rect2 UnreadCount => new(348, 52, 56, 26);
    public static Rect2 Message => new(16, 124, 388, 146);
    // Four whole 35 px attachment rows.
    public static Rect2 ReadAttachments => new(16, 318, 388, 137);
    public static Rect2 DraftMessage => new(16, 148, 388, 116);
    public static Rect2 DraftAttachments => new(16, 338, 388, 142);
}
