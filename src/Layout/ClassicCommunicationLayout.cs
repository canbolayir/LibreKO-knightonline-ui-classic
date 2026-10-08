using Godot;

namespace KnightOnlineUiClassic.Layout;

// Dedicated Messenger and room UIFs are absent from the inspected exports; reuse the simple service composition.
public static class ClassicCommunicationLayout
{
    public static Vector2 Size(string id) => id == "messenger" ? new(420, 460) : new(460, 570);
    public static Rect2 BuddyList => new(16, 92, 388, 246);
    public static Rect2 RoomList => new(16, 92, 428, 162);
    public static Rect2 RoomLog => new(16, 350, 428, 164);
}
