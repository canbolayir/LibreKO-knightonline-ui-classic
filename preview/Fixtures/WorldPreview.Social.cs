using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

/// <summary>Builders for the nation chat list, the party commander bar and the clan and wish boards.</summary>
public sealed partial class WorldPreview
{
    private const int PreviewNationRoom = 1002;

    /// <summary>The nation chat list on its public or private tab, or the room creation window.</summary>
    public Control BuildNationChat(string page)
    {
        var nc = Feature("NationChannels");
        Native.Call(nc, "Build");
        Native.Set(nc, "NationShown", true);
        var list = Native.Get<HudWindow>(nc, "NcList")!;
        list.Visible = true;
        var state = Game.Session.NationChat.NationChat;
        state.Left();
        switch (page)
        {
            case "public":
                state.EnteredPublic(2);
                Native.Call(nc, "ShowNationTabQuietly", true);
                Native.Call(nc, "RefreshNationButtons");
                Native.Set(nc, "NationSelectedChannel", 3);
                Native.Call(nc, "OnNationChannels", new List<NationChannel>
                {
                    new(1, "General", 212), new(2, "Trade", 87), new(3, "Party", 41), new(4, "Help", 9),
                });
                return DetachPreviewControl(list);
            case "create":
                Native.Call(nc, "OpenNationCreate");
                Native.Get<LineEdit>(nc, "NcCreateTitle")!.Text = "Siege at nine";
                Native.Get<OptionButton>(nc, "NcCreateOption")!.Selected = 1;
                Native.Call(nc, "ApplyNationCreateOption");
                Native.Get<LineEdit>(nc, "NcCreatePassword")!.Text = "1234";
                Native.Get<OptionButton>(nc, "NcCreateSize")!.Selected = 1;
                return DetachPreviewControl(Native.Get<HudWindow>(nc, "NcCreate")!);
            default:
                state.OwnRoom(PreviewNationRoom, "Siege at nine", true, "1234", 30);
                Native.Call(nc, "ShowNationTabQuietly", false);
                Native.Call(nc, "RefreshNationButtons");
                Native.Set(nc, "NationSelectedRoom", 1004);
                Native.Call(nc, "OnNationRooms", new List<NationRoom>
                {
                    new(1001, "Moradon traders", false, Nations.Karus, 12, 50),
                    new(PreviewNationRoom, "Siege at nine", true, Nations.Karus, 4, 30),
                    new(1003, "Colony hunters", false, Nations.ElMorad, 7, 10),
                    new(1004, "Clan recruits", true, Nations.Karus, 2, 10),
                });
                return DetachPreviewControl(list);
        }
    }

    /// <summary>The party commander bar with its alarm built (the alarm layer stays with the World).</summary>
    public Control BuildCommander(CommanderOrder order)
    {
        SkillData.EnsureLoaded();
        var commander = Feature("PartyCommander");
        Native.Call(commander, "BuildCommanderBar");
        Native.Call(commander, "BuildCommanderAlarm");
        var bar = Native.Get<HudWindow>(commander, "CommanderBar")!;
        bar.Visible = true;
        return DetachPreviewControl(bar);
    }

    /// <summary>The clan board, a clan advert message, the wish board or a wish message.</summary>
    public Control BuildClanBoards(string view)
    {
        ItemData.EnsureLoaded();
        var adverts = new List<ClanAdvert>
        {
            new(10, "Wolves", 2, 7, "Alpha", 0, "We hunt every night in Ronark Land. Level 70+ welcome, voice chat preferred."),
            new(11, "Doves", 3, 4, "Beta", 0, "Relaxed clan for questing and Lunar War."),
            new(12, "NightWatch", 1, 12, "Cassandra", 0, "Royal clan recruiting priests and mages for castle siege."),
        };
        var posts = new List<WishPost>
        {
            new("Rin", "Priest, level 72, online most evenings. Looking for an active clan.", 72, 112, 25, true, 101, "2026-10-09"),
            new("Kaito", "Archer looking for a Lunar War clan.", 68, 107, 31, false, -1, "2026-10-07"),
        };
        if (view is "wish-board" or "wish-message")
        {
            // The wish board opens on the clan board's layer.
            Native.Call(Feature("ClanBoard"), "Build");
            var wish = Feature("WishBoard");
            Native.Call(wish, "Build");
            Native.Call(wish, "OnWishStatus", new WishStatus(WishBoardRole.Recruiter, "Kaito", 0));
            Native.Call(wish, "OnWishPosts", posts);
            if (view == "wish-message")
            {
                Native.Call(wish, "OpenWishView", posts[0]);
                return DetachPreviewControl(Native.Get<HudWindow>(wish, "WmWindow")!);
            }
            Native.Call(wish, "ShowWishBoard");
            return DetachPreviewControl(Native.Get<HudWindow>(wish, "WbWindow")!);
        }
        var board = Feature("ClanBoard");
        Native.Call(board, "Build");
        Native.Call(board, "OnStatus", new ClanBoardStatus(ClanBoardState.Clanless, new List<ClanApplicant>(), 11, 0, "Doves"));
        Native.Call(board, "OnAdverts", adverts);
        if (view == "clan-message")
        {
            var mode = Enum.Parse(Native.ClientType("LibreKO.Features.Windows.ClanBoardFeature+MessageMode")!, "ViewAdvert");
            Native.Call(board, "OpenMessage", mode, adverts[0], "", "", null);
            return DetachPreviewControl(Native.Get<HudWindow>(board, "CmWindow")!);
        }
        return DetachPreviewControl(Native.Get<HudWindow>(board, "CbWindow")!);
    }
}
