using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

/// <summary>
/// Social window fixtures for the Classic audits: nation chat, clan and join-request boards, leader transfer,
/// commander and command list. They reuse the client's own preview builders where it ships one and build the
/// remaining states through the client's handlers, so every window shows real native content.
/// </summary>
public static partial class PreviewFixtures
{
    private static HudWindow Detached(Control control) => (HudWindow)Native.Call(typeof(WorldRoot), "Preview.DetachPreviewControl", control)!;

    /// <summary>Builds one social window in a named state and returns it detached from the world.</summary>
    public static HudWindow BuildSocialUiPreview(WorldRoot world, string view)
    {
        ItemData.EnsureLoaded();
        switch (view)
        {
            case "nation-public": return (HudWindow)Call(world, "Preview.BuildNationChat", "public")!;
            case "nation-private": return (HudWindow)Call(world, "Preview.BuildNationChat", "private")!;
            case "nation-create": return (HudWindow)Call(world, "Preview.BuildNationChat", "create")!;
            case "nation-password":
                Call(world, "NationChannels.Build");
                Field<LineEdit>(world, "NationChannels.ncPasswordEdit").Text = "1234";
                Field<HudWindow>(world, "NationChannels.ncPassword").Visible = true;
                return Detached(Field<HudWindow>(world, "NationChannels.ncPassword"));
            case "commander": return (HudWindow)Call(world, "Preview.BuildCommander", CommanderOrder.None)!;
            case "clan-board": return (HudWindow)Call(world, "Preview.BuildClanBoards", "clan-board")!;
            case "clan-message": return (HudWindow)Call(world, "Preview.BuildClanBoards", "clan-message")!;
            case "wish-board": return (HudWindow)Call(world, "Preview.BuildClanBoards", "wish-board")!;
            case "wish-message": return (HudWindow)Call(world, "Preview.BuildClanBoards", "wish-message")!;
            case "clan-join-list":
                Call(world, "ClanBoard.Build");
                Call(world, "ClanBoard.OnStatus", new ClanBoardStatus(ClanBoardState.Staff, new List<ClanApplicant>
                {
                    new(72, 112, "Rin", "Priest, online most evenings."),
                    new(68, 107, "Kaito", "Archer looking for a Lunar War clan."),
                    new(80, 205, "LongCharacterName1299", "Warrior with siege experience, ready to join today."),
                }, 0, 0, ""));
                Call(world, "ClanBoard.OpenJoinList");
                return Detached(Field<HudWindow>(world, "ClanBoard.cjWindow"));
            case "wish-requests":
                Call(world, "ClanBoard.Build");
                Call(world, "WishBoard.Build");
                Call(world, "WishBoard.OnWishRequests", new List<WishRequest>
                {
                    new(10, "Alpha", "Wolves", 2, 2, 0, "Join us for the Lunar War tonight."),
                    new(12, "Cassandra", "NightWatch", 1, 2, 0, "Royal clan, castle siege every week."),
                });
                return Detached(Field<HudWindow>(world, "WishBoard.wrWindow"));
            case "clanhandover": return (HudWindow)Call(world, "Preview.BuildClanHandover")!;
            case "command-list": return (HudWindow)Call(world, "Preview.BuildCommandList", 0)!;
        }
        throw new ArgumentException("Unknown social view " + view);
    }
}
