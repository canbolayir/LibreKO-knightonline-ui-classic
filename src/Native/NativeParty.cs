using System.Collections;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Party HUD and seek-party board support: the native party actions the Classic party HUD offers beyond the
/// plugin party API (invite by name, party board, member menu), and on the board the listing data on the
/// native rows plus a private-message action per listing. The row meter, war board, row order, automatic
/// re-invite, commander bar and user vote go through <see cref="LibreKO.Plugins.IGameParty"/>.
/// </summary>
public static class NativeParty
{
    private const int MembersPerRecruitment = 8, ListingsPerPage = 10;

    /// <summary>The client's invite-by-name prompt (the Classic text prompt when the theme replaces it).</summary>
    public static void InviteByName(WorldRoot? world) { if (world != null) Native.TryCall(world, "PartyInvites.OpenInvitePrompt", out _); }

    /// <summary>The party board through the client's toggle, which also loads its list.</summary>
    public static void ToggleBoard(WorldRoot? world) { if (world != null) Native.TryCall(world, "SeekParty.ToggleSeek", out _); }

    public static bool IsCommander(int memberId) => Game.Session.Party.PartyCommanderId == memberId;

    /// <summary>Shows the native member menu as the Classic context menu, titled with the member's name.</summary>
    public static void PrepareMemberMenu(WorldRoot? world, string name) =>
        NativeContextMenus.MirrorPartyMenu(world)?.SetMeta(KnightOnlineUiClassic.Windows.ClassicContextMenu.TitleMeta, name);

    public static void PrepareBoard(HudWindow window, WorldRoot world)
    {
        if (Native.Get<VBoxContainer>(world, "SeekParty.seekListBox") is not { } list) return;
        if (list.Name != "seek_members") list.Name = "seek_members";
        NativeWindows.Name(Native.Get<OptionButton>(world, "SeekParty.wantedClassOpt"), "seek_wanted_class");
        NativeWindows.Name(Native.Get<LineEdit>(world, "SeekParty.wantedMsgInput"), "seek_wanted_message");
        NativeWindows.Name(Native.Get<HBoxContainer>(world, "SeekParty.seekFilters"), "seek_filters");
        if (Native.Get<HBoxContainer>(world, "SeekParty.fullMoonRow") is { } fullMoon && NativeWindows.Name(fullMoon, "seek_full_moon"))
        {
            // Enter, Party Board and (3277df5) Auto Entry, which joins the first fitting Full Moon Rift party.
            var actions = fullMoon.GetChildren().OfType<Button>().ToArray();
            string[] names = { "seek_full_moon_enter", "seek_full_moon_shout", "seek_full_moon_auto" };
            for (int i = 0; i < actions.Length && i < names.Length; i++) actions[i].Name = names[i];
            if (actions.Length > 2) actions[2].TooltipText = "Join the first open Full Moon Rift party that fits your class and level";
        }
        NativeWindows.Name(Native.Get<Label>(world, "SeekParty.seekHeading"), "seek_heading");
        NativeWindows.Name(Native.Get<Label>(world, "SeekParty.seekStatusLbl"), "seek_status");
        if (Native.Descendants(window.Body).OfType<Button>().FirstOrDefault(b => b.Text == "Invite by name") is { } byName) byName.Name = "seek_invite_name";
        // Rows from the previous page are queued for deletion; drop them before the new page is shown. Filter
        // changes rebuild the rows without a server reply, so rows are described as they arrive.
        bool queued = false;
        list.ChildEnteredTree += _ =>
        {
            foreach (var old in list.GetChildren().Where(c => c.IsQueuedForDeletion()).ToArray()) list.RemoveChild(old);
            if (queued) return;
            queued = true;
            Callable.From(() => { queued = false; if (GodotObject.IsInstanceValid(world)) Listed(world); }).CallDeferred();
        };
        NativeWindows.Sync(window, () => Mirror(world));
        if (Game.Connection == null) return;
        var weakWorld = new WeakReference<WorldRoot>(world);
        Action<int, int, List<PartyBbsEntry>> handler = (page, total, entries) =>
        {
            if (weakWorld.TryGetTarget(out var w) && GodotObject.IsInstanceValid(w)) Listed(w);
        };
        NativeHooks.Listen("Net.PartyBbsListEvent", () => Game.Session.Party.PartyBbsListEvent += handler, () => Game.Session.Party.PartyBbsListEvent -= handler, world);
    }

    private static void Mirror(WorldRoot world)
    {
        if (Native.Get<Button>(world, "SeekParty.seekRegisterBtn") is { } register) register.SetMeta(HotNames.Seeking, Native.Get<bool>(world, "SeekParty.seeking"));
        if (Native.Get<Label>(world, "SeekParty.seekPageLbl") is { } page && page.Text.StartsWith("Page ") && page.Text.Contains(" / "))
        {
            int pages = Math.Max(1, (Native.Get<int>(world, "SeekParty.seekTotal") + ListingsPerPage - 1) / ListingsPerPage);
            page.SetMeta(HotNames.PageCaption, $"{Native.Get<int>(world, "SeekParty.seekPage") + 1} / {pages}");
        }
    }

    /// <summary>
    /// Describes the board's rows after the client listed a page of seek-party entries: the rows show the
    /// entries that pass the client's board filter, without the player's own listing.
    /// </summary>
    public static void Listed(WorldRoot world, IReadOnlyList<PartyBbsEntry> _) => Listed(world);

    public static void Listed(WorldRoot world)
    {
        if (Native.Get<VBoxContainer>(world, "SeekParty.seekListBox") is not { } list) return;
        var all = Native.Get<List<PartyBbsEntry>>(world, "SeekParty.seekEntries") ?? new List<PartyBbsEntry>();
        var filter = Native.Get<PartyBoardFilter>(world, "SeekParty.seekFilter");
        int zone = Native.Get<int>(world, "ZoneId"), level = Game.Session.Self.Sheet.Level;
        string self = Game.Session.Self.LastEnter.Name ?? "";
        var entries = all.Where(e => filter.Passes(e, zone, level) && !string.Equals(e.Name, self, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var old in list.GetChildren().Where(c => c.IsQueuedForDeletion()).ToArray()) list.RemoveChild(old);
        Mirror(world);
        var rows = list.GetChildren().OfType<PanelContainer>().ToArray();
        var classes = Native.Get<Array>(Native.StaticOwner("LibreKO.Features.Hud.SeekPartyFeature"), "WantedClasses");
        for (int i = 0; i < rows.Length && i < entries.Count; i++)
        {
            var e = entries[i];
            var panel = rows[i];
            string wanted = classes != null && classes.Length > 0
                ? Native.Get<string>(classes.GetValue(Math.Clamp(e.ClassOrWanted, 0, classes.Length - 1))!, "Item1") ?? "" : "";
            panel.SetMeta("seek_name", e.Name);
            panel.SetMeta("seek_level", e.IsLeaderRecruiting ? $"{e.MemberCount}/{MembersPerRecruitment}" : e.Level.ToString());
            panel.SetMeta("seek_class", e.IsLeaderRecruiting ? wanted : CharacterClassCatalog.DisplayName(e.ClassOrWanted));
            panel.SetMeta("seek_detail", $"{Native.Call(Native.StaticOwner("LibreKO.Features.Hud.SeekPartyFeature"), "SeekZoneName", e.ZoneId)}\n{e.Message}");
            if (panel.GetChildCount() > 0 && panel.GetChild(0) is HBoxContainer row && !row.GetChildren().OfType<Button>().Any(b => b.Text == "Private"))
            {
                string name = e.Name;
                var whisper = new Button { Text = "Private", Visible = false, FocusMode = Control.FocusModeEnum.None };
                whisper.AddThemeFontSizeOverride("font_size", 12);
                whisper.Pressed += () => { if (GodotObject.IsInstanceValid(world)) Native.Call(world, "Whispers.OpenWhisperWith", name); };
                row.AddChild(whisper);
            }
        }
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName PageCaption = "page_caption";
    internal static readonly StringName Seeking = "seeking";
}
