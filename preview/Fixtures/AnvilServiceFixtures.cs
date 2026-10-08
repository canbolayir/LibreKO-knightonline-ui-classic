using Godot;
using LibreKO;
using LibreKO.Network;

/// <summary>Magic Anvil and NPC service fixtures (formerly in the client's World.UiPreview partial).</summary>
public static partial class PreviewFixtures
{
    private const int PreviewUpgradedResult = 156210009;

    /// <summary>
    /// The client's staged anvil preview, prepared by the Classic anvil adapter. The client's preview
    /// gate re-asks for the selection completed after its first request, so the fixture answers twice.
    /// The preview world is not in the scene tree, so the anvil's scan tweens are bound to the window.
    /// </summary>
    public static Control BuildUpgradeUiPreview(World world)
    {
        var panel = (HudWindow)Native.Call(world, "BuildUpgradeUiPreview")!;
        var anvil = NativeAnvil.Prepare(panel, world)!;
        Native.Call(world, "OnUpgradeResult", new UpgradeResult(2, 2, 1, new[] { new UpgradeSlotResult(PreviewUpgradedResult, 0) }));
        anvil.RefreshBag();
        panel.AddChild(new PreviewTweenBinder(world, panel) { Name = "preview_tween_binder" });
        return panel;
    }

    /// <summary>Right-click on a carried item in the Classic anvil.</summary>
    public static void ClassicAnvilTake(World world, int abs) => NativeAnvil.Of(world)!.Take(abs);

    /// <summary>The Classic anvil's Cancel action.</summary>
    public static void ClassicAnvilCancel(World world) => NativeAnvil.Of(world)!.Reset();

    /// <summary>A server item-move acknowledgement, delivered the way <see cref="Net"/> raises it.</summary>
    public static void DeliverItemMoveResult(World world, bool ok)
    {
        Native.Call(world, "OnItemMoveResult", ok);
        RaiseNet("ItemMoveResultEvent", ok);
    }

    /// <summary>The shared amount prompt, built after the combination window was prepared.</summary>
    public static void BuildCombineAmountPrompt(World world)
    {
        Native.Call(world, "BuildAmountPrompt");
        NativeServices.HookCombineAmount(world);
    }

    /// <summary>A server repair result, delivered the way <see cref="Net"/> raises it.</summary>
    public static void DeliverRepairResult(World world, bool ok, int money)
    {
        Native.Call(world, "OnRepairResult", ok, money);
        RaiseNet("ItemRepairResultEvent", ok, money);
    }

    /// <summary>
    /// Raises a <see cref="Net"/> event for the plugin's subscribers. The preview world never ran its
    /// network initialisation, so its own handler is called directly by the caller first, matching
    /// the order in which the client subscribed before the plugin.
    /// </summary>
    private static void RaiseNet(string evt, params object?[] args) => Native.Get<Delegate>(Net.I, evt)?.DynamicInvoke(args);
}

/// <summary>Keeps the anvil's scan and reveal tweens running while the preview world is outside the tree.</summary>
public partial class PreviewTweenBinder : Node
{
    private readonly World? _world;
    private readonly Node? _host;
    private Tween? _bound;

    public PreviewTweenBinder() { }

    public PreviewTweenBinder(World world, Node host)
    {
        _world = world;
        _host = host;
        ProcessPriority = int.MinValue;
    }

    public override void _Process(double delta)
    {
        if (_world == null || _host == null || Native.Get<Tween>(_world, "_upgradeScanTween") is not { } tween || tween == _bound) return;
        tween.BindNode(_host);
        _bound = tween;
    }
}
