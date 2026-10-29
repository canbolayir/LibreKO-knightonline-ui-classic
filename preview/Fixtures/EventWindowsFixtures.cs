using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

/// <summary>
/// Event windows and states the client's own preview builders do not cover: the Monster Stone prompt, the event
/// schedule before its first reply, after a clock shift and during a lottery draw, and the siege war panel before the
/// castle is held.
/// Each builds the client's own window, feeds it the state a live session shows and detaches it from its layer.
/// </summary>
public static partial class PreviewFixtures
{
    private static HudWindow Detached(HudWindow window)
    {
        window.GetParent()?.RemoveChild(window);
        window.Visible = true;
        return window;
    }

    public static HudWindow BuildNestUiPreview(WorldRoot world, string view)
    {
        ItemData.EnsureLoaded();
        Call(world, "MonsterStone.BuildNestWindow");
        // A Monster Stone from the item table, with its own description as the explanation.
        var stone = ItemData.All().Where(i => i.Name.Contains("Monster Stone") || i.Name.Contains("Monster's Stone")).OrderBy(i => i.Id).FirstOrDefault();
        var window = Field<HudWindow>(world, "MonsterStone.NestWindow");
        SetField(world, "MonsterStone.NestStone", stone?.Id ?? 0);
        window.Title = stone != null ? ItemData.DisplayName(stone.Id) : "Monster Stone";
        Field<Label>(world, "MonsterStone.NestExplain").Text = view == "long"
            ? string.Join(" ", Enumerable.Repeat(stone?.Desc is { Length: > 0 } desc ? desc : "Summons the monsters sealed in the stone into a private dungeon.", 3))
            : stone?.Desc is { Length: > 0 } text ? text : "Summons the monsters sealed in the stone into a private dungeon for you and your party.";
        return Detached(window);
    }

    /// <summary>
    /// The client's daily rank preview with the board list it reads from the installed game content (absent from the
    /// audit packs): the picker lists the boards and the information lines describe the shown board.
    /// </summary>
    public static HudWindow BuildDailyRankBoardsUiPreview(WorldRoot world, string view)
    {
        var window = (HudWindow)Call(world, "Preview.BuildDailyRank", view)!;
        var boards = Field<List<DailyRankBoardInfo>>(world, "DailyRankWindow.DrBoards");
        if (boards.Count == 0)
        {
            boards.Add(new DailyRankBoardInfo(DailyRank.AdonisBoard, "Adonis",
                new[] { "Ranks the characters who defeated the most enemies yesterday.", "The top three receive the Adonis title for one day." }));
            boards.Add(new DailyRankBoardInfo(DailyRank.ClanBoard, "Clan",
                new[] { "Ranks the clans that earned the most clan points yesterday.", "Clan ranks are shared by every member." }));
            var picker = Field<OptionButton>(world, "DailyRankWindow.drPicker");
            foreach (var board in boards) picker.AddItem(board.Title);
            Call(world, "DailyRankWindow.Render", view == "daily-rank-clan" ? DailyRank.ClanBoard : DailyRank.AdonisBoard);
        }
        return window;
    }

    public static HudWindow BuildEventsScheduleUiPreview(WorldRoot world, string view)
    {
        Call(world, "Events.BuildWindow");
        Call(world, "Events.RefreshWindow");
        return Detached(Field<HudWindow>(world, "Events.Window"));
    }

    /// <summary>The client's schedule preview after a game master moved the event clock: the warning line shows.</summary>
    public static HudWindow BuildEventsShiftUiPreview(WorldRoot world, string view)
    {
        var window = (HudWindow)Call(world, "Preview.BuildEvents", "")!;
        var schedule = Field<EventSchedule>(world, "Events.Schedule");
        SetField(world, "Events.Schedule", new EventSchedule(TimeSpan.FromMinutes(-75), schedule.Now, schedule.Events, schedule.ReceivedUtc));
        Call(world, "Events.RefreshWindow");
        return window;
    }

    /// <summary>The client's schedule preview while the lottery draw is live: its card offers Open.</summary>
    public static HudWindow BuildEventsLotteryUiPreview(WorldRoot world, string view)
    {
        var window = (HudWindow)Call(world, "Preview.BuildEvents", "")!;
        var schedule = Field<EventSchedule>(world, "Events.Schedule");
        var lottery = schedule.Events.First(e => e.Kind == EventKind.Lottery);
        var now = schedule.Now.Append(new EventNow(EventKind.Lottery, 0, lottery.Name, EventPhase.Running, 900, lottery.MinLevel, lottery.MaxLevel)).ToList();
        SetField(world, "Events.Schedule", new EventSchedule(schedule.ClockOffset, now, schedule.Events, schedule.ReceivedUtc));
        Call(world, "Events.RefreshWindow");
        return window;
    }

    public static HudWindow BuildSiegeWarStateUiPreview(WorldRoot world, string view)
    {
        Call(world, "Siege.BuildWarWindow");
        bool held = view == "held";
        Call(world, "Siege.ShowStatus", new SiegeWarStatus(held ? (SiegeWarState)2 : SiegeWarState.Unheld, 1, 0, 0, held ? 845 : 0, 2_400,
            held ? "TheVeryLongClanNameOfAll" : ""));
        return Detached(Field<HudWindow>(world, "Siege.WarPanel"));
    }
}
