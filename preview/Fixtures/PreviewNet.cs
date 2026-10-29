using Godot;

/// <summary>
/// The preview boots the client's network node once (named "preview_net") so Game.Session and Game.Requests exist;
/// audits release only the connections they created themselves.
/// </summary>
public static class PreviewNet
{
    public const string BootName = "preview_net";

    public static void Release(Node? net)
    {
        if (net == null || !GodotObject.IsInstanceValid(net) || net.Name == BootName) return;
        net.Free();
    }
}