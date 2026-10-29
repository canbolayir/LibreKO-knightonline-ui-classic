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

    /// <summary>The leader transfer window as a chief sees it: three co-leaders, Velkan preselected.</summary>
    public Control BuildClanHandover()
    {
        ItemData.EnsureLoaded();
        Native.Call(Game.Session.Clan, "SeedPreview", PreviewClan(asChief: true));
        var handover = Feature("ClanHandover");
        Native.Set(handover, "HandoverPreselect", "Velkan");
        byte ok = Native.Get<byte>(handover.GetType(), "HandoverListOk");
        Native.Call(handover, "ShowHandoverWindow", ok, new List<string> { "Aurelia", "Velkan", "Morgrim" });
        return DetachPreviewControl(Native.Get<HudWindow>(handover, "HandoverWindow")!);
    }

    /// <summary>A whisper window with Tester10, or two folded whispers (one with a long name) stacked.</summary>
    public Control BuildWhisper(bool minimized = false)
    {
        ItemData.EnsureLoaded();
        EnsureChat();
        var whispers = Feature("Whispers");
        if (Native.Get<object>(whispers, "whisperLayer") == null) Native.Call(whispers, "Build");
        SeedWhisperTimeline();
        // As a whisper the player opens: shown, unfolded and raised.
        Native.Call(whispers, "OpenWhisperWith", PreviewWhisperPartner);
        Native.TryCall(whispers, "OnDirectTimeline", out _, PreviewWhisperPartner);
        var chat = Native.Call(whispers, "GetOrCreateWhisper", PreviewWhisperPartner, false, null)!;
        var window = Native.Get<HudWindow>(chat, "Window")!;
        // The client stacks unplaced whispers above its chat panel, which a preview World never lays out (it sits at the
        // origin, so the stack hides the window); the preview places the whisper as a player would.
        Native.Call(window.Layout, "Place", new Vector2(360, 120), window.Size);
        window.Visible = true;
        if (!minimized) return DetachPreviewControl(window);
        window.SetMinimized(true);
        var longName = Native.Call(whispers, "GetOrCreateWhisper", "Bartholomew the Third", true, null)!;
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 8);
        stack.AddChild(DetachPreviewControl(window));
        stack.AddChild(DetachPreviewControl(Native.Get<HudWindow>(longName, "Window")!));
        return stack;
    }

    private const string PreviewWhisperPartner = "Rikka";
    private const int PreviewWhisperPartnerId = 9001, PreviewSelfId = 42;

    /// <summary>A direct conversation with Rikka: the connection notice and twelve lines (posts 201-212), both sides.</summary>
    private void SeedWhisperTimeline()
    {
        if (Native.Get<int>(_world, "Avatar.CharId") == 0) Native.Set(_world, "Avatar.CharId", PreviewSelfId);
        int self = Native.Get<int>(_world, "Avatar.CharId");
        var timeline = Game.Session.RoomMedia.DirectTimeline(PreviewWhisperPartner);
        if (timeline.Posts.Count > 0) return;
        // Yesterday's and today's lines, so the thread shows two day markers.
        var start = DateTime.Now.Date.AddDays(-1).AddHours(21);
        timeline.Add(RoomPost.Notice(ItemData.Text(0, "Successfully connected for a private chat"), start) with { Id = 200 });
        string[] lines =
        {
            "hey, are you farming Ronark?", "yep, west side by the bridge. bring pots", "omw (Smile)", "invite me when you get here",
            "which channel are you on?", "channel 2, near the north gate", "ok give me a sec, repairing", "no rush, I'll hold the spot",
            "ready, invite me", "also got something for you", "nice, thanks! how much do you want for it?", "free, you helped last week",
        };
        for (int i = 0; i < lines.Length; i++)
        {
            bool mine = i % 2 == 1;
            var post = RoomPost.Line(mine ? self : PreviewWhisperPartnerId, mine ? "Me" : PreviewWhisperPartner, lines[i], (i < 6 ? start.AddMinutes(i) : DateTime.Now.AddMinutes(i - 20))) with { Id = 201 + i };
            // Three reacted posts: one each way and one with both reactions.
            if (post.Id is 203 or 207) post.Reactions.Set(mine ? PreviewWhisperPartnerId : self, mine ? PreviewWhisperPartner : "Me", "\u2764");
            if (post.Id == 210) { post.Reactions.Set(self, "Me", "\U0001F44D"); post.Reactions.Set(PreviewWhisperPartnerId, PreviewWhisperPartner, "\U0001F44D"); }
            timeline.Add(post);
        }
    }
}