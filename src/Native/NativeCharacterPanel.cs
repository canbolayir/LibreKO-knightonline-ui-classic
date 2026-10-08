using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The Classic character window's view of the client's character, clan, friend and quest state. It
/// keeps its own page selection (the client has no Quest page) and drives the client's existing
/// handlers through <see cref="Native"/>.
/// </summary>
public sealed class CharacterPanelBridge : IGameCharacterPanel
{
    private const string CharacterKey = "character", ClanKey = "clan", FriendsKey = "friends", QuestKey = "quest";
    private const int ClanPage = 1, FriendsPage = 2;
    private const int ClanPointsTab = 1;
    private const long MemberWhisper = 1, MemberParty = 2, MemberAppoint = 4, MemberHandover = 5;
    private const byte ClanChannel = 6, AllianceChannel = 15;
    private const int QuestStateActive = 1, QuestStateCompleted = 2, QuestStateReadyToTurnIn = 3, QuestStateOpen = 4;
    private const int LastQuestFilter = 2, LastQuestKind = 3;
    private const string ClanDetailsId = "character_clan_details";
    private static readonly Vector2 ClanDetailsPosition = new(370, 82);

    private static readonly ConditionalWeakTable<World, CharacterPanelBridge> _bridges = new();

    private readonly WeakReference<World> _world;
    private string _page = CharacterKey;
    private HudWindow? _clanDetails;
    private List<(string Name, int Points)> _donations = new();

    private CharacterPanelBridge(World world)
    {
        _world = new(world);
        if (Net.I != null) Net.I.ClanDonationListEvent += OnDonations;
    }

    /// <summary>The bridge of a client world; one per world so the page selection persists.</summary>
    public static CharacterPanelBridge For(World world) => _bridges.GetValue(world, w => new CharacterPanelBridge(w));

    /// <summary>The Knights Management window the Classic clan page opens, once created.</summary>
    public HudWindow? ClanDetails => _clanDetails != null && GodotObject.IsInstanceValid(_clanDetails) ? _clanDetails : null;

    private World? W => _world.TryGetTarget(out var world) && GodotObject.IsInstanceValid(world) ? world : null;

    private void OnDonations(List<(string Name, int Points)> list)
    {
        if (W == null) { if (Net.I != null) Net.I.ClanDonationListEvent -= OnDonations; return; }
        _donations = list.ToList();
    }

    private static T Get<T>(World world, string member) => Native.Get<T>(world, member)!;
    private static int Int(World world, string member) => Native.TryGet<int>(world, member, out var value) ? value : 0;
    private static object? Call(World world, string method, params object?[] args) => Native.Call(world, method, args);
    private static object Page(int value) => Enum.ToObject(Native.ClientType("LibreKO.World+CharacterPage")!, value);
    private static CharacterSheet? Sheet(World world) => Native.Get<CharacterSheet>(world, "Sheet");

    public string SelectedPage => _page;
    public string RaceName => W is { } w ? StarterStats.RaceName(Int(w, "_selfRace")) : "";
    public string JobName => W is { } w ? CharacterClassCatalog.SpecializationName(Int(w, "_selfClass")) : "";
    public string LevelLabel => W is { } w ? Sheet(w)?.LevelLabel ?? "" : "";
    public string TitleName => W is { } w ? Native.Get<Button>(w, "_stTitleBtn")?.Text ?? "Title: none" : "Title: none";
    public MyClanInfo Clan => Net.I?.MyClan ?? default;
    public int QuestFilter => W is { } w ? Int(w, "_questFilter") : 0;
    public int QuestKind => W is { } w && Native.Get<object>(w, "_questKind") is Enum kind ? Convert.ToInt32(kind) + 1 : 0;

    public IReadOnlyList<Window> Dialogs
    {
        get
        {
            if (W is not { } w) return Array.Empty<Window>();
            return new[] { "_clanInviteAsk", "_clanDisbandAsk", "_clanLeaveAsk", "_clanRemoveAsk", "_clanAllianceAsk", "_clanConfirmAsk", "_clanMemberMenu" }
                .Select(member => Native.Get<Window>(w, member)).OfType<Window>().ToArray();
        }
    }

    public int StatBonus(int row) => W is { } w ? Sheet(w)?.StatBonusAtRow(row) ?? 0 : 0;

    public void SelectPage(string page)
    {
        if (page is not (CharacterKey or ClanKey or FriendsKey or QuestKey) || W is not { } w) return;
        _page = page;
        if (page == QuestKey)
        {
            if (Native.Get<IDictionary>(w, "_characterPages") is { } pages)
                foreach (var content in pages.Values.OfType<Control>()) content.Visible = false;
            Net.I.SendQuestLogRequest();
        }
        else Call(w, "ShowCharacterPage", Page(page == ClanKey ? ClanPage : page == FriendsKey ? FriendsPage : 0));
        SyncClanContent(w);
    }

    /// <summary>
    /// Escape closes Knights Management before the main windows. The client's escape order has no
    /// seam, so the entry goes ahead of its last two entries (main windows, then whispers).
    /// </summary>
    private static void EscapeCloses(World w, HudWindow window)
    {
        const int TrailingEntries = 2;
        if (Native.Get<List<(Func<bool> IsOpen, Action Close)>>(w, "_escapeStack") is not { } stack) return;
        var weak = new WeakReference<HudWindow>(window);
        bool Open() => weak.TryGetTarget(out var shown) && GodotObject.IsInstanceValid(shown) && shown.Visible;
        void Close() { if (weak.TryGetTarget(out var shown) && GodotObject.IsInstanceValid(shown)) shown.Visible = false; }
        stack.Insert(Math.Max(0, stack.Count - TrailingEntries), (Open, Close));
    }

    /// <summary>
    /// Once the clan view lives in Knights Management, it counts as shown (for the client's member
    /// refreshes) while the Clan page or that window is open.
    /// </summary>
    private void SyncClanContent(World w)
    {
        if (ClanDetails is { } details && Native.Get<Control>(w, "_clanContent") is { } content)
            content.Visible = _page == ClanKey || details.Visible;
    }

    public string Status(string section)
    {
        if (W is not { } w) return "";
        return section switch
        {
            "friends" => Native.Get<Label>(w, "_friendStatus")?.Text ?? "",
            "clan" => Native.Get<Label>(w, "_clanStatus")?.Text ?? "",
            "union" => Native.Get<string>(w, "_allianceNotice") ?? "",
            _ => "",
        };
    }

    public IReadOnlyList<GamePanelRow> Rows(string section)
    {
        var rows = new List<GamePanelRow>();
        if (W is not { } w) return rows;
        switch (section)
        {
            case "clan":
                foreach (var m in (Native.Get<List<ClanMember>>(w, "_clanMembers") ?? new()).OrderByDescending(m => m.IsOnline).ThenBy(m => m.Fame)
                             .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var job = CharacterClassCatalog.DisplayName(m.Class);
                    var seen = m.IsOnline ? "Online" : $"Offline: {m.HoursSinceLogin} hours";
                    rows.Add(new(m.Name, new[] { ClanRanks.Name(m.Fame), m.Name, m.Level.ToString(), job },
                        $"{m.Name}\n{ClanRanks.Name(m.Fame)} · Lv {m.Level} · {CharacterClassCatalog.SpecializationName(m.Class)}\n{seen}\n{m.Memo}",
                        m.IsOnline ? new Color("fff0c8") : new Color("b5b5b5"), m.IsOnline));
                }
                break;
            case "friends":
                foreach (var f in (Native.Get<List<FriendEntry>>(w, "_friends") ?? new()).OrderByDescending(f => f.Status).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    rows.Add(new(f.Name, new[] { f.Name }, $"{f.Name}\n{Native.Call(typeof(World), "FriendDetailLine", f)}",
                        f.InParty ? new Color("ff9292") : f.IsOnline ? new Color("8ff099") : new Color("bbbbbb"), f.IsOnline));
                break;
            case "union":
                foreach (var c in Native.Get<List<AllianceClanEntry>>(w, "_allianceClans") ?? new())
                {
                    var officers = string.Join("\n", c.Officers.Select(o => $"{ClanRanks.Name(o.Fame)}: {o.Name}"));
                    rows.Add(new(c.Id.ToString(), new[] { c.Name, c.Id == Clan.AllianceId ? "Leader" : "Member" },
                        $"{c.Name}\n{officers}", c.Id == Clan.ClanId ? new Color("fff0a0") : Colors.White));
                }
                break;
            case "contribution":
                foreach (var d in _donations.OrderByDescending(d => d.Points))
                    rows.Add(new(d.Name, new[] { d.Name, d.Points.ToString("N0") }, $"{d.Name}: {d.Points:N0} Contribution", Colors.White));
                break;
            case "quests":
                QuestRows(w, rows);
                break;
        }
        return rows;
    }

    private static void QuestRows(World w, List<GamePanelRow> rows)
    {
        var selfName = Net.I.LastEnter.Name ?? "";
        var views = Native.Get<Dictionary<int, QuestView>>(w, "_questViews") ?? new();
        var tracked = Native.Get<HashSet<int>>(w, "_questTracked") ?? new();
        foreach (var (id, state) in Call(w, "QuestsInView") as List<(int QuestId, int State)> ?? new())
        {
            var facts = (QuestData.Facts)Call(w, "QuestFacts", id)!;
            var title = (string)Call(w, "QuestName", id, selfName)!;
            var status = (string)Native.Call(typeof(World), "StateName", state)!;
            var shortState = state switch { QuestStateActive => "Active", QuestStateCompleted => "Done", QuestStateReadyToTurnIn => "Ready", QuestStateOpen => "Open", _ => "Open" };
            var reset = "";
            views.TryGetValue(id, out var view);
            if (view != null && view.Daily && view.NextReset > 0)
                reset = $"\nDaily reset: {DateTimeOffset.FromUnixTimeSeconds(view.NextReset).ToLocalTime():g}";
            bool active = state is QuestStateActive or QuestStateReadyToTurnIn;
            bool abandon = active && view?.AutoAccepted != true;
            bool claim = view != null ? view.CanClaim && view.Options.Length == 0 : state == QuestStateReadyToTurnIn;
            bool isTracked = tracked.Contains(id);
            rows.Add(new(id.ToString(), new[] { title, shortState, "—" },
                $"{title}\n{facts.Kind} · Lv {facts.Level} · {status}\n{Call(w, "QuestJournal", id, selfName)}{reset}" + (isTracked ? "\nTracked" : ""),
                state == QuestStateReadyToTurnIn ? new Color("fff080") : Colors.White, true, active, abandon, claim, isTracked));
        }
    }

    public void Refresh(string section)
    {
        if (W is not { } w) return;
        switch (section)
        {
            case "friends": Call(w, "EnsureFriendsLoaded"); Net.I.SendFriendListRequest(); break;
            case "clan": if (Clan.InClan) Net.I.SendClanMembersRequest(); break;
            case "union": if (Clan.InClan) Net.I.SendAllianceList(); break;
            case "contribution": if (Clan.InClan) Net.I.SendClanDonationList(); break;
            case "quests": Net.I.SendQuestLogRequest(); break;
        }
    }

    public void Act(string action, string selection = "", string value = "")
    {
        if (W is not { } w) return;
        bool member = (Native.Get<List<ClanMember>>(w, "_clanMembers") ?? new()).Any(m => string.Equals(m.Name, selection, StringComparison.OrdinalIgnoreCase));
        bool other = !string.Equals(selection, Net.I.LastEnter.Name, StringComparison.OrdinalIgnoreCase);
        var friends = Native.Get<List<FriendEntry>>(w, "_friends") ?? new();
        switch (action)
        {
            case "titles": Call(w, "ToggleTitlePicker"); return;
            case "presets": Call(w, "TogglePreset"); return;
            case "clan_management": OpenClanDetails(w); return;
            case "clan_contribution":
                if (Clan.InClan) { Call(w, "ShowClanTab", Enum.ToObject(Native.ClientType("LibreKO.World+ClanTab")!, ClanPointsTab)); OpenClanDetails(w); }
                return;
            case "clan_donate": if (Clan.InClan) Call(w, "ToggleClanPoints"); return;
            case "clan_leave": if (Clan.InClan) Call(w, "OnLeaveClan"); return;
            case "clan_invite":
                if (Clan.CanInvite && SelectedPlayer(w) is { } invited) Net.I.SendClanInvite(invited);
                else Call(w, "CombatNotice", "Select a player. A chief or vice-chief can invite members.");
                return;
            case "clan_private": if (member && other) MemberAction(w, selection, MemberWhisper); return;
            case "clan_party": if (member && other) MemberAction(w, selection, MemberParty); return;
            case "clan_info": if (member) Call(w, "RequestUserInformation", selection); return;
            case "clan_appoint": if (member && other && Clan.IsChief) MemberAction(w, selection, MemberAppoint); return;
            case "clan_handover": if (member && other && Clan.IsChief) MemberAction(w, selection, MemberHandover); return;
            case "clan_remove":
                if (member && other && Clan.IsChief)
                    Call(w, "AskClanConfirm", "Expel from clan", "Expel", $"Do you really want to expel {selection}?", (Action)(() => Net.I.SendClanKick(selection)));
                return;
            case "union_leave": if (Clan.IsChief) Call(w, "OnAllianceButton"); return;
            case "union_add":
                if (!Clan.IsChief || Clan.Flag < ClanTypes.Promoted) return;
                if (Clan.AllianceId == 0) { Call(w, "OnAllianceButton"); return; }
                if (Clan.AllianceId == Clan.ClanId && SelectedPlayer(w) is { } ally) Net.I.SendAllianceInsert(ally);
                else Call(w, "CombatNotice", "Target the chief of the clan you want to ally with.");
                return;
            case "union_remove":
                if (Clan.IsChief && Clan.AllianceId == Clan.ClanId && int.TryParse(selection, out int clanId) && clanId != Clan.ClanId
                    && (Native.Get<List<AllianceClanEntry>>(w, "_allianceClans") ?? new()).Any(c => c.Id == clanId))
                    Call(w, "AskClanConfirm", "Expel from alliance", "Expel", "Expel the selected clan from the alliance?", (Action)(() => Net.I.SendAlliancePunish(clanId)));
                return;
            case "clan_chat": OpenChannel(w, ClanChannel, "$"); return;
            case "union_chat": OpenChannel(w, AllianceChannel, "&"); return;
            case "friend_add":
                if (Native.Get<LineEdit>(w, "_friendAddInput") is { } input)
                    input.Text = value.Trim().Length > 0 ? value.Trim() : SelectedPlayer(w) is { } id && Native.Get<IDictionary>(w, "_ents")?[id] is { } target
                        ? Native.Get<string>(target, "Name") ?? "" : "";
                Call(w, "DoFriendAdd");
                return;
            case "friend_remove": if (friends.Any(f => f.Name == selection)) Net.I.SendFriendRemove(selection); return;
            case "friend_private": if (friends.Any(f => f.Name == selection && f.IsOnline)) Call(w, "OpenWhisperWith", selection); return;
            case "friend_party": if (friends.Any(f => f.Name == selection && f.IsOnline)) Call(w, "InvitePlayerToParty", selection); return;
            case "quest_filter": if (int.TryParse(value, out int filter) && filter is >= 0 and <= LastQuestFilter) Call(w, "SetQuestFilter", filter); return;
            case "quest_kind":
                if (int.TryParse(value, out int kind) && kind is >= 0 and <= LastQuestKind)
                    Call(w, "SetQuestKind", kind == 0 ? null : Enum.ToObject(typeof(QuestData.Kind), kind - 1));
                return;
            case "quest_select": SelectQuest(w, selection); return;
            case "quest_details": if (SelectQuest(w, selection)) ShowQuestsWindow(w); return;
            case "quest_track": if (SelectQuest(w, selection) && Native.Get<Button>(w, "_questTrackBtn") is { Disabled: false }) Call(w, "ToggleTrackSelectedQuest"); return;
            case "quest_abandon": if (SelectQuest(w, selection) && Native.Get<Button>(w, "_questAbandonBtn") is { Disabled: false }) Call(w, "AbandonSelectedQuest"); return;
            case "quest_complete": if (SelectQuest(w, selection) && Native.Get<Button>(w, "_questCompleteBtn") is { Disabled: false }) Call(w, "CompleteSelectedQuest"); return;
        }
    }

    private static int? SelectedPlayer(World w)
    {
        int id = Int(w, "_selectedId");
        return id > 0 && Native.Get<IDictionary>(w, "_ents") is { } ents && ents.Contains(id) && ents[id] is { } ent
            && !Native.Get<bool>(ent, "IsNpc") ? id : null;
    }

    private static void MemberAction(World w, string name, long action)
    {
        Native.Set(w, "_ctxMember", name);
        Call(w, "OnMemberMenuAction", action);
    }

    private static void OpenChannel(World w, byte channel, string prefix)
    {
        if (Native.Get<object>(w, "Chat") is not { } chat) return;
        Native.Call(chat, "SetChannel", channel);
        (NativeGame.Source as ClientGame)?.RequestChannel(prefix);
        Native.Call(chat, "Open");
    }

    private static bool SelectQuest(World w, string selection)
    {
        if (!int.TryParse(selection, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            || Call(w, "QuestsInView") is not List<(int QuestId, int State)> quests || quests.All(q => q.QuestId != id)) return false;
        Native.Set(w, "_questSelected", id);
        Call(w, "RefreshQuestDetail");
        return true;
    }

    /// <summary>Shows the native Quests window without the refresh that would reload and reset the selection.</summary>
    private static void ShowQuestsWindow(World w)
    {
        if (Native.Get<Dictionary<string, HudWindow>>(w, "_mainWindows") is not { } windows || !windows.TryGetValue("Quests", out var window)) return;
        window.Visible = true;
        window.GetParent()?.MoveChild(window, window.GetParent().GetChildCount() - 1);
        Native.TryCall(w, "SyncMainWindowState", out _);
    }

    /// <summary>
    /// Moves the client's clan management view into its own Knights Management window, which the
    /// Classic character window embeds as a subpage.
    /// </summary>
    private void OpenClanDetails(World w)
    {
        if (Native.Get<Control>(w, "_clanContent") is not { } content) return;
        if (ClanDetails == null)
        {
            int width = Native.TryGet<int>(typeof(World), "CharacterPageWidth", out var pageWidth) ? pageWidth : 0;
            var window = new HudWindow(ClanDetailsId, "Knights Management", ClanDetailsPosition, width) { Visible = false };
            if (content.GetParent() is { } parent)
            {
                parent.RemoveChild(content);
                var slot = new Control { Visible = false };
                parent.AddChild(slot);
                if (Native.Get<IDictionary>(w, "_characterPages") is { } pages) pages[Page(ClanPage)] = slot;
            }
            window.Body.AddChild(content);
            (Native.Get<CanvasLayer>(w, "_mainLayer") ?? (Node)w).AddChild(window);
            _clanDetails = window;
            window.VisibilityChanged += () => { if (W is { } current) SyncClanContent(current); };
            EscapeCloses(w, window);
        }
        content.Visible = true;
        _clanDetails!.Visible = true;
        Call(w, "ApplyMyClan");
        Call(w, "EnsureClanLoaded");
        Call(w, "RefreshClanTab");
    }
}

public partial class ClientGame
{
    private WeakReference<World>? _world;

    /// <summary>The running client world, cached while it stays in the scene tree.</summary>
    private World? ActiveWorld
    {
        get
        {
            if (_world != null && _world.TryGetTarget(out var cached) && GodotObject.IsInstanceValid(cached) && cached.IsInsideTree()) return cached;
            var world = Native.CurrentWorld;
            _world = world == null ? null : new(world);
            return world;
        }
    }

    private IGameCharacterPanel? CharacterPanelSource => ActiveWorld is { } world ? CharacterPanelBridge.For(world) : null;

    internal void RequestChannel(string prefix) => RaiseChannel(prefix);
}
