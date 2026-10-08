using Godot;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Prepares native windows before the Classic skins run. A preparer names the native controls a skin
/// looks for, mirrors native state into the metas the skin reads and builds the native additions the
/// skin expects. Preparers are registered as window extenders ahead of the skins, and both defer to the
/// end of the frame in which the window was built, so the preparer always runs first.
/// </summary>
public static class NativeWindows
{
    public delegate void Preparer(HudWindow window, World world);

    private static readonly Dictionary<string, List<Preparer>> _preparers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(Func<HudWindow, bool> Match, Preparer Prepare)> _patterns = new();
    private static bool _watching;

    public static void Prepare(string id, Preparer prepare)
    {
        if (!_preparers.TryGetValue(id, out var list)) _preparers[id] = list = new();
        list.Add(prepare);
    }

    /// <summary>Prepares windows whose id is not known in advance, such as per-player whisper windows.</summary>
    public static void PrepareMatching(Func<HudWindow, bool> match, Preparer prepare) => _patterns.Add((match, prepare));

    /// <summary>Registers every preparer. Call before the skins register their own extenders.</summary>
    public static void Register(PluginUi ui)
    {
        foreach (var (id, preparers) in _preparers)
        {
            var captured = preparers;
            ui.ExtendWindow(id, body => Callable.From(() => Run(body, captured)).CallDeferred());
        }
        if (_patterns.Count > 0) WatchTree();
    }

    private static void Run(Control body, List<Preparer> preparers)
    {
        if (!GodotObject.IsInstanceValid(body) || Native.WindowOf(body) is not { } window) return;
        if (Native.WorldOf(window) is not { } world) return;
        foreach (var prepare in preparers)
        {
            try { prepare(window, world); }
            catch (Exception e) { GD.PushError($"[plugin:knightonline-ui-classic] {window.Id} preparation: {e}"); }
        }
    }

    private static void WatchTree()
    {
        if (_watching || Engine.GetMainLoop() is not SceneTree tree) return;
        _watching = true;
        tree.NodeAdded += node =>
        {
            if (node is not HudWindow window) return;
            Callable.From(() =>
            {
                if (!GodotObject.IsInstanceValid(window) || Native.WorldOf(window) is not { } world) return;
                foreach (var (match, prepare) in _patterns)
                {
                    if (!match(window)) continue;
                    try { prepare(window, world); }
                    catch (Exception e) { GD.PushError($"[plugin:knightonline-ui-classic] {window.Id} preparation: {e}"); }
                }
            }).CallDeferred();
        };
    }

    /// <summary>Runs <paramref name="sync"/> every frame while the window is visible, before the skin draws.</summary>
    public static void Sync(HudWindow window, Action sync)
    {
        var node = new NativeSync(window, sync) { Name = "native_sync" };
        window.AddChild(node);
        window.MoveChild(node, 0);
        sync();
    }

    /// <summary>Calls <paramref name="added"/> for every node that later enters the window's subtree.</summary>
    public static void OnDescendantAdded(Node root, Action<Node> added)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        SceneTree.NodeAddedEventHandler? handler = null;
        handler = node =>
        {
            if (!GodotObject.IsInstanceValid(root)) { tree.NodeAdded -= handler; return; }
            if (root.IsAncestorOf(node)) added(node);
        };
        tree.NodeAdded += handler;
    }

    /// <summary>Names a node if it exists; returns whether it did.</summary>
    public static bool Name(Node? node, string name)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return false;
        node.Name = name;
        return true;
    }

    public static T? Control<T>(World world, string field) where T : Node => Native.Get<T>(world, field);
}

public partial class NativeSync : Node
{
    private readonly HudWindow _window;
    private readonly Action _sync;

    public NativeSync() { _window = null!; _sync = () => { }; }

    public NativeSync(HudWindow window, Action sync)
    {
        _window = window;
        _sync = sync;
        ProcessPriority = int.MinValue;
    }

    public override void _Process(double delta)
    {
        if (_window == null || !_window.IsVisibleInTree()) return;
        _sync();
    }
}
