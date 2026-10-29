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
        var game = context.Game;
        Action attached = () => NativeHudChat.Attach(game);
        NativeHooks.Listen("Game.BecameAvailable", () => game.BecameAvailable += attached, () => game.BecameAvailable -= attached);
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
    private static WeakReference<WorldRoot>? _world;

    /// <summary>The running client world, as attached to the plugin game API.</summary>
    public static WorldRoot? World
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
        if (Native.Get<object>(game, "windows") is not { } proxy || Native.Get<IGameWindows>(proxy, "Source") is not { } source) return;
        if (source is NativeServiceWindows wrapped) source = wrapped.Inner;
        if (CurrentWorld() is not { } world) return;
        _world = new WeakReference<WorldRoot>(world);
        var wrapper = new NativeServiceWindows(source, world);
        if (Native.Set(proxy, "Source", wrapper))
            NativeHooks.Hold("Game.Windows.Source", () =>
            {
                if (ReferenceEquals(Native.Get<IGameWindows>(proxy, "Source"), wrapper)) Native.Set(proxy, "Source", source);
            }, world);
        HideIcons(world);
        DockClearOfStatus(world);
        // The Genie window is built after the client attaches the plugin game API.
        var built = new WeakReference<WorldRoot>(world);
        Callable.From(() => { if (Plugin.Active && built.TryGetTarget(out var w) && GodotObject.IsInstanceValid(w)) OpenClearOfMiniMap(w); }).CallDeferred();
        NativeQuestKey.Attach(world, game);
        NativeGoldLog.Attach(world);
        NativeChat.Attach(world);
        NativeWhispers.Attach(world);
    }

    internal static void Reset() => _world = null;

    /// <summary>The world the client is running: the current scene, or a live WorldRoot elsewhere in the tree (previews).</summary>
    private static WorldRoot? CurrentWorld()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return null;
        if (tree.CurrentScene is WorldRoot current && !current.IsQueuedForDeletion()) return current;
        return tree.Root.FindChildren("*", "", true, false).OfType<WorldRoot>().FirstOrDefault(w => !w.IsQueuedForDeletion());
    }

    private static void HideIcons(WorldRoot world, bool retry = true)
    {
        var icons = new (NativeHudPart Part, string Button, string? Blink)[]
        {
            (NativeHudPart.MailIcon, "MailIcon.IconButton", null),
            (NativeHudPart.AchievementsIcon, "AchievementTrophy.Trophy", "AchievementTrophy.badge.Blink"),
            (NativeHudPart.AttendanceIcon, "Attendance.Gift", "Attendance.giftBadge.Blink"),
            (NativeHudPart.PowerUpStoreIcon, "TopIcons.powerUpStoreIcon", null),
            // The status bar's G, E and R open these; E and R flash for the alert and the new-day blink.
            (NativeHudPart.GenieIcon, TopIconPrefix + "Genie", null),
            (NativeHudPart.EventsIcon, "Events.iconButton", null),
            (NativeHudPart.DailyRankIcon, "DailyRankWindow.drIcon", null),
        };
        HideIcons(world, icons, retry);
    }

    private const string TopIconPrefix = "TopIcons.Icon:";

    private static Control? IconOf(WorldRoot world, string field) =>
        !field.StartsWith(TopIconPrefix) ? Native.Get<Control>(world, field)
        : Native.ClientType("LibreKO.Features.Hud.TopIconSlot") is { } slots && Enum.TryParse(slots, field[TopIconPrefix.Length..], out var slot)
            ? Native.Call(world, "TopIcons.Icon", slot) as Control : null;

    private static void HideIcons(WorldRoot world, (NativeHudPart Part, string Button, string? Blink)[] icons, bool retry)
    {
        bool changed = false;
        var later = new List<(NativeHudPart, string, string?)>();
        foreach (var (part, field, blink) in icons)
        {
            if (!NativeHud.IsHidden(part)) continue;
            if (IconOf(world, field) is not { } button)
            {
                // Some icons (daily rank) are created after the client attaches the plugin game API.
                if (Native.Has(world, field)) later.Add((part, field, blink));
                continue;
            }
            button.Visible = false;
            button.VisibilityChanged += () => { if (button.Visible) button.Visible = false; };
            if (blink != null && Native.Get<Tween>(world, blink) is { } tween && tween.IsValid()) tween.Kill();
            button.Modulate = Colors.White;
            changed = true;
        }
        if (later.Count > 0 && retry)
        {
            var weak = new WeakReference<WorldRoot>(world);
            Callable.From(() =>
            {
                if (Plugin.Active && weak.TryGetTarget(out var w) && GodotObject.IsInstanceValid(w)) HideIcons(w, later.ToArray(), false);
            }).CallDeferred();
        }
        if (!changed) return;
        Native.TryCall(world, "MainWindows.QueueDockLayout", out _);
        // Close the gaps the hidden icons leave in the remaining top-right strip.
        Native.TryCall(world, "TopIcons.Place", out _);
    }

    private const float StatusGap = 4f;

    /// <summary>
    /// The client docks the Character window clear of its own HUD, which no longer includes the Classic status bar
    /// that replaces it: the window would sit over the bar and catch the clicks meant for its letter row. The docked
    /// spot moves just below the bar, its letter row and envelope; a window the player moved keeps its place.
    /// </summary>
    private static void DockClearOfStatus(WorldRoot world)
    {
        if (Native.Get<System.Collections.IDictionary>(world, "MainWindows.Windows") is not { } windows || !windows.Contains("Character")
            || windows["Character"] is not HudWindow window || Native.Get<Func<Vector2>>(window, "dock") is not { } dock) return;
        var weak = new WeakReference<WorldRoot>(world);
        window.DockTo(() =>
        {
            var spot = dock();
            if (ClassicStatus(weak) is not { } status) return spot;
            var bar = status.BarBounds;
            if (!new Rect2(spot, window.Size).Intersects(bar)) return spot;
            float room = window.GetViewportRect().Size.Y - window.Size.Y - StatusGap;
            return new Vector2(spot.X, Mathf.Max(spot.Y, Mathf.Min(bar.End.Y + StatusGap, room)));
        });
    }

    /// <summary>
    /// The Genie window's first spot (the client's, beside its own small HUD) lies under the Classic minimap, which is
    /// drawn above it; it opens right of the minimap instead. A window the player moved keeps its place.
    /// </summary>
    private static void OpenClearOfMiniMap(WorldRoot world)
    {
        if (Native.Get<HudWindow>(world, "Genie.Panel") is not { } window || !GodotObject.IsInstanceValid(window.Layout)
            || Native.Get<Func<Vector2>>(window.Layout, "defaultPosition") is not { } placement) return;
        var weak = new WeakReference<WorldRoot>(world);
        Native.Set(window.Layout, "defaultPosition", (Func<Vector2>)(() =>
        {
            var spot = placement();
            if (ClassicStatus(weak) is not { MiniMapBounds: { } map } || !new Rect2(spot, window.Size).Intersects(map)) return spot;
            float room = window.GetViewportRect().Size.X - window.Size.X - StatusGap;
            return new Vector2(Mathf.Max(spot.X, Mathf.Min(map.End.X + StatusGap, room)), spot.Y);
        }));
        // The layout placed the window when it was built; place it again whenever it opens (unless moved or saved).
        var layout = window.Layout;
        window.VisibilityChanged += () =>
        {
            if (Plugin.Active && window.Visible && GodotObject.IsInstanceValid(layout)) Native.TryCall(layout, "ReapplyDefault", out _);
        };
    }

    private static KnightOnlineUiClassic.Windows.StatusHud? ClassicStatus(WeakReference<WorldRoot> weak) =>
        Plugin.Active && weak.TryGetTarget(out var w) && GodotObject.IsInstanceValid(w)
        && Native.Get<System.Collections.IDictionary>(w, "pluginHud") is { } parts && parts.Contains(HudPart.StatusBars)
        && parts[HudPart.StatusBars] is Node part && GodotObject.IsInstanceValid(part) ? Status(part) : null;

    private static KnightOnlineUiClassic.Windows.StatusHud? Status(Node node)
    {
        if (node is KnightOnlineUiClassic.Windows.StatusHud status) return status.IsVisibleInTree() ? status : null;
        if (node is HudWindow) return null;
        foreach (var child in node.GetChildren())
            if (Status(child) is { } found) return found;
        return null;
    }

    public static int NotificationCount(string id)
    {
        if (World is not { } world) return 0;
        return id.ToLowerInvariant() switch
        {
            "mail" => Game.Session.Mail.MailUnread,
            "achievements" => Native.Get<System.Collections.IDictionary>(world, "Achievement.achClaimablePerTab") is { } tabs
                ? tabs.Values.Cast<int>().Sum() : 0,
            "attendance" => Native.Call(world, "Attendance.ClaimableCount") is int count ? count : 0,
            // Attention states rather than counts: an event starting soon, a new day's rankings not yet viewed.
            "events" => Native.Get<bool>(world, "Events.alert") ? 1 : 0,
            "daily_rank" => Native.Get<bool>(world, "DailyRankWindow.drBlink") ? 1 : 0,
            _ => 0,
        };
    }

    public static int SelectedAbs
    {
        get
        {
            if (World is not { } world) return -1;
            int selected = Native.Get<int>(world, "Hotbar.HotSelected");
            if (selected < 0 || Native.Get<int[]>(world, "Hotbar.Slots") is not { } slots) return -1;
            return selected < slots.Length && slots[selected] != 0 ? selected : -1;
        }
    }

    public static void Select(int abs)
    {
        if (World is { } world) Native.Call(world, "Hotbar.SelectHotAbs", abs);
    }

    /// <summary>Whether the client has applied the character's saved skill bar from the server.</summary>
    public static bool HotbarLoaded => World is { } world && Native.Get<bool>(world, "Hotbar.hotLoadedFromServer");

    /// <summary>
    /// The client's own "usable now" test for a skill-bar entry (MP, stamina, weapon, anger, buff, coins, learned,
    /// consumed items, group cooldown), plus the zone and transformation gates it applies when the entry fires.
    /// Null when the client cannot tell, so the caller keeps its own fallback.
    /// </summary>
    public static bool? HotbarUsable(int abs)
    {
        if (World is not { } world || Native.Get<int[]>(world, "Hotbar.Slots") is not { } slots || abs < 0 || abs >= slots.Length) return null;
        int id = slots[abs];
        if (id == 0) return true;
        int zone = Native.Get<int>(world, "ZoneId");
        var skill = SkillData.Get(id);
        if (skill == null && ItemData.Get(id) is { } item)
        {
            if (!ChaosBattle.ItemUsable(zone, item.Kind)) return false;
            if (item.Effect1 != 0) skill = SkillData.Get(item.Effect1);
        }
        if (skill == null) return true;
        if (SkillData.IsSkill(id))
        {
            if (!ZoneSkillTrees.Allowed(zone, skill.Tree, skill.Id)) return false;
            if (Native.Call(world, "SkillCastSystem.SelfTransformModel") is int form && !SkillData.IsGranted(skill.Tree, form)) return false;
        }
        return Native.Call(world, "Hotbar.Usable", skill, Time.GetTicksMsec() / 1000.0) is bool usable ? usable : null;
    }

    public static bool Running => World is not { } world || Native.Get<bool>(world, "Avatar.Running");
    public static bool Sitting => World is { } world && Native.Get<bool>(world, "Avatar.Sitting");
    public static bool AutoAttacking => World is { } world && Native.Get<bool>(world, "BasicAttackSystem.AutoAttack");
    // 3277df5 exposes these through IGameCommands (Run(KeyAction) runs the client's own key action handler,
    // ShowExitMenu opens the game menu); the camera half turn has no key action and stays reflected.
    public static void ToggleRun() { if (World != null) Plugin.Kit.Game.Commands.Run(KeyAction.ToggleRun); }
    public static void ToggleAttack() { if (World != null) Plugin.Kit.Game.Commands.Run(KeyAction.AutoAttack); }
    public static void TurnCamera() => Command("CameraSystem.StartCameraHalfTurn");
    public static void OpenGameMenu() { if (World != null) Plugin.Kit.Game.Commands.ShowExitMenu(); }

    private static void Command(string method) { if (World is { } world) Native.Call(world, method); }

    /// <summary>
    /// Whether the minimap is shown. The Classic HUD hides the native minimap layer, so the native
    /// control's own visibility only records the player's minimap toggle.
    /// </summary>
    public static bool MiniMapVisible => World is not { } world || Native.Get<Control>(world, "Map.MiniMap") is not { } map || map.Visible;
}

/// <summary>
/// Opens and closes the service windows the client does not register as plugin windows (mail,
/// achievements, attendance, Power-Up Store, zone map, genie, events and daily rank) through their own
/// toggles, and passes every other id to the client.
/// </summary>
public sealed class NativeServiceWindows : IGameWindows
{
    private readonly WorldRoot _world;

    public NativeServiceWindows(IGameWindows inner, WorldRoot world)
    {
        Inner = inner;
        _world = world;
    }

    public IGameWindows Inner { get; }

    // The client's top-right icon row (3277df5): forwarded so the host can drop plugin handlers on unload.
    public Rect2 TopRightRow => Inner.TopRightRow;

    public event Action? TopRightRowMoved
    {
        add => Inner.TopRightRowMoved += value;
        remove => Inner.TopRightRowMoved -= value;
    }

    private static readonly Dictionary<string, (string Shown, string Toggle, string? Close)> Services = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mail"] = ("Mail.shown", "Mail.Toggle", null),
        ["achievements"] = ("Achievement.Shown", "Achievement.ToggleAchievements", null),
        ["attendance"] = ("Attendance.shown", "Attendance.Toggle", null),
        ["shoppingmall"] = ("ShoppingMall.shown", "ShoppingMall.Toggle", null),
        ["zonemap"] = ("Map.fullShown", "Map.ToggleFull", null),
        ["genie"] = ("Genie.shown", "Genie.Toggle", "Genie.Close"),
        // Shown by the window itself. The events toggle asks for the schedule; the daily rank one builds the window
        // on first use and records the day as viewed, as the client's top-right icons did.
        ["events"] = ("Events.Window", "Events.Toggle", null),
        ["daily_rank"] = ("DailyRankWindow.Window", "DailyRankWindow.OnIcon", null),
        // The party window is gone; the taskbar's party button opens the party board, which loads its list on open.
        ["seek_party"] = ("SeekParty.SeekShown", "SeekParty.ToggleSeek", null),
    };

    private bool Service(string id, out (string Shown, string Toggle, string? Close) service) =>
        Services.TryGetValue(id, out service) && GodotObject.IsInstanceValid(_world) && Native.Has(_world, service.Shown);

    private bool Shown(string member) => Native.Get<object>(_world, member) switch
    {
        bool shown => shown,
        CanvasItem window => GodotObject.IsInstanceValid(window) && window.Visible,
        _ => false,
    };

    public bool IsOpen(string id) => Service(id, out var service) ? Shown(service.Shown) : Inner.IsOpen(id);

    public void Open(string id)
    {
        if (!Service(id, out var service)) { Inner.Open(id); return; }
        if (!Shown(service.Shown)) Native.Call(_world, service.Toggle);
    }

    public void Close(string id)
    {
        if (!Service(id, out var service)) { Inner.Close(id); return; }
        if (service.Close != null) Native.Call(_world, service.Close);
        else if (Shown(service.Shown)) Native.Call(_world, service.Toggle);
    }

    public void Toggle(string id)
    {
        if (!Service(id, out var service)) { Inner.Toggle(id); return; }
        Native.Call(_world, service.Toggle);
    }
}
