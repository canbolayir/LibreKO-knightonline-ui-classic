using Godot;
using LibreKO;
using LibreKO.Network;
using KnightOnlineUiClassic.Native;

/// <summary>
/// Event board fixtures (bounty, sheriff reports, fishing hall, nation force). The client has no preview
/// builders for these windows, so each is built by its own facet init, opened by its own toggle and filled
/// through its own reply handlers; the board preparer then runs as the client's window flow would.
/// </summary>
public static partial class PreviewFixtures
{
    public const string BoardPlayer = "Rivermaster";

    public static HudWindow BuildBoardWindowPreview(WorldRoot world, string id, bool sheriff)
    {
        var me = Game.Session.Self.LastEnter; me.Name = BoardPlayer; Seed(world, me);
        var (init, panel, toggle) = id switch
        {
            "bounty" => ("BountyInit", "Bounty.panel", "ToggleBounty"),
            "report" => ("ReportInit", "Report.panel", "ToggleReport"),
            "fishinghall" => ("FishingHallInit", "FishingHall.fishHallPanel", "ToggleFishingHall"),
            _ => ("ForcesInit", "Forces.panel", "ToggleForces"),
        };
        SetField(world, "PlayerSystem.IsGm", sheriff);
        Call(world, init);
        var window = Detach<HudWindow>(world, panel);
        Call(world, toggle);
        NativeBoards.Prepare(window, world);
        return window;
    }

    private static readonly string[] BoardNames =
    {
        "Talon", "Morrigan", "Brightblade", "Kessandra", "Orrin", "Valeska", "Dunmore", "Isolde", "Thorne", "Ravenna",
        "Grimwald", "Elsbeth", "Corvin", "Maelis", "Halvard", "Seraphine", "Brannoc", "Ysolde", "Kaedric", "Liora",
        "Fenwick", "Odalys", "Garrick", "Nerys", "Ulric", "Thessaly", "Bastion", "Wrenna", "Caspian", "Eluned",
    };

    /// <summary>Open bounties; <paramref name="longNames"/> adds a target and poster wider than their columns.</summary>
    public static void FillBounties(WorldRoot world, int count, bool longNames)
    {
        var list = Enumerable.Range(0, count).Select(i => new BountyEntry
        {
            Id = 100 + i,
            Target = longNames && i == 1 ? "Xx_UnstoppableShadowReaper_xX" : BoardNames[i % BoardNames.Length],
            Poster = longNames && i == 2 ? "TheExtremelyPatientAvenger" : BoardNames[(i + 7) % BoardNames.Length],
            Reward = i == 0 ? 25_000_000 : 50_000 * (i + 1),
        }).ToList();
        Call(world, "Bounty.OnList", list);
    }

    /// <summary>Hall of fame ranks; the local player holds rank 5.</summary>
    public static void FillFishingHall(WorldRoot world, int count)
    {
        var list = Enumerable.Range(0, count).Select(i => new FishingHallEntry
        {
            Rank = i + 1,
            Name = i == 4 ? BoardPlayer : i == 2 ? "GrandmasterOfTheSilverLake" : BoardNames[i % BoardNames.Length],
            Score = 1_250_000 - i * 37_500,
        }).ToList();
        Call(world, "FishingHall.OnList", list);
    }

    private const string LongReason =
        "Repeatedly killed low-level players at the Moradon gate and camped the respawn point for over an hour, " +
        "then insulted the victims in shout chat. Several witnesses from both nations confirmed it and screenshots " +
        "were collected by the clan leaders who were present at the time.";

    /// <summary>One server page of open reports.</summary>
    public static void FillReports(WorldRoot world, int totalPages, int count, bool longReason)
    {
        var list = Enumerable.Range(0, count).Select(i => new ReportEntry(500 + i,
            i == 3 ? "MuchTooLongCharacterNameHere" : BoardNames[(i * 3) % BoardNames.Length], (byte)(i % 3), (byte)(i % 2),
            longReason && i == 1 ? LongReason : "Botting in Ronark Land.")).ToList();
        Call(world, "Report.OnList", (byte)totalPages, list);
    }

    public static void FillForces(WorldRoot world, bool joined, int rank, int points, int anger)
    {
        Call(world, "Forces.OnStatus", new ForcesStatus { Joined = joined, Rank = (byte)rank, Points = points, AngerPct = (byte)anger });
    }
}
