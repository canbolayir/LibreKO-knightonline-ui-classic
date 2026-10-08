using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterHud(PluginContext context)
    {
        NativeLayout.WatchContentAnchors();
        context.Game.BecameAvailable += () => NativeHudChat.Attach(context.Game);
    }

    static partial void RegisterChat(PluginContext context)
    {
        NativeWindows.PrepareMatching(NativeWhispers.IsWhisper, NativeWhispers.Prepare);
        NativeWindows.Prepare("chat_colors", NativeChatColours.Prepare);
    }
}

/// <summary>
/// The HUD side of the client the Classic HUD and chat need: hidden top icons, the service windows the
/// taskbar and shortcuts open, notification counts, commands, hotbar selection and minimap state.
/// </summary>
public static class NativeHudChat
{
    private static WeakReference<World>? _world;

    /// <summary>The running client world, as attached to the plugin game API.</summary>
    public static World? World
    {
        get
        {
            if (_world != null && _world.TryGetTarget(out var world) && GodotObject.IsInstanceValid(world)) return world;
            return null;
        }
    }

    /// <summary>Runs whenever the client attaches a world to the plugin game API.</summary>
    internal static void Attach(PluginGame game)
    {
        if (Native.Get<object>(game, "_windows") is not { } proxy || Native.Get<IGameWindows>(proxy, "Source") is not { } source) return;
        if (source is NativeServiceWindows wrapped) source = wrapped.Inner;
        if (!Native.Has(source, "_w") || Native.Get<World>(source, "_w") is not { } world) return;
        _world = new WeakReference<World>(world);
        Native.Set(proxy, "Source", new NativeServiceWindows(source, world));
        HideIcons(world);
        NativeGoldLog.Attach(world);
        NativeChat.Attach(world);
        NativeWhispers.Attach(world);
    }

    private static void HideIcons(World world)
    {
        var icons = new (NativeHudPart Part, string Button, string? Blink)[]
        {
            (NativeHudPart.MailIcon, "_mailIconButton", null),
            (NativeHudPart.AchievementsIcon, "_trophy", "_trophyBlink"),
            (NativeHudPart.AttendanceIcon, "_attendanceGift", "_attendanceGiftBlink"),
            (NativeHudPart.PowerUpStoreIcon, "_powerUpStoreIcon", null),
        };
        bool changed = false;
        foreach (var (part, field, blink) in icons)
        {
            if (!NativeHud.IsHidden(part) || Native.Get<Control>(world, field) is not { } button) continue;
            button.Visible = false;
            button.VisibilityChanged += () => { if (button.Visible) button.Visible = false; };
            if (blink != null && Native.Get<Tween>(world, blink) is { } tween && tween.IsValid()) tween.Kill();
            button.Modulate = Colors.White;
            changed = true;
        }
        if (changed) Native.TryCall(world, "QueueDockLayout", out _);
    }

    public static int NotificationCount(string id)
    {
        if (World is not { } world) return 0;
        return id.ToLowerInvariant() switch
        {
            "mail" => Net.I?.MailUnread ?? 0,
            "achievements" => Native.Get<System.Collections.IDictionary>(world, "_achClaimablePerTab") is { } tabs
                ? tabs.Values.Cast<int>().Sum() : 0,
            "attendance" => Native.Call(world, "ClaimableAttendanceCount") is int count ? count : 0,
            _ => 0,
        };
    }

    public static int SelectedAbs
    {
        get
        {
            if (World is not { } world) return -1;
            int selected = Native.Get<int>(world, "_hotSelected");
            if (selected < 0 || Native.Get<int[]>(world, "_hotbar") is not { } slots) return -1;
            return selected < slots.Length && slots[selected] != 0 ? selected : -1;
        }
    }

    public static void Select(int abs)
    {
        if (World is { } world) Native.Call(world, "SelectHotAbs", abs);
    }

    public static bool Running => World is not { } world || Native.Get<bool>(world, "_running");
    public static bool Sitting => World is { } world && Native.Get<bool>(world, "_selfSitting");
    public static bool AutoAttacking => World is { } world && Native.Get<bool>(world, "_autoAttack");
    public static void ToggleRun() => Command("ToggleRunMode");
    public static void ToggleAttack() => Command("ToggleAutoAttack");
    public static void TurnCamera() => Command("StartCameraHalfTurn");
    public static void OpenGameMenu() { if (World is { } world) Native.Call(world, "ToggleEsc", (bool?)true); }

    private static void Command(string method) { if (World is { } world) Native.Call(world, method); }

    /// <summary>
    /// Whether the minimap is shown. The Classic HUD hides the native minimap layer, so the native
    /// control's own visibility only records the player's minimap toggle.
    /// </summary>
    public static bool MiniMapVisible => World is not { } world || Native.Get<Control>(world, "_miniMap") is not { } map || map.Visible;
}

/// <summary>
/// Opens and closes the service windows the client does not register as plugin windows (mail,
/// achievements, attendance, Power-Up Store, zone map and genie) through their own toggles, and passes
/// every other id to the client.
/// </summary>
public sealed class NativeServiceWindows : IGameWindows
{
    private readonly World _world;

    public NativeServiceWindows(IGameWindows inner, World world)
    {
        Inner = inner;
        _world = world;
    }

    public IGameWindows Inner { get; }

    private static readonly Dictionary<string, (string Shown, string Toggle, string? Close)> Services = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mail"] = ("_mailShown", "ToggleMail", null),
        ["achievements"] = ("_achShown", "ToggleAchievements", null),
        ["attendance"] = ("_attendanceShown", "ToggleAttendance", null),
        ["shoppingmall"] = ("_pusShown", "ToggleShoppingMall", null),
        ["zonemap"] = ("_fullMapShown", "ToggleFullMap", null),
        ["genie"] = ("_genieShown", "ToggleGenie", "CloseGenie"),
    };

    private bool Service(string id, out (string Shown, string Toggle, string? Close) service) =>
        Services.TryGetValue(id, out service) && GodotObject.IsInstanceValid(_world) && Native.Has(_world, service.Shown);

    public bool IsOpen(string id) => Service(id, out var service) ? Native.Get<bool>(_world, service.Shown) : Inner.IsOpen(id);

    public void Open(string id)
    {
        if (!Service(id, out var service)) { Inner.Open(id); return; }
        if (!Native.Get<bool>(_world, service.Shown)) Native.Call(_world, service.Toggle);
    }

    public void Close(string id)
    {
        if (!Service(id, out var service)) { Inner.Close(id); return; }
        if (service.Close != null) Native.Call(_world, service.Close);
        else if (Native.Get<bool>(_world, service.Shown)) Native.Call(_world, service.Toggle);
    }

    public void Toggle(string id)
    {
        if (!Service(id, out var service)) { Inner.Toggle(id); return; }
        Native.Call(_world, service.Toggle);
    }
}
