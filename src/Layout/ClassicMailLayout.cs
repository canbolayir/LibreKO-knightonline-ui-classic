using Godot;

namespace KnightOnlineUiClassic.Layout;

// No compatible mail UIF is present in the exported reference set. Reuse the simple service composition.
public static class ClassicMailLayout
{
    public static Vector2 Size(string id) => new(420, id == "mail" ? 400 : id == "mailread" ? 510 : 562);
    public static Rect2 Inbox => new(16, 112, 388, 236);
    public static Rect2 Message => new(16, 124, 388, 146);
    public static Rect2 ReadAttachments => new(16, 318, 388, 130);
    public static Rect2 DraftMessage => new(16, 148, 388, 116);
    public static Rect2 DraftAttachments => new(16, 338, 388, 142);
}
