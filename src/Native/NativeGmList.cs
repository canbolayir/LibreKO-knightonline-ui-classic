using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The names of the game masters online, for the GM List notice bar. The client receives no such list today, so the
/// list is empty and the bar says so. A core change would send it the way the 2.x clients receive it: the server
/// keeps the online GM names and sends <c>GS_NOTICE</c> with a new style (5: <c>u8 count</c>, then <c>count</c>
/// names as <c>u16</c>-length strings) at game start and whenever a GM logs in or out; the client's
/// <c>Net.HandleNotice</c> keeps it as <c>Net.GmList</c> and raises <c>Net.GmListEvent</c>
/// (<c>Action&lt;IReadOnlyList&lt;string&gt;&gt;</c>). When that event exists, <see cref="Attach"/> listens to it;
/// anything else can call <see cref="Set"/>.
/// </summary>
public static class NativeGmList
{
    public const string NetEvent = "GmListEvent", NetList = "GmList";
    private static IReadOnlyList<string> _names = Array.Empty<string>();

    /// <summary>The online GM names, in the server's order.</summary>
    public static IReadOnlyList<string> Names => _names;

    /// <summary>Raised whenever the list changes.</summary>
    public static event Action? Changed;

    public static void Set(IEnumerable<string> names)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToArray();
        if (list.SequenceEqual(_names)) return;
        _names = list;
        Changed?.Invoke();
    }

    public static void Clear() => Set(Array.Empty<string>());

    /// <summary>Follows the client's GM list once the client has one; a client without it keeps the empty list quietly.</summary>
    public static void Attach(Godot.Node lifetime)
    {
        if (Game.Connection is not { } net || !Native.Has(net, NetEvent)) return;
        Native.Subscribe(net, NetEvent, new Action<IReadOnlyList<string>>(names => Set(names)), lifetime);
        if (Native.Has(net, NetList) && Native.Get<IReadOnlyList<string>>(net, NetList) is { } current) Set(current);
    }
}
