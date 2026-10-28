using Godot;
using LibreKO;
using LibreKO.Domain;

/// <summary>
/// Builders for the event overlays and the Events window: each builds the client's own control through the
/// feature that owns it on the prepared World, puts it in a known state and hands it to the preview detached.
/// </summary>
public sealed partial class WorldPreview
{
    private const int PreviewMonsterStone = 900144023;
    private const int PreviewNestTimeLeft = 1786, PreviewNestGraceLeft = 11;
    private const int PreviewEventLeadMinutes = 15, PreviewEventLaterMinutes = 95;
    private const int PreviewSignUpSeconds = 412, PreviewRaceSeconds = 1830;

    /// <summary>The war score strip of a soccer match: 7:34 left, blue 2, red 1.</summary>
    public Control BuildSoccer()
    {
        ItemData.EnsureLoaded();
        var soccer = Feature("SoccerSystem");
        Native.Call(soccer, "AttachPlates", Feature("EventPlates"));
        var layer = new CanvasLayer();
        _world.AddChild(layer);
        Native.Set(soccer, "SoccerLayer", layer);
        var board = (Control)Native.Call(soccer, "EnsureSoccerBoard")!;
        Native.Call(soccer, "OnSoccerSync", 454, 2, 1);
        return DetachPreviewControl(board);
    }

    /// <summary>The Chaos nest toast: the monster stone and its time, or its grace time once completed.</summary>
    public Control BuildNestTimer(bool completed)
    {
        ItemData.EnsureLoaded();
        var chaos = Feature("Chaos");
        Native.Call(chaos, "ShowNestTimer", PreviewMonsterStone, completed ? PreviewNestGraceLeft : PreviewNestTimeLeft, completed);
        return DetachPreviewControl(Native.Get<Control>(chaos, "NestTimerPanel")!);
    }

    /// <summary>The Nereid strip and its two boards, stacked, with sample scores.</summary>
    public Control BuildNereid()
    {
        var nereid = Feature("Nereid");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        var strip = UiTheme.Text("", 12, UiTheme.TextHi, HorizontalAlignment.Center);
        strip.AddThemeStyleboxOverride("normal", UiTheme.MeterTrack());
        strip.CustomMinimumSize = new Vector2(MiniMap.MapSize, 18);
        var flags = (Label)Native.Call(nereid, "Board", Control.LayoutPreset.TopLeft, 0f)!;
        var score = (Label)Native.Call(nereid, "Board", Control.LayoutPreset.TopLeft, 0f)!;
        foreach (var label in new[] { flags, score })
        {
            label.GetParent()?.RemoveChild(label);
            label.CustomMinimumSize = new Vector2(Native.Get<float>(Native.StaticOwner("LibreKO.Features.Hud.NereidFeature"), "BoardWidth"), 18);
        }
        Native.Set(nereid, "Strip", strip); Native.Set(nereid, "FlagBoard", flags); Native.Set(nereid, "ScoreBoard", score);
        strip.Text = "El Morad  6 : 8  Karus";
        flags.Text = "Total Point   Elmo: 6   Karu: 8";
        score.Text = "TotalPoint   Elmo: 12   Karu: 26";
        flags.Visible = score.Visible = strip.Visible = true;
        box.AddChild(strip); box.AddChild(flags); box.AddChild(score);
        return box;
    }

    /// <summary>The order message: the own red flash (Chaos stone broken) or the yellow hold (monument lost).</summary>
    public CanvasLayer BuildOrderMessage(bool own, float viewWidth)
    {
        ItemData.EnsureLoaded();
        var notices = Feature("ChatNotices");
        var owner = Native.StaticOwner("LibreKO.Features.Windows.ChatNoticesFeature");
        string text = own
            ? "Zeus" + ItemData.Text(Native.Get<int>(owner, "TextChaosStoneBroken"), "You have destroyed the [Summoning Stone of Chaos] and opened the Gates of Chaos.")
            : ItemData.Text(Native.Get<int>(owner, "TextMonumentLost"), "Enemy nation has captured center Monument");
        var colour = own ? Native.Get<Color>(owner, "OrderMessageRed") : LibreKO.UiKit.OrderMessageYellow;
        Native.Call(notices, "PlaceOrderMessage", text, colour, own ? OrderMessage.Flash : OrderMessage.Hold, viewWidth);
        var layer = Native.Get<CanvasLayer>(notices, "OrderMessageLayer")!;
        layer.GetParent()?.RemoveChild(layer);
        return layer;
    }

    /// <summary>The siege tower boarding button and the drop-off action, stacked.</summary>
    public Control BuildSiegeTower()
    {
        ItemData.EnsureLoaded();
        var tower = Feature("SiegeTowerSystem");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 24);
        Native.Call(tower, "BuildTowerButton", box);
        var button = Native.Get<Button>(tower, "TowerButton")!;
        
        button.Text = SiegeText(SiegeTower.BoardingText);
        button.Visible = true;
        var dropOff = UiTheme.ActionButton(SiegeText(SiegeTower.ExitText), "");
        dropOff.CustomMinimumSize = new Vector2(Native.Get<float>(tower.GetType(), "DropOffWidth"), Native.Get<float>(tower.GetType(), "DropOffHeight"));
        dropOff.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        Native.Set(tower, "DropOff", dropOff);
        box.AddChild(dropOff);
        return box;
    }

    private static string SiegeText(int id) => (string)Native.Call(Native.StaticOwner("LibreKO.Features.Core.SiegeTowerSystem"), "TowerText", id)!;

    /// <summary>The ranker statue's paper with a Karus first-place message.</summary>
    public Control BuildRankerPaper()
    {
        ItemData.EnsureLoaded();
        var ranker = Feature("RankerMessageWindow");
        Native.Set(ranker, "Type", (byte)RankerStatue.KarusFirstType);
        Native.Set(ranker, "Name", "Veteran");
        Native.Set(ranker, "Text", "Glory to Karus. Meet me at the Ronark Land bridge every evening; the strong are always welcome in our ranks.");
        Native.Call(ranker, "ShowPaper");
        return DetachPreviewControl(Native.Get<Control>(ranker, "Paper")!);
    }

    /// <summary>The Events window with a live sign-up, a running race and three scheduled events.</summary>
    public Control BuildEvents(string variant = "")
    {
        Game.Session.Self.Sheet.SeedProgress(62, 0, 1_000_000);
        var events = Feature("Events");
        Native.Call(events, "BuildWindow");
        var utc = DateTime.UtcNow;
        var soon = utc.AddMinutes(PreviewEventLeadMinutes);
        var later = utc.AddMinutes(PreviewEventLaterMinutes);
        static EventSlot Daily(DateTime at) => new(EventSchedule.EveryDay, at.Hour, at.Minute);
        var schedule = new EventSchedule(TimeSpan.Zero,
            new List<EventNow>
            {
                new(EventKind.Temple, 0, "Juraid Mountain", EventPhase.SignUp, PreviewSignUpSeconds, 40, 83),
                new(EventKind.CollectionRace, 0, "Moradon Hunt", EventPhase.Running, PreviewRaceSeconds, 1, 83),
            },
            new List<ScheduledEvent>
            {
                new(EventKind.Temple, "Forgotten Temple", "", 60, 83, [Daily(soon)]),
                new(EventKind.Temple, "Under The Castle", "", 70, 83, [Daily(later)]),
                new(EventKind.Lottery, "Daily Lottery", "", 0, 0, [Daily(later)]),
            },
            utc);
        Native.Set(events, "Schedule", schedule);
        Native.Call(events, "RefreshWindow");
        var window = Native.Get<HudWindow>(events, "Window")!;
        window.Visible = true;
        return DetachPreviewControl(window);
    }

    /// <summary>The event title chips under the clock: <paramref name="count"/> titles, folded or expanded.</summary>
    public Control BuildEventTitles(int count, bool expanded)
    {
        var samples = new[]
        {
            ("Exp Event", "EXP Increase Event is under way! Earn 50 percent extra EXP from monster hunting."),
            ("Clan Notice", "Siege practice tonight at nine, meet at the Moradon gate."),
            ("Drop Event", "Drop Increase Event is under way! Earn 30 percent extra Drop from monster hunting."),
            ("NP Event", "Bonus National point event in effect. You'll receive additional 20 percent of NPs for kill."),
        };
        var titles = Game.Session.Zone.EventTitles;
        titles.Clear();
        for (int i = 0; i < count && i < samples.Length; i++) titles.Show(samples[i].Item1, samples[i].Item2);
        if (titles.Expanded != expanded) Native.TryCall(titles, "ToggleExpanded", out _);
        var feature = Feature("EventTitlesWindow");
        Native.Call(feature, "BuildTitleColumn");
        Native.Call(feature, "Refresh");
        return DetachPreviewControl(Native.Get<Control>(feature, "TitleColumn")!);
    }
}
