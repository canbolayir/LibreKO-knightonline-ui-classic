using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Native;

/// <summary>
/// Achievements, attendance, rankings and genie fixtures. Each builds the client's own window through its
/// native initializer, feeds it the server replies a live session would receive, then runs this group's
/// preparer as the client's window flow would before the skin is applied.
/// </summary>
public static partial class PreviewFixtures
{
    private static HudWindow DetachWindow(WorldRoot world, string layerField, string windowField)
    {
        var layer = Field<CanvasLayer>(world, layerField);
        var window = Field<HudWindow>(world, windowField);
        layer.RemoveChild(window);
        NativeProgression.Prepare(window, world);
        return window;
    }

    /// <summary>Real achievement ids from the client's definitions, grouped by tab.</summary>
    public static List<int> AchievementIds(AchievementTab tab) =>
        AchievementData.Ids.Where(id => AchievementData.Get(id)?.Tab == tab).OrderBy(id => id).ToList();

    public static HudWindow BuildAchievementsUiPreview(WorldRoot world, string view)
    {
        ItemData.EnsureLoaded();
        Native.Call(Game.Session.Achievement, "Clear");
        Call(world, "Achievement.Build");
        Call(world, "Achievement.ToggleAchievements");
        var entries = new List<AchievementEntry>();
        if (view != "empty")
        {
            foreach (var tab in AchievementData.Tabs)
            {
                var ids = AchievementIds(tab);
                for (int i = 0; i < ids.Count; i++)
                {
                    var state = i % 5 == 0 ? AchievementState.Achieved : i % 5 == 1 ? AchievementState.Claimed : AchievementState.InProgress;
                    entries.Add(new AchievementEntry { Id = ids[i], State = state, Progress = state != AchievementState.InProgress ? 10 : i % 10, Target = 10 });
                }
            }
            Native.Call(Game.Session.Achievement, "ApplyList", entries);
            var recent = entries.Where(e => e.Claimed || e.Claimable).Take(3).Select(e => e.Id).ToArray();
            Native.Call(Game.Session.Achievement, "ApplySummary", new AchievementSummary
            {
                PlayMinutes = 9_754, MonstersDefeated = 48_211, PlayersDefeated = 1_337, Deaths = 212, Points = 2_450,
                RecentlyAchieved = recent, AchievedPerTab = new int[AchievementData.Tabs.Length],
            });
        }
        if (view is "list" or "claimed") Call(world, "Achievement.SelectTab", AchievementTab.War);
        if (view == "claimed") Field<CheckButton>(world, "Achievement.AchHideClaimed").ButtonPressed = true;
        return DetachWindow(world, "Achievement.achLayer", "Achievement.AchPanel");
    }

    public static HudWindow BuildAttendanceUiPreview(WorldRoot world, string view)
    {
        ItemData.EnsureLoaded();
        Call(world, "Attendance.Build");
        Native.Call(Game.Session.Attendance, "NoteAutoOpened");
        int total = AttendanceWire.DailySlots + AttendanceWire.BonusSlots;
        var slots = new int[total]; var states = new byte[total];
        int attended = view switch { "empty" => 0, "complete" => AttendanceWire.DailySlots, _ => 12 };
        for (int i = 0; i < AttendanceWire.DailySlots; i++)
        {
            slots[i] = i + 1;
            states[i] = i < attended - 1 ? AttendanceSlotState.Claimed
                : i == attended - 1 ? (view == "complete" ? AttendanceSlotState.Claimed : AttendanceSlotState.Claimable)
                : i == attended ? AttendanceSlotState.ClaimableExtra : (byte)0;
            if (view == "empty") states[i] = i == 0 ? AttendanceSlotState.Claimable : (byte)0;
        }
        for (int i = 0; i < AttendanceWire.BonusSlots; i++)
        {
            int index = AttendanceWire.DailySlots + i;
            slots[index] = AttendanceWire.BonusFirstSlot + i;
            states[index] = view == "complete" ? AttendanceSlotState.Claimed : i == 0 && view != "empty" ? AttendanceSlotState.Claimable : (byte)0;
        }
        Call(world, "Attendance.OnBoard", slots, states);
        Call(world, "Attendance.Open");
        if (view == "failed") Call(world, "Attendance.OnFailed", EventBoardSubOpcode.AttendanceClaim, EventBoardResult.InventoryFull);
        return DetachWindow(world, "Attendance.layer", "Attendance.Panel");
    }

    public static List<RankEntry> RankRows(int nation, int count)
    {
        string[] names = { "Rikka", "LongCharacterName1299", "Zeus", "Ariel", "Marduk", "Ranger", "Guardian", "Magician", "Warrior", "Priestess" };
        string[] clans = { "canCLAN", "", "TheVeryLongClanNameOfAll", "Moradon", "Guardians" };
        return Enumerable.Range(0, count).Select(i => new RankEntry
        {
            Name = names[i % names.Length] + (i >= names.Length ? (i / names.Length).ToString() : ""), Nation = nation,
            ClanName = clans[i % clans.Length], Loyalty = 125_000 - i * 7_310, Place = i + 1,
            Bonus = i % 3 == 0 ? 1_250 - i * 40 : 0,
        }).ToList();
    }

    /// <summary>The PK ladder as the client shows it while the ladder key is held in a ladder zone.</summary>
    public static HudWindow BuildRankUiPreview(WorldRoot world, string view)
    {
        Call(world, "Rank.Build");
        // The player's own nation carries a full ladder; the other side may be short or empty.
        int own = view == "empty" ? 0 : view == "short" ? 4 : PkLadder.RowsShown, other = view == "empty" ? 0 : view == "short" ? 7 : PkLadder.RowsShown;
        bool karus = Game.Session.Self.LastEnter.Nation == Nations.Karus;
        Call(world, "Rank.OnData", new PkLadderReply(RankRows(1, karus ? own : other), RankRows(2, karus ? other : own),
            view == "empty" ? 0 : 6, view == "empty" ? 0 : 21_344, view == "empty" ? 0 : 480));
        var window = Field<HudWindow>(world, "Rank.Panel");
        window.Visible = true; SetField(world, "Rank.shown", true);
        return DetachWindow(world, "Rank.layer", "Rank.Panel");
    }

    /// <summary>The column a skill belongs to, as the client sorts them: enemy skills, HP/MP restoring skills, the rest.</summary>
    public static GenieCellKind GenieKindOf(SkillData.Skill s) =>
        s.IsEnemy ? GenieCellKind.Attack
        : GenieRecovery.RestoresHp(s.IsFriendly, s.Type1, s.DirectType, s.FirstDamage, s.TimeDamage)
            || GenieRecovery.RestoresMp(s.IsFriendly, s.Type1, s.DirectType, s.FirstDamage) ? GenieCellKind.Recovery
        : GenieCellKind.Support;

    public static HudWindow BuildGenieUiPreview(WorldRoot world, string view)
    {
        ItemData.EnsureLoaded();
        Call(world, "Genie.Build");
        if (view != "empty")
        {
            SkillData.EnsureLoaded();
            var setup = Field<GenieSetup>(world, "Genie.Genie");
            // Every other cell of each column takes a class skill with an icon; some support skills also go to the party.
            var known = SkillData.ForClass(KnightOnlineUiClassic.Plugin.Kit.Nation == 1 ? 105 : 205).Where(s => SkillIcons.Get(s.Id) != null).ToList();
            foreach (var kind in new[] { GenieCellKind.Attack, GenieCellKind.Recovery, GenieCellKind.Support })
            {
                var cells = setup.Cells(kind);
                var ids = known.Where(s => GenieKindOf(s) == kind).Select(s => s.Id).ToList();
                for (int i = 0; i < ids.Count && i * 2 < cells.Length; i++) cells[i * 2] = ids[i];
            }
            for (int i = 0; i < setup.SupportParty.Length; i += 4) setup.SupportParty[i] = setup.Support[i] != 0;
            setup.Set(GenieMode.Combo, true);
            setup.Set(GenieMoreMode.PetPotion, true);
            setup.Set(GenieOther.FeedFamiliar, true);
            Call(world, "Genie.SyncControls");
            foreach (var name in new[] { "Bulture", "Dark Mare", "Ash Knight Warrior With A Very Long Name", "Treant", "Lich", "Bone Dragon" })
                Field<HashSet<string>>(world, "Genie.Monsters").Add(name);
            Call(world, "Genie.RefreshMonsters");
        }
        Call(world, "Genie.Toggle");
        Call(world, "Genie.OnStatus", view == "empty" ? "..." : "Your genie whispers: keep your potions close and your enemies in range.", view != "claimed");
        if (view == "running")
        {
            SetField(world, "Genie.wasRunning", true); SetField(world, "Genie.wantsRunning", true);
            Native.Set(Game.Session.Genie, "GenieRunning", true);
            Call(world, "Genie.OnSystemState", true, 118);
        }
        else if (view == "pending")
        {
            SetField(world, "Genie.RequestPending", true);
            Field<Button>(world, "Genie.start").Disabled = true;
            Field<Label>(world, "Genie.RunLabel").Text = "Waiting for server response…";
        }
        else Call(world, "Genie.OnSystemState", false, view == "empty" ? 0 : 240);
        return DetachWindow(world, "Genie.Layer", "Genie.Panel");
    }
}
