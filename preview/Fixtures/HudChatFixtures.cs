using Godot;
using LibreKO;
using KnightOnlineUiClassic.Native;

/// <summary>
/// Chat fixtures the fork kept in the client (World.UiPreview.ChatColours.cs). The chat colour editor is
/// built by the client and prepared by the plugin, as it is in the game.
/// </summary>
public static partial class PreviewFixtures
{
    public static CanvasLayer BuildChatColoursClassicUiPreview(World world)
    {
        var type = Native.ClientType("LibreKO.ChatSystem")!;
        var chat = Activator.CreateInstance(type, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object?[] { world }, null)!;
        Native.Set(world, "Chat", chat);
        Native.Call(chat, "Build");
        Native.Call(chat, "DetachNetwork");
        Native.Call(world, "BuildChatColors");
        var layer = Native.Get<CanvasLayer>(world, "_chatColorsLayer")!;
        var window = Native.Get<HudWindow>(world, "_chatColorsWindow")!;
        NativeChatColours.Prepare(window, world);
        Native.Call(world, "OpenChatColors");
        var panel = Native.Get<Control>(chat, "Panel")!;
        panel.Reparent(layer, false);
        panel.Visible = false;
        world.RemoveChild(layer);
        return layer;
    }
}
