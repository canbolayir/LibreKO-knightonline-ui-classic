using System.Reflection;
using LibreKO.Plugins;

/// <summary>
/// Routes the plugin's native game services to the preview's per-interface fixture proxies, so the
/// audits keep configuring the same <see cref="PreviewData"/> instances they always have.
/// </summary>
public class PreviewNative : DispatchProxy
{
    private static readonly Dictionary<string, Type> Owners = new()
    {
        ["get_CharacterPanel"] = typeof(IGameWindows), ["get_NpcPortrait"] = typeof(IGameWindows), ["NotificationCount"] = typeof(IGameWindows),
        ["ReadHistory"] = typeof(IGameChat), ["LinkInventoryItem"] = typeof(IGameChat), ["ShowLinkTooltip"] = typeof(IGameChat),
        ["HideLinkTooltip"] = typeof(IGameChat), ["PlayerMenu"] = typeof(IGameChat), ["NearbyPlayers"] = typeof(IGameChat),
        ["get_SelectedAbs"] = typeof(IGameHotbar), ["Select"] = typeof(IGameHotbar),
        ["get_Running"] = typeof(IGameCommands), ["get_Sitting"] = typeof(IGameCommands), ["get_AutoAttacking"] = typeof(IGameCommands),
        ["ToggleRun"] = typeof(IGameCommands), ["ToggleAttack"] = typeof(IGameCommands), ["TurnCamera"] = typeof(IGameCommands), ["OpenGameMenu"] = typeof(IGameCommands),
        ["get_MiniMapVisible"] = typeof(IGameMap),
        ["MoveAmount"] = typeof(IGameInventory), ["TransferToInventorySlot"] = typeof(IGameInventory), ["ConfirmDrop"] = typeof(IGameInventory),
    };

    private Dictionary<Type, PreviewData> _sources = new();
    private readonly Dictionary<string, Delegate?> _events = new();

    public static INativeGame Create(Dictionary<Type, PreviewData> sources)
    {
        var proxy = Create<INativeGame, PreviewNative>();
        ((PreviewNative)(object)proxy)._sources = sources;
        return proxy;
    }

    public static PreviewNative Of(INativeGame game) => (PreviewNative)(object)game;

    public void Raise(string evt, params object?[] args) => _events.GetValueOrDefault(evt)?.DynamicInvoke(args);

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        string name = method!.Name;
        if (name.StartsWith("add_")) { _events[name[4..]] = Delegate.Combine(_events.GetValueOrDefault(name[4..]), (Delegate)args![0]!); return null; }
        if (name.StartsWith("remove_")) { _events[name[7..]] = Delegate.Remove(_events.GetValueOrDefault(name[7..]), (Delegate)args![0]!); return null; }
        if (Owners.TryGetValue(name, out var owner) && _sources.TryGetValue(owner, out var source)) return source.Answer(method, args);
        var type = method.ReturnType;
        return type == typeof(void) ? null : type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
