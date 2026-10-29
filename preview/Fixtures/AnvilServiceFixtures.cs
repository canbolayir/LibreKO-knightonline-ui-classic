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
    public static Control BuildUpgradeUiPreview(WorldRoot world)
    {
        var panel = (HudWindow)Call(world, "Preview.BuildUpgrade")!;
        var anvil = NativeAnvil.Prepare(panel, world)!;
        Native.Call(world, "Upgrade.OnResult", new UpgradeResult(ItemUpgradeSubOpcode.Upgrade, ItemUpgradeKind.Preview, ItemUpgradeResult.Succeeded, new[] { new UpgradeSlotResult(PreviewUpgradedResult, 0) }));
        anvil.RefreshBag();
        panel.AddChild(new PreviewTweenBinder(world, panel) { Name = "preview_tween_binder" });
        return panel;
    }

    /// <summary>Right-click on a carried item in the Classic anvil.</summary>
    public static void ClassicAnvilTake(WorldRoot world, int abs) => NativeAnvil.Of(world)!.Take(abs);

    /// <summary>The Classic anvil's Cancel action.</summary>
    public static void ClassicAnvilCancel(WorldRoot world) => NativeAnvil.Of(world)!.Reset();

    /// <summary>A server item-move acknowledgement, delivered the way <see cref="Net"/> raises it.</summary>
    public static void DeliverItemMoveResult(WorldRoot world, bool ok)
    {
        var reply = new Packet(GameOpcodes.GS_ITEM_MOVE);
        reply.WriteByte((byte)ItemMoveSubOpcode.Move);
        reply.WriteByte((byte)(ok ? 1 : 0));
        reply.ResetOffset();
        PreviewPackets.Receive(reply);
    }

    /// <summary>The shared amount prompt, built after the combination window was prepared.</summary>
    public static void BuildCombineAmountPrompt(WorldRoot world)
    {
        Native.Call(world, "Merchant.BuildAmountPrompt");
        NativeServices.HookCombineAmount(world);
    }

    /// <summary>A server repair result, delivered the way <see cref="Net"/> raises it.</summary>
    public static void DeliverRepairResult(WorldRoot world, bool ok, int money)
    {
        Native.Call(world, "Repair.OnResult", ok, money);
        RaiseNet("ItemRepairResultEvent", ok, money);
    }

    /// <summary>
    /// Raises a <see cref="Net"/> event for the plugin's subscribers. The preview world never ran its
    /// network initialisation, so its own handler is called directly by the caller first, matching
    /// the order in which the client subscribed before the plugin.
    /// </summary>
    private static void RaiseNet(string evt, params object?[] args) => Native.Get<Delegate>(NetOwner(evt), evt)?.DynamicInvoke(args);
}

/// <summary>Keeps the anvil's scan and reveal tweens running while the preview world is outside the tree.</summary>
public partial class PreviewTweenBinder : Node
{
    private readonly WorldRoot? _world;
    private readonly Node? _host;
    private Tween? _bound;

    public PreviewTweenBinder() { }

    public PreviewTweenBinder(WorldRoot world, Node host)
    {
        _world = world;
        _host = host;
        ProcessPriority = int.MinValue;
    }

    public override void _Process(double delta)
    {
        if (_world == null || _host == null || Native.Get<Tween>(_world, "Upgrade.scanTween") is not { } tween || tween == _bound) return;
        tween.BindNode(_host);
        _bound = tween;
    }
}
