using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>Native world overlays that have no window id: prompts, event plates, banners, boards and loot.</summary>
public enum NativeOverlay
{
    /// <summary>The temple event registration modal (a full-rect host named <c>event_registration</c>).</summary>
    Registration,
    /// <summary>The registration countdown plate.</summary>
    Countdown,
    /// <summary>The in-zone event leave plate.</summary>
    Leave,
    /// <summary>The death notice panel.</summary>
    Death,
    /// <summary>A native confirmation dialog (party invite, duel challenge, town recall).</summary>
    Prompt,
    /// <summary>A centre-screen broadcast banner or prompt line.</summary>
    Announcement,
    /// <summary>The zone type chip.</summary>
    Chip,
    /// <summary>A war score strip.</summary>
    Score,
    /// <summary>The loot window.</summary>
    Loot,
    /// <summary>A multi-line event box with its own controls (Knight Royale sign-up, genie quick panel).</summary>
    Board,
    /// <summary>A centre toast plate (Monster Stone and Chaos timers).</summary>
    Toast,
    /// <summary>The event title chips beside the premium chip.</summary>
    Titles,
    /// <summary>A one-line label plate (Nereid flag and score boards).</summary>
    LabelPlate,
    /// <summary>A free-standing action button (siege drop-off).</summary>
    Action,
    /// <summary>The siege tower button, which the client puts on its own (hidden) target bar.</summary>
    TowerButton,
    /// <summary>The ranker statue's message paper.</summary>
    Paper,
    /// <summary>The genie quick panel (an event box with its own minimise, close and time rows).</summary>
    GenieQuick,
    /// <summary>The Wanted list buttons, which the client puts on its own (hidden) mini map.</summary>
    Wanted,
    /// <summary>A label plate the client puts on its own (hidden) mini map (the Nereid monument strip).</summary>
    MapPlate,
}

/// <summary>
/// Prepares the client's world overlays for the Classic skins once the world is attached. The
/// preparer names the native controls a skin looks for and mirrors native state into metas
/// (<see cref="Urgent"/>, <see cref="Registered"/>, <see cref="RegisteredElsewhere"/>); every native control,
/// callback and timer stays.
/// </summary>
public static class NativeOverlays
{
    public const string Urgent = "overlay_urgent", Registered = "overlay_registered";
    /// <summary>Meta on the registration: the character is registered for another open sign-up than the shown one.</summary>
    public const string RegisteredElsewhere = "overlay_registered_elsewhere";
    /// <summary>Meta on the order message: the area banner it must stay below while both are shown.</summary>
    public const string StackBelow = "overlay_stack_below";
    private const string Prepared = "classic_overlays";
    private static readonly Dictionary<NativeOverlay, List<Action<Node>>> _skins = new();

    public static void Extend(NativeOverlay part, Action<Node> skin)
    {
        if (!_skins.TryGetValue(part, out var list)) _skins[part] = list = new();
        list.Add(skin);
    }

    internal static void Register(PluginContext context)
    {
        var game = context.Game;
        Action attached = () => { if (NativeHudChat.World is { } world) Prepare(world); };
        NativeHooks.Listen("Game.BecameAvailable", () => game.BecameAvailable += attached, () => game.BecameAvailable -= attached);
    }

    internal static void Reset() => _skins.Clear();

    /// <summary>
    /// Overlays the client builds later (or rebuilds), checked four times a second: the event plates and boards,
    /// banners and splashes, the siege tower actions, the ranker paper and the Wanted buttons.
    /// </summary>
    private static readonly (string Field, NativeOverlay Part, string Name)[] Later =
    {
        ("SoccerSystem.soccerBoard", NativeOverlay.Score, "soccer_score"),
        ("Chaos.NestTimerPanel", NativeOverlay.Toast, "event_timer"),
        ("KnightRoyaleWindow.RoyaleBoard", NativeOverlay.Board, "royale_board"),
        ("GenieSub.Sub", NativeOverlay.GenieQuick, "genie_quick"),
        ("EventTitlesWindow.TitleColumn", NativeOverlay.Titles, "event_titles"),
        ("GatherSystem.gatherChip", NativeOverlay.Chip, "gather_chip"),
        ("KnightRoyaleWindow.areaBanner", NativeOverlay.Announcement, "area_banner"),
        ("MsgOut.msgOut", NativeOverlay.Announcement, "skill_message"),
        ("ChatNotices.orderMessage", NativeOverlay.Announcement, "order_message"),
        ("KillSplash.killSplash", NativeOverlay.Announcement, "kill_splash"),
        ("Nereid.FlagBoard", NativeOverlay.LabelPlate, "nereid_flags"),
        ("Nereid.ScoreBoard", NativeOverlay.LabelPlate, "nereid_score"),
        ("Nereid.Strip", NativeOverlay.MapPlate, "nereid_strip"),
        ("SiegeTowerSystem.DropOff", NativeOverlay.Action, "siege_drop_off"),
        ("SiegeTowerSystem.TowerButton", NativeOverlay.TowerButton, "siege_tower"),
        ("RankerMessageWindow.Paper", NativeOverlay.Paper, "ranker_paper"),
        ("VanguardWindow.wantedToggle", NativeOverlay.Wanted, "wanted_toggle"),
        ("VanguardWindow.wantedOpen", NativeOverlay.Wanted, "wanted_open"),
    };

    /// <summary>Prepares the overlays that already exist and the later ones once (safe to call repeatedly).</summary>
    public static void PrepareLater(WorldRoot world)
    {
        foreach (var (field, part, name) in Later)
        {
            if (!Native.Has(world, field) || Native.Get<Node>(world, field) is not { } node || !GodotObject.IsInstanceValid(node)
                || node.IsQueuedForDeletion() || node.HasMeta(Prepared)) continue;
            node.SetMeta(Prepared, true);
            node.Name = name;
            if (part == NativeOverlay.GenieQuick) NativeGenieQuick.Bind(node, world);
            Apply(part, node);
        }
        // The client draws the order message (top 110) over the area banner's title (top 100); the skin stacks them.
        if (Native.Has(world, "ChatNotices.orderMessage") && Native.Has(world, "KnightRoyaleWindow.areaBanner")
            && Native.Get<Control>(world, "ChatNotices.orderMessage") is { } order && GodotObject.IsInstanceValid(order) && !order.HasMeta(StackBelow)
            && Native.Get<Control>(world, "KnightRoyaleWindow.areaBanner") is { } area && GodotObject.IsInstanceValid(area))
            order.SetMeta(StackBelow, area);
        NativeContextMenus.PrepareLater(world);
    }

    /// <summary>Prepares every overlay of a world; safe to call again.</summary>
    public static void Prepare(WorldRoot world)
    {
        if (!GodotObject.IsInstanceValid(world) || world.HasMeta(Prepared)) return;
        world.SetMeta(Prepared, true);
        PrepareEvents(world);
        Apply(NativeOverlay.Death, Named(world, "DeathDialog.Panel", "death_notice"));
        foreach (var (field, name) in new[] { ("PartyInvites.inviteAskDialog", "party_invite_prompt"), ("PartyInvites.inviteWaitDialog", "party_invite_wait"), ("Challenge.askDialog", "duel_prompt"), ("TownRecall.dialog", "town_recall_prompt") })
            Apply(NativeOverlay.Prompt, Named(world, field, name));
        foreach (var (field, name) in new[] { ("UpgradeNotice.label", "upgrade_announcement"), ("Shout.label", "shout_announcement") })
            if (Native.Get<Label>(world, field)?.GetParent()?.GetParent() is PanelContainer panel) Apply(NativeOverlay.Announcement, Named(panel, name));
        Apply(NativeOverlay.Announcement, Named(world, "BattleEvent.battleeventBanner", "battle_announcement"));
        Apply(NativeOverlay.Announcement, Named(world, "BattleEvent.battleeventResult", "battle_result"));
        Apply(NativeOverlay.Announcement, Named(world, "ClanBattle.panel", "clan_battle_announcement"));
        Apply(NativeOverlay.Announcement, Named(world, "Loot.prompt", "loot_prompt"));
        Apply(NativeOverlay.Chip, Named(world, "ZoneAbility.ZoneabilityChip", "zone_chip"));
        Apply(NativeOverlay.Score, Named(world, "BattleEvent.BattleeventBoard", "battle_score"));
        Apply(NativeOverlay.Score, Named(world, "BorderDefenseWar.BdwScoreBanner", "border_war_score"));
        PrepareLoot(world);
        NativeContextMenus.Prepare(world);
        PrepareLater(world);
        double next = 0;
        world.AddChild(new NativeEventWatcher(() =>
        {
            if (Time.GetTicksMsec() < next || !GodotObject.IsInstanceValid(world)) return;
            next = Time.GetTicksMsec() + 250;
            PrepareLater(world);
        }) { Name = "classic_overlay_later" });
    }

    private static void PrepareEvents(WorldRoot world)
    {
        var modal = Named(world, "Bifrost.joinModal", "event_registration");
        Named(world, "Bifrost.joinModalTitle", "registration_title");
        Named(world, "Bifrost.joinModalTimer", "registration_timer");
        Named(world, "Bifrost.joinModalBar", "registration_bar");
        Named(world, "Bifrost.joinModalStatus", "registration_status");
        Named(world, "Bifrost.joinModalCount", "registration_count");
        Named(world, "Bifrost.joinModalBtn", "registration_join");
        Named(world, "Bifrost.joinModalCloseBtn", "registration_close");
        var countdown = Named(world, "Bifrost.banner", "event_countdown");
        Named(world, "Bifrost.titleLbl", "countdown_title");
        Named(world, "Bifrost.timerLbl", "countdown_timer");
        Named(world, "Bifrost.bar", "countdown_bar");
        var leave = Named(world, "Bifrost.inZoneLeaveBanner", "event_leave");
        Named(world, "Bifrost.inZoneLeaveTitleLbl", "leave_title");
        Named(world, "Bifrost.inZoneLeaveBtn", "leave_action");
        // The registration and countdown share the client's remaining time and registration state. The state is read
        // only while one of them is shown, after the client's own frame and just before the skins sync to it.
        if (modal is Control host)
        {
            int urgentSeconds = Native.Get<int>(Native.StaticOwner("LibreKO.Features.Windows.BifrostFeature"), "UrgentSecs");
            var watcher = new NativeEventWatcher(() =>
            {
                if (!GodotObject.IsInstanceValid(world)) return;
                if (!host.IsVisibleInTree() && !(countdown is CanvasItem plate && GodotObject.IsInstanceValid(plate) && plate.IsVisibleInTree())) return;
                var (left, registered, elsewhere) = SignUpState(world);
                bool urgent = left > 0 && left <= urgentSeconds;
                foreach (var node in new[] { host, countdown })
                {
                    if (node == null || !GodotObject.IsInstanceValid(node)) continue;
                    node.SetMeta(Urgent, urgent);
                    node.SetMeta(Registered, registered);
                    node.SetMeta(RegisteredElsewhere, elsewhere);
                }
            }) { Name = "classic_event_state", ProcessPriority = int.MaxValue - 2 };
            host.AddChild(watcher);
        }
        Apply(NativeOverlay.Registration, modal);
        Apply(NativeOverlay.Countdown, countdown);
        Apply(NativeOverlay.Leave, leave);
    }

    /// <summary>
    /// The shown sign-up's remaining seconds and whether the character is registered for it or for another open
    /// sign-up. Upstream fa3cfc3 keeps the sign-ups per event on <c>Net.SignUps</c> (<c>TempleSignUpBoard</c>): the
    /// registration shows <c>Current</c> with <c>SecondsLeft(Current, utc)</c>, and with no sign-up open the plaque
    /// counts the plain Bifrost time (<c>BifrostSecondsLeft</c>). Older clients keep one sign-up on the World
    /// (<c>_bifrostRemaining</c>, <c>_isEventRegistered</c>).
    /// </summary>
    internal static (int Seconds, bool Registered, bool Elsewhere) SignUpState(WorldRoot world)
    {
        var board = Game.Session.Bifrost.SignUps;
        var current = Native.Get<object>(board, "Current");
        int shown = current == null ? 0 : Convert.ToInt32(current);
        int mine = Native.Get<object>(board, "Registered") is { } registered ? Convert.ToInt32(registered) : 0;
        var utc = DateTime.UtcNow;
        object? seconds = shown != 0 ? Native.Call(board, "SecondsLeft", current, utc) : Native.Call(world, "Bifrost.SecondsLeft", utc);
        return (seconds is int left ? left : 0, shown != 0 && mine == shown, mine != 0 && mine != shown);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node, WorldRoot> _lootWorlds = new();

    private static void PrepareLoot(WorldRoot world)
    {
        // The loot box is a twelve-cell grid of client item cells (entry order) with Take all and Filter under it.
        if (Named(world, "Loot.Panel", "loot_window") is not { } panel || Native.Get<ItemSlotView[]>(world, "Loot.cells") is not { } cells) return;
        for (int i = 0; i < cells.Length; i++)
        {
            if (Named(cells[i], "loot_slot_" + i) == null) return;
            if (i == 0) Named(cells[i].GetParent(), "loot_grid");
        }
        if (cells[0].GetParent()?.GetParent() is Node column)
            foreach (var actions in column.GetChildren().OfType<HBoxContainer>())
            {
                var buttons = actions.GetChildren().OfType<Button>().ToArray();
                if (buttons.Length < 2) continue;
                Named(actions, "loot_actions"); Named(buttons[0], "loot_take_all"); Named(buttons[1], "loot_filter");
            }
        _lootWorlds.AddOrUpdate(panel, world);
        Apply(NativeOverlay.Loot, panel);
    }

    /// <summary>Closes the loot box through the client, as Escape does; the client's box has no close button.</summary>
    public static void CloseLoot(Node panel)
    {
        if (_lootWorlds.TryGetValue(panel, out var world) && GodotObject.IsInstanceValid(world)) Native.Call(world, "Loot.Close");
    }

    private static Node? Named(WorldRoot world, string field, string name) => Named(Native.Get<Node>(world, field), name);

    private static Node? Named(Node? node, string name)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return null;
        node.Name = name;
        return node;
    }

    private static void Apply(NativeOverlay part, Node? node)
    {
        if (node == null || !_skins.TryGetValue(part, out var skins)) return;
        foreach (var skin in skins)
        {
            try { skin(node); }
            catch (Exception e) { GD.PushError($"[plugin:knightonline-ui-classic] {node.Name} overlay: {e}"); }
        }
    }
}

public static partial class NativeSetup
{
    static partial void RegisterOverlays(PluginContext context) => NativeOverlays.Register(context);
}
