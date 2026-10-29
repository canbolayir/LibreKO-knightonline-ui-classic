using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Native;

public static partial class PreviewFixtures
{
    public const int EventsPreviewItem = 379154000, EventsPreviewApple = 810418000;

    private static CanvasLayer EventsLayer(WorldRoot world, string layer, string window)
    {
        var node = Field<CanvasLayer>(world, layer);
        node.GetParent()?.RemoveChild(node);
        NativeEventWindows.Prepare(Field<HudWindow>(world, window), world);
        return node;
    }

    private static void EventsPlayer(int nation)
    {
        var me = Game.Session.Self.LastEnter;
        me.Name = "EventTester2026"; me.Nation = nation;
        Native.Call(Game.Session.Self, "SeedPreviewEnter", me);
        Game.Session.Self.Sheet.SeedProgress(62, 0, 1);
    }

    public static CollectionRaceState CollectionRaceEventsState(bool completed, bool crowded)
    {
        var state = new CollectionRaceState
        {
            RaceId = 7,
            Name = crowded ? "Ronark Land Apostles of Flame and Doom Soldier Extermination" : "Moradon Rookie Roundup",
            RemainingSeconds = 56 * 60,
            Winners = completed ? 4 : 3,
            MaxWinners = crowded ? 0 : 16,
            IsCompleted = completed,
            Objectives =
            [
                new CollectionRaceObjective { Kind = CollectionRaceObjectiveKind.Monster, TargetId = 150, Name = "Kecoon", Count = 15, Current = completed ? 15 : 4 },
                new CollectionRaceObjective { Kind = CollectionRaceObjectiveKind.Monster, TargetId = 250, Name = "Bulcan", Count = 10, Current = 10 },
                new CollectionRaceObjective { Kind = CollectionRaceObjectiveKind.Item, TargetId = EventsPreviewApple, Name = "Apples of Moradon", Count = 15, Current = completed ? 15 : 7 },
            ],
            Rewards =
            [
                new CollectionRaceReward { ItemId = QuestData.CoinItemId, ItemCount = 500_000, Rate = 100 },
                new CollectionRaceReward { ItemId = QuestData.ExpItemId, ItemCount = 250_000, Rate = 100 },
                new CollectionRaceReward { ItemId = QuestData.LadderPointItemId, ItemCount = 100, Rate = 100 },
                new CollectionRaceReward { ItemId = EventsPreviewItem, ItemCount = 1, Rate = 35 },
            ],
        };
        if (crowded)
        {
            state.Objectives.Add(new CollectionRaceObjective { Kind = CollectionRaceObjectiveKind.Monster, TargetId = 7001, Name = "Apostle of Flame of the Burning Ronark Highlands", Count = 40, Current = 12 });
            state.Objectives.Add(new CollectionRaceObjective { Kind = CollectionRaceObjectiveKind.Monster, TargetId = 7002, Name = "Doom Soldier", Count = 25, Current = 25 });
            state.Objectives.Add(new CollectionRaceObjective { Kind = CollectionRaceObjectiveKind.EnemyPlayer, Count = 5, Current = 1 });
            for (int i = 0; i < 4; i++)
                state.Rewards.Add(new CollectionRaceReward { ItemId = EventsPreviewItem, ItemCount = 1 + i, Rate = (byte)(10 + i * 5) });
        }
        return state;
    }

    public static CanvasLayer BuildCollectionRaceEventsPreview(WorldRoot world, int nation, bool completed, bool crowded)
    {
        ItemData.EnsureLoaded();
        EventsPlayer(nation);
        Call(world, "CollectionRace.BuildWindow");
        Call(world, "CollectionRace.Show", CollectionRaceEventsState(completed, crowded));
        return EventsLayer(world, "CollectionRace.crLayer", "CollectionRace.CrWindow");
    }

    public static CanvasLayer BuildLotteryEventsPreview(WorldRoot world, int nation)
    {
        ItemData.EnsureLoaded();
        EventsPlayer(nation);
        Call(world, "Lottery.Build");
        return EventsLayer(world, "Lottery.layer", "Lottery.window");
    }

    public static LotteryState LotteryEventsState(bool itemCost, int remaining, int mine)
    {
        var state = new LotteryState
        {
            Active = true, LotteryId = 3, Name = itemCost ? "El Morad Castle Grand Festival Lottery Draw" : "Moradon Weekly Lottery",
            RemainingSeconds = remaining, UserLimit = 5, TotalTickets = 1234, MyTickets = mine,
            ReqItemId = itemCost ? EventsPreviewApple : QuestData.CoinItemId, ReqItemCount = itemCost ? 3 : 100_000,
            ReqItemName = itemCost ? "Apples of Moradon" : "",
        };
        state.Rewards.Add(new LotteryReward { ItemId = EventsPreviewItem, ItemCount = 1 });
        state.Rewards.Add(new LotteryReward { ItemId = EventsPreviewApple, ItemCount = 20 });
        state.Rewards.Add(new LotteryReward { ItemId = QuestData.CoinItemId, ItemCount = 0 });
        state.Rewards.Add(new LotteryReward { ItemId = EventsPreviewApple, ItemCount = 5 });
        return state;
    }

    public static CanvasLayer BuildRouletteEventsPreview(WorldRoot world, int nation)
    {
        ItemData.EnsureLoaded();
        EventsPlayer(nation);
        Call(world, "Roulette.Build");
        return EventsLayer(world, "Roulette.layer", "Roulette.panel");
    }

    public static CanvasLayer BuildTournamentEventsPreview(WorldRoot world, int nation)
    {
        EventsPlayer(nation);
        Call(world, "Tournament.Build");
        return EventsLayer(world, "Tournament.layer", "Tournament.panel");
    }
}
