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
    public delegate void Preparer(HudWindow window, WorldRoot world);

    private static readonly Dictionary<string, List<Preparer>> _preparers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(Func<HudWindow, bool> Match, Preparer Prepare)> _patterns = new();
    private static NativeHooks.Hook? _watching;

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
        if (!Plugin.Active || !GodotObject.IsInstanceValid(body) || Native.WindowOf(body) is not { } window) return;
        if (Native.WorldOf(window) is not { } world) return;
        foreach (var prepare in preparers)
        {
            try { prepare(window, world); }
            catch (Exception e) { GD.PushError($"[plugin:knightonline-ui-classic] {window.Id} preparation: {e}"); }
        }
    }

    private static void WatchTree()
    {
        if (_watching is { Active: true }) return;
        _watching = NativeHooks.WatchTree("tree.windows", node =>
        {
            if (node is not HudWindow window) return;
            Callable.From(() =>
            {
                if (!Plugin.Active || !GodotObject.IsInstanceValid(window) || Native.WorldOf(window) is not { } world) return;
                foreach (var (match, prepare) in _patterns)
                {
                    if (!match(window)) continue;
                    try { prepare(window, world); }
                    catch (Exception e) { GD.PushError($"[plugin:knightonline-ui-classic] {window.Id} preparation: {e}"); }
                }
            }).CallDeferred();
        });
    }

    /// <summary>Forgets every preparer; the hooks themselves are released by <see cref="NativeHooks"/>.</summary>
    internal static void Reset()
    {
        _preparers.Clear();
        _patterns.Clear();
        _watching = null;
    }

    /// <summary>Runs <paramref name="sync"/> every frame while the window is visible, before the skin draws.</summary>
    public static void Sync(HudWindow window, Action sync)
    {
        var node = new NativeSync(window, sync) { Name = "native_sync" };
        window.AddChild(node);
        window.MoveChild(node, 0);
        sync();
    }

    /// <summary>
    /// Calls <paramref name="added"/> for every node that later enters the window's subtree, until the root is freed,
    /// its World leaves the tree or the plugin shuts down.
    /// </summary>
    public static void OnDescendantAdded(Node root, Action<Node> added)
    {
        NativeHooks.Hook? hook = null;
        hook = NativeHooks.WatchTree("tree.descendants", node =>
        {
            if (!GodotObject.IsInstanceValid(root)) { hook?.Release(); return; }
            if (root.IsAncestorOf(node)) added(node);
        }, Native.WorldOf(root));
    }

    /// <summary>Names a node if it exists; returns whether it did.</summary>
    public static bool Name(Node? node, string name)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return false;
        node.Name = name;
        return true;
    }

    public static T? Control<T>(WorldRoot world, string field) where T : Node => Native.Get<T>(world, field);
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
        // The window's own visibility signal fires before its skins' signals, so this catch-up runs first on show.
        KnightOnlineUiClassic.Layout.ShownOnly.Gate(this, window, sync);
    }

    public override void _Process(double delta)
    {
        if (_window == null || !_window.IsVisibleInTree()) return;
        _sync();
    }
}
