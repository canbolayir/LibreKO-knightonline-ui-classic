using Godot;
using LibreKO;

/// <summary>
/// The world the previews build windows in, without a zone, terrain or server. The client keeps no preview code:
/// <see cref="Prepare"/> runs the World scene's own construction steps (its systems, then its features, the escape
/// order and the HUD theme) in construct-only mode on a <see cref="WorldRoot"/> that never loads a zone, and the builders below show a
/// client control in a known state. Everything is reached through <see cref="Native"/>, by the client's names.
/// </summary>
public sealed partial class WorldPreview
{
    private readonly WorldRoot _world;
    private bool _prepared;

    public WorldPreview(WorldRoot world) => _world = world;

    /// <summary>Builds the World's systems and features once, as the World scene does after its zone loads.</summary>
    public void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
        // The nodes World.tscn gives the scene: the entity layer and the camera the systems attach to.
        var entities = _world.GetNodeOrNull<Node3D>("Entities") ?? Child(new Node3D { Name = "Entities" });
        var camera = _world.GetNodeOrNull<Camera3D>("Camera") ?? Child(new Camera3D { Name = "Camera" });
        // Construct only: each fixture builds the window it shows, so no feature needs the zone, the viewport or the server.
        Native.Set(Native.Get<object>(_world, "Features")!, "ConstructOnly", true);
        Native.Call(_world, "BuildCoreSystems");
        Native.Call(_world, "EntitySystem.AttachLayer", entities);
        Native.Call(_world, "CameraSystem.Attach", camera);
        Native.Call(_world, "BuildFacets");
        // The item tooltip is shared by every window that shows items; it needs only the World.
        Native.Call(Feature("ItemTooltip"), "Build");
        Native.TryCall(_world, "BuildEscapeOrder", out _);
        Native.TryCall(_world, "ApplyHudTheme", out _);
    }

    private T Child<T>(T node) where T : Node
    {
        _world.AddChild(node);
        return node;
    }

    private object Feature(string property) => Native.Get<object>(_world, property)
        ?? throw new InvalidOperationException("The preview world has no " + property);
}
