using System.Collections;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Party roster and seek-party board support: member and listing data on the native rows, a
/// selection that targets the clicked member, a private-message action per listing and the Classic
/// roster's top-right docking.
/// </summary>
public static class NativeParty
{
    private const int MembersPerRecruitment = 8, ListingsPerPage = 10;
    private const string PartyDockKey = "Party";

    public static void PrepareRoster(HudWindow window, World world)
    {
        if (Native.Get<VBoxContainer>(world, "_partyMembersBox") is not { } members) return;
        if (members.Name != "party_members") members.Name = "party_members";
        foreach (var child in members.GetChildren()) Describe(world, members, child);
        members.ChildEnteredTree += child => Describe(world, members, child);
        Dock(window, world);
    }

    private static void Describe(World world, VBoxContainer members, Node node)
    {
        if (node is not Control row || row is Label || row.HasMeta("party_id")) return;
        var party = Net.I.Party;
        int index = row.GetIndex();
        if (index < 0 || index >= party.Count) return;
        var m = party[index];
        bool isLeader = index == 0, self = m.CharId == Native.Get<int>(world, "_myId");
        int hp = m.Hp, maxHp = m.MaxHp, mp = m.Mp, maxMp = m.MaxMp, level = m.Level, cls = m.Class;
        if (self)
        {
            var vitals = Net.I.Vitals;
            hp = vitals.Hp; maxHp = vitals.MaxHp; mp = vitals.Mp; maxMp = vitals.MaxMp;
            level = Net.I.Sheet.Level; cls = Native.Get<int>(world, "_selfClass");
        }
        var status = Net.I.PartyStatusOf(m.CharId);
        row.SetMeta("party_id", m.CharId);
        row.SetMeta("party_name", m.Name);
        row.SetMeta("party_leader", isLeader);
        row.SetMeta("party_hp", hp);
        row.SetMeta("party_max_hp", maxHp);
        row.SetMeta("party_mp", mp);
        row.SetMeta("party_max_mp", maxMp);
        row.SetMeta("party_status", string.Join(", ", status ?? Array.Empty<byte>()));
        row.TooltipText = $"{m.Name} · Lv {level} {CharacterClassCatalog.DisplayName(cls)}\nHP {hp}/{maxHp} · MP {mp}/{maxMp}";
        if (status is { Count: > 0 })
        {
            var names = status.Select(type => Native.Call(typeof(World), "StatusName", type) as string ?? "").Where(name => name.Length > 0).ToArray();
            row.TooltipText += "\n" + string.Join(", ", names);
        }
        int memberId = m.CharId;
        row.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || !row.HasMeta("classic_party_row")) return;
            if (!GodotObject.IsInstanceValid(world)) return;
            members.SetMeta("party_selected", memberId);
            if (Native.Get<IDictionary>(world, "_ents") is { } ents && ents.Contains(memberId) && ents[memberId] is { } member
                && Native.Call(world, "Selectable", member) is true)
                Native.Call(world, "Select", memberId, member);
        };
    }

    /// <summary>Docks the Classic roster at the top-right screen edge, outside the right-hand window stack.</summary>
    private static void Dock(HudWindow window, World world)
    {
        if (Native.Get<IDictionary>(world, "_mainWindows") is not { } windows || !windows.Contains(PartyDockKey) || windows[PartyDockKey] != window) return;
        window.DockTo(() => new Vector2(Mathf.Max(0, window.GetViewportRect().Size.X - Footprint(window).X), 0));
        void Leave()
        {
            Native.Get<List<string>>(world, "_dockOrder")?.Remove(PartyDockKey);
            Callable.From(() => { if (GodotObject.IsInstanceValid(window) && window.Visible && !Config.HasWindowPos(window.Id)) Native.TryCall(window.Layout, "ReapplyDefault", out _); }).CallDeferred();
        }
        window.VisibilityChanged += Leave;
        window.Resized += Leave;
        if (window.IsInsideTree()) window.GetViewport().SizeChanged += () => { if (GodotObject.IsInstanceValid(window)) Leave(); };
        Leave();
    }

    private static Vector2 Footprint(Control window)
    {
        var minimum = window.GetCombinedMinimumSize();
        return new Vector2(Mathf.Max(window.Size.X, minimum.X), Mathf.Max(window.Size.Y, minimum.Y)) * window.Scale;
    }

    public static void PrepareBoard(HudWindow window, World world)
    {
        if (Native.Get<VBoxContainer>(world, "_seekListBox") is not { } list) return;
        if (list.Name != "seek_members") list.Name = "seek_members";
        // Rows from the previous page are queued for deletion; drop them before the new page is shown.
        list.ChildEnteredTree += _ =>
        {
            foreach (var old in list.GetChildren().Where(c => c.IsQueuedForDeletion()).ToArray()) list.RemoveChild(old);
        };
        NativeWindows.Sync(window, () => Mirror(world));
        if (Net.I == null) return;
        var net = Net.I;
        var weakWorld = new WeakReference<World>(world);
        Action<int, int, List<PartyBbsEntry>>? handler = null;
        handler = (page, total, entries) =>
        {
            if (!weakWorld.TryGetTarget(out var w) || !GodotObject.IsInstanceValid(w)) { net.PartyBbsListEvent -= handler; return; }
            Listed(w, entries);
        };
        net.PartyBbsListEvent += handler;
    }

    private static void Mirror(World world)
    {
        if (Native.Get<Button>(world, "_seekRegisterBtn") is { } register) register.SetMeta("seeking", Native.Get<bool>(world, "_seeking"));
        if (Native.Get<Label>(world, "_seekPageLbl") is { } page && page.Text.StartsWith("Page ") && page.Text.Contains(" / "))
        {
            int pages = Math.Max(1, (Native.Get<int>(world, "_seekTotal") + ListingsPerPage - 1) / ListingsPerPage);
            page.SetMeta("page_caption", $"{Native.Get<int>(world, "_seekPage") + 1} / {pages}");
        }
    }

    /// <summary>Describes the board's rows after the client listed a page of seek-party entries.</summary>
    public static void Listed(World world, IReadOnlyList<PartyBbsEntry> entries)
    {
        if (Native.Get<VBoxContainer>(world, "_seekListBox") is not { } list) return;
        foreach (var old in list.GetChildren().Where(c => c.IsQueuedForDeletion()).ToArray()) list.RemoveChild(old);
        Mirror(world);
        var rows = list.GetChildren().OfType<PanelContainer>().ToArray();
        var classes = Native.Get<Array>(typeof(World), "WantedClasses");
        for (int i = 0; i < rows.Length && i < entries.Count; i++)
        {
            var e = entries[i];
            var panel = rows[i];
            string wanted = classes != null && classes.Length > 0
                ? Native.Get<string>(classes.GetValue(Math.Clamp(e.ClassOrWanted, 0, classes.Length - 1))!, "Item1") ?? "" : "";
            panel.SetMeta("seek_name", e.Name);
            panel.SetMeta("seek_level", e.IsLeaderRecruiting ? $"{e.MemberCount}/{MembersPerRecruitment}" : e.Level.ToString());
            panel.SetMeta("seek_class", e.IsLeaderRecruiting ? wanted : CharacterClassCatalog.DisplayName(e.ClassOrWanted));
            panel.SetMeta("seek_detail", $"{Native.Call(typeof(World), "SeekZoneName", e.ZoneId)}\n{e.Message}");
            if (panel.GetChildCount() > 0 && panel.GetChild(0) is HBoxContainer row && !row.GetChildren().OfType<Button>().Any(b => b.Text == "Private"))
            {
                string name = e.Name;
                var whisper = new Button { Text = "Private", Visible = false, FocusMode = Control.FocusModeEnum.None };
                whisper.AddThemeFontSizeOverride("font_size", 12);
                whisper.Pressed += () => { if (GodotObject.IsInstanceValid(world)) Native.Call(world, "OpenWhisperWith", name); };
                row.AddChild(whisper);
            }
        }
    }
}
