using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureEventsAAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false))
            throw new Exception("Missing existing item content pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size = new Vector2I(1000, 740);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 900), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("EVENTS_A_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        void Raise(string name, params object?[] args)
        {
            var handler = Native.Get<Delegate>(PreviewFixtures.NetOwner(name), name);
            handler?.DynamicInvoke(args);
        }
        async Task Hover(Control control)
        {
            // Another window's focus or mouse change can drop the hover: move again, up to three times.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var p = control.GetGlobalRect().GetCenter() + new Vector2(attempt, 0);
                GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(2);
                if (control is not BaseButton button || button.Disabled) return;
                ulong until = Time.GetTicksMsec() + 1500;
                while (button.GetDrawMode() != BaseButton.DrawMode.Hover && Time.GetTicksMsec() < until) await Frames(1);
                if (button.GetDrawMode() == BaseButton.DrawMode.Hover) return;
                if (attempt < 2) GD.Print($"EVENTS_A hover attempt {attempt + 2} on {control.Name}");
            }
        }
        async Task Click(Control control)
        {
            var p = control.GetGlobalRect().GetCenter(); var viewport = GetViewport();
            viewport.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            viewport.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            viewport.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            viewport.PushInput(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero }, true); await Frames(1);
        }
        bool InsideScroll(Control control)
        {
            for (var node = control.GetParent(); node != null; node = node.GetParent()) if (node is ScrollContainer) return true;
            return false;
        }
        async Task Capture(HudWindow window, ClassicEventPanel panel, string name, string state)
        {
            await Frames();
            var bounds = window.GetGlobalRect();
            Require((window.Size - panel.Size).Length() < 1, $"{name} {state} frame follows the Classic composition {panel.Size}");
            Require(GetViewportRect().Encloses(bounds), $"{name} {state} window inside viewport");
            foreach (var c in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta(ClassicEventPanel.ExpectedRect)))
            {
                var expected = c.GetMeta(ClassicEventPanel.ExpectedRect).AsRect2();
                Require((c.Position - expected.Position).Length() < .6f && (c.Size - expected.Size).Length() < .6f,
                    $"{name} {state} actual bounds {c.Name} {c.GetRect()} = {expected}");
                if (!InsideScroll(c)) Require(bounds.Grow(.5f).Encloses(c.GetGlobalRect()), $"{name} {state} contained {c.Name}");
                if (c is Label or Button) Require(c.GetThemeFont("font") == Plugin.Kit.Bold, $"{name} {state} bold Classic typography {c.Name}");
            }
            foreach (var scroll in Descendants(panel).OfType<ScrollContainer>().Where(s => s.IsVisibleInTree()))
                Require(bounds.Grow(.5f).Encloses(scroll.GetGlobalRect()) && scroll.ClipContents, $"{name} {state} contained scrolling {scroll.Name}");
            if (panel.CloseButton is { } close && close.IsVisibleInTree())
                Require(bounds.Encloses(close.GetGlobalRect()) && GetViewportRect().Encloses(close.GetGlobalRect()), $"{name} {state} Close reachable");
            string file = $"{prefix}-{name}-{state}.png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { window = name, state, file, bounds = bounds.ToString() });
        }
        async Task<(WorldRoot, CanvasLayer, HudWindow, ClassicEventPanel)> Open(string fixture, string field, params object?[] args)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var layer = (CanvasLayer)DetailCall(world, fixture, args)!;
            AddChild(layer);
            var window = (HudWindow)DetailField(world, field)!;
            await Frames(2);
            var panel = ClassicEventsSkin.Apply(window.Body)!;
            Require(panel != null, fixture + " Classic composition applied to the prepared native window");
            await Frames();
            window.Position = ((GetViewportRect().Size - window.Size) / 2).Floor();
            return (world, layer, window, panel!);
        }
        T Find<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().First(c => c.Name == name);

        // Collection Race -------------------------------------------------------------------------------
        {
            var (world, layer, window, panel) = await Open("BuildCollectionRaceEventsPreview", "CollectionRace.CrWindow", nation, false, false);
            window.Position = new Vector2(620, 60);
            await Capture(window, panel, "collectionrace", "progress");
            var cards = Descendants(panel).OfType<ClassicNpcCard>().ToArray();
            Require(cards.Length == 7, "Collection Race shows every native objective and reward row as a quest card");
            Require(cards.All(c => panel.GetGlobalRect().Encloses(c.GetGlobalRect())), "Collection Race cards stay inside the frame");
            Require(Find<Label>(panel, "cr_winners").Text == "3 / 16" && Find<Control>(panel, "cr_row_winners").Visible, "Winner limit row mirrors the native row");
            DetailCall(world, "CollectionRace.OnCrTimerTick"); await Frames();
            Require(Find<Label>(panel, "cr_time").Text == "55 : 59", "Native timer keeps counting inside the Classic row");
            DetailCall(world, "CollectionRace.OnProgress", 7, new[] { 9, 10, 12 }); await Frames();
            var hunt = Descendants(panel).OfType<ClassicNpcCard>().First(c => c.Name == "monster_card");
            Require(Descendants(hunt).OfType<Label>().Any(l => l.Text == "9 / 15"), "Progress updates rebuild the hunt card from the native row");
            await Capture(window, panel, "collectionrace", "updated");
            DetailCall(world, "CollectionRace.OnCompleted", "Collection Race complete!"); await Frames();
            Require(Find<Label>(panel, "cr_complete_title").IsVisibleInTree(), "Completion banner shown");
            await Capture(window, panel, "collectionrace", "completed");
            var toggle = panel.MinimizeButton!;
            await Click(toggle); await Frames();
            Require(window.Minimized && (panel.Size - ClassicEventsLayout.Minimized).Length() < 1 && !panel.Content.Visible, "Original arrow minimizes through the native handler");
            await Capture(window, panel, "collectionrace", "minimized");
            await Click(toggle); await Frames();
            Require(!window.Minimized && panel.Content.Visible, "Original arrow restores the tracker");
            DetailCall(world, "CollectionRace.Show", PreviewFixtures.CollectionRaceEventsState(false, true)); await Frames(10);
            window.Position = new Vector2(620, 60);
            Require(panel.Size.Y <= ClassicEventsLayout.CollectionRace.MaxHeight && !Find<Control>(panel, "cr_row_winners").Visible, "Long race stays within the maximum frame and hides an unlimited winner row");
            var scroll = Find<ScrollContainer>(panel, "cr_scroll");
            Require(scroll.GetVScrollBar().Visible, "Long race scrolls inside the frame");
            Require(TitleText(panel).TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && TitleText(panel).GetGlobalRect().End.X <= panel.MinimizeButton!.GetGlobalRect().Position.X, $"Long race name is trimmed before the arrow {TitleText(panel).GetGlobalRect()} {panel.MinimizeButton!.GetGlobalRect()} {TitleText(panel).TextOverrunBehavior}");
            await Capture(window, panel, "collectionrace", "long");
            scroll.ScrollVertical = 10000; await Frames();
            await Capture(window, panel, "collectionrace", "long-bottom");
            DetailCall(world, "CollectionRace.OnClose"); await Frames();
            Require(!window.Visible, "Native close packet hides the tracker");
            layer.Free(); world.Free(); await Frames();
        }

        // Lottery ---------------------------------------------------------------------------------------
        {
            var (world, layer, window, panel) = await Open("BuildLotteryEventsPreview", "Lottery.window", nation);
            world.OpenLottery(); await Frames();
            var buy = Find<Button>(panel, "lottery_buy");
            Require(buy.Disabled && Find<Label>(panel, "lottery_time").Text == "Inactive", "Inactive lottery keeps native disabled purchase");
            await Capture(window, panel, "lottery", "inactive");
            Raise("LotteryStateEvent", PreviewFixtures.LotteryEventsState(false, 1800, 1)); await Frames();
            Require(window.Visible && !buy.Disabled && Find<Control>(panel, "lottery_coin").Visible, "Active lottery opens with gold price plaque and Buy enabled");
            var cells = Find<HBoxContainer>(panel, "lottery_rewards").GetChildren().OfType<PanelContainer>().ToArray();
            Require(cells.Length == 4 && cells.All(c => (c.Size - ClassicEventsLayout.Cell).Length() < .6f), "Prize cards use the original 45-pixel inventory cell");
            var prizeArea = Find<ScrollContainer>(panel, "lottery_prizes");
            Require(prizeArea.Size.Y == ClassicEventsLayout.Lottery.PrizeCells && panel.Size.Y == prizeArea.Position.Y + prizeArea.Size.Y + ClassicEventsLayout.Lottery.Bottom,
                $"Lottery frame ends one bottom margin below its prize cells {panel.Size}");
            await Capture(window, panel, "lottery", "active");
            await Hover(buy); Require(buy.GetDrawMode() == BaseButton.DrawMode.Hover, "Buy button receives real hover");
            await Capture(window, panel, "lottery", "buy-hover");
            await Click(buy);
            Require(buy.Disabled && Find<Label>(panel, "lottery_status").Text == "Purchasing ticket..." && NativeEventWindows.Lottery(world).PendingSince != 0,
                "Buy uses the native request and locks until the reply");
            await Capture(window, panel, "lottery", "pending");
            Raise("LotteryJoinEvent", new LotteryJoinResult { Success = true, Message = "You bought a lottery ticket.", MyTickets = 2, TotalTickets = 1235 }); await Frames();
            Require(!buy.Disabled && Find<Label>(panel, "lottery_tickets").Text == "2 / 5" && NativeEventWindows.Lottery(world).PendingSince == 0, "Join reply releases the lock and repaints tickets");
            await Capture(window, panel, "lottery", "joined");
            Raise("LotteryJoinEvent", new LotteryJoinResult { Success = false, Message = "You cannot buy more tickets for this lottery.", MyTickets = 5, TotalTickets = 1240 }); await Frames();
            Require(buy.Disabled, "Ticket limit keeps the native disabled state");
            await Capture(window, panel, "lottery", "limit");
            Raise("LotteryStateEvent", PreviewFixtures.LotteryEventsState(true, 45, 0)); await Frames();
            Require(!Find<Control>(panel, "lottery_coin").Visible, "Item price hides the gold coin");
            await Capture(window, panel, "lottery", "item-cost-final-minute");
            var winners = new List<LotteryWinner>
            {
                new() { Place = 2, CharacterName = "SecondPlaceWinner", ItemId = PreviewFixtures.EventsPreviewApple, ItemCount = 20 },
                new() { Place = 1, CharacterName = "AVeryLongCharacterNameForTheDraw", ItemId = PreviewFixtures.EventsPreviewItem, ItemCount = 1 },
                new() { Place = 3, CharacterName = "Third", ItemId = QuestData.CoinItemId, ItemCount = 1_000_000 },
                new() { Place = 4, CharacterName = "Fourth", ItemId = 123, ItemName = "Mystery Box", ItemCount = 1 },
                new() { Place = 5, CharacterName = "Fifth", ItemId = PreviewFixtures.EventsPreviewApple, ItemCount = 5 },
            };
            Raise("LotteryEndedEvent", "The lottery draw has ended.", winners); await Frames();
            Require(Find<ScrollContainer>(panel, "lottery_winner_list").Visible && Find<Label>(panel, "lottery_prize_caption").Text == "Winners", "Drawn winners replace the prize cells");
            Require(Find<Control>(panel, "lottery_winner_1").GetIndex() == 0, "Winners are listed by place");
            var winnerList = Find<ScrollContainer>(panel, "lottery_winner_list");
            Require(winnerList.Size.Y == ClassicEventsLayout.Lottery.WinnerRows * ClassicEventsLayout.Lottery.WinnerPitch - 2 && winnerList.GetVScrollBar().Visible
                && panel.Size.Y == winnerList.Position.Y + winnerList.Size.Y + ClassicEventsLayout.Lottery.Bottom, $"Winners show four whole rows, scroll the rest and the frame follows {panel.Size}");
            await Capture(window, panel, "lottery", "winners");
            Raise("LotteryStateEvent", PreviewFixtures.LotteryEventsState(false, 3600, 0)); await Frames();
            Require(!Find<ScrollContainer>(panel, "lottery_winner_list").Visible, "A new lottery clears the previous draw");
            window.SetMinimized(true); await Frames();
            Require((panel.Size - ClassicEventsLayout.Minimized).Length() < 1 && panel.CloseButton!.IsVisibleInTree(), "Minimized lottery keeps Close on the title plate");
            await Capture(window, panel, "lottery", "minimized");
            window.SetMinimized(false); await Frames();
            Vector2 origin = window.Position, pointer = origin + new Vector2(60, 20);
            Find<Control>(panel, "events_drag").EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = pointer });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = pointer + new Vector2(-30, 20) });
            window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, GlobalPosition = pointer + new Vector2(-30, 20) });
            Require(window.Position == origin + new Vector2(-30, 20), "Title strip uses native dragging");
            await Click(panel.CloseButton!);
            Require(!window.Visible && !(bool)DetailField(world, "Lottery.shown")!, "Classic Close calls the native CloseLottery");
            layer.Free(); world.Free(); await Frames();
        }

        // Roulette --------------------------------------------------------------------------------------
        {
            var (world, layer, window, panel) = await Open("BuildRouletteEventsPreview", "Roulette.panel", nation);
            DetailCall(world, "Roulette.Toggle"); await Frames();
            window.Position = ((GetViewportRect().Size - window.Size) / 2).Floor();
            var spin = Find<Button>(panel, "roulette_spin");
            var roulette = (ClassicRoulettePanel)panel;
            await Capture(window, panel, "roulette", "empty");
            Raise("RouletteStatusEvent", 3);
            Raise("RoulettePrizeLogEvent", (object)new[] { new RoulettePrize(0, 15_000, 1_760_000_000), new RoulettePrize(PreviewFixtures.EventsPreviewItem, 1, 1_759_990_000), new RoulettePrize(0, 5_000, 1_759_980_000) });
            await Frames();
            Require(Find<Label>(panel, "roulette_coin_value").Text == "3" && !spin.Disabled, "Event coins mirror the native count");
            await Capture(window, panel, "roulette", "ready");
            await Click(spin);
            Require(NativeEventWindows.Roulette(world).Spinning && spin.Disabled, "Spin sends the native request and locks the button");
            await Frames(8); await Capture(window, panel, "roulette", "spinning");
            Require(roulette.Spinning, "Reel keeps turning while the reply is outstanding");
            spin.EmitSignal(BaseButton.SignalName.Pressed);
            Require(NativeEventWindows.Roulette(world).ResultOk == null, "A second press while turning sends nothing");
            Raise("RouletteSpinEvent", true, 0, 27_500); Raise("RouletteStatusEvent", 2);
            for (ulong until = Time.GetTicksMsec() + 6000; roulette.Spinning && Time.GetTicksMsec() < until;) await Frames(1);
            await Frames(2);
            Require(!roulette.Spinning && !NativeEventWindows.Roulette(world).Spinning && !spin.Disabled, "Reel lands after the reply and releases Spin");
            var center = Find<Panel>(panel, "roulette_reel_2");
            Require(center.GetNode<TextureRect>("icon").Texture == ClassicEventPanel.GoldIcon, "Gold prize lands in the centre cell");
            Require(Find<Label>(panel, "roulette_result").Text == "You won 27,500 gold!" || Find<Label>(panel, "roulette_result").Text.StartsWith("You won 27"), "Native result text shown after landing");
            await Capture(window, panel, "roulette", "landed");
            var log = Enumerable.Range(0, 9).Select(i => new RoulettePrize(i % 3 == 1 ? PreviewFixtures.EventsPreviewItem : 0, i % 3 == 1 ? 1 : 5_000 + i * 2_500, 1_760_000_000 - i * 600)).ToArray();
            Raise("RoulettePrizeLogEvent", (object)log); await Frames();
            Require(Find<ScrollContainer>(panel, "roulette_log").GetVScrollBar().Visible, "Long prize history scrolls inside its inset");
            await Capture(window, panel, "roulette", "history-long");
            Raise("RouletteStatusEvent", 0); await Frames();
            Require(spin.Disabled, "No coins disables Spin");
            await Capture(window, panel, "roulette", "no-coins");
            Raise("RouletteStatusEvent", 1); await Frames();
            await Click(spin); Raise("RouletteSpinEvent", false, 0, 0); await Frames();
            Require(!roulette.Spinning && Find<Label>(panel, "roulette_result").Text == "Not enough event coins.", "Refused spin stops the reel with the native message");
            await Capture(window, panel, "roulette", "refused");
            Raise("RouletteStatusEvent", 1); await Frames();
            await Click(spin); Raise("RouletteSpinEvent", true, PreviewFixtures.EventsPreviewItem, 0);
            for (ulong until = Time.GetTicksMsec() + 6000; roulette.Spinning && Time.GetTicksMsec() < until;) await Frames(1);
            await Frames(2);
            Require(center.GetNode<TextureRect>("icon").Texture == ItemIcons.Get(PreviewFixtures.EventsPreviewItem)
                && Find<Label>(panel, "roulette_result").Text == $"You won {ItemData.DisplayName(PreviewFixtures.EventsPreviewItem)}!", "Item prize lands with its icon and item name");
            await Capture(window, panel, "roulette", "item-prize");
            await Hover(panel.CloseButton!); Require(panel.CloseButton!.GetDrawMode() == BaseButton.DrawMode.Hover, "Roulette close receives real hover"); await Capture(window, panel, "roulette", "close-hover");
            await Click(panel.CloseButton!);
            Require(!window.Visible && !(bool)DetailField(world, "Roulette.shown")!, "Close uses the native close handler");
            layer.Free(); world.Free(); await Frames();
        }

        // Tournament ------------------------------------------------------------------------------------
        {
            var (world, layer, window, panel) = await Open("BuildTournamentEventsPreview", "Tournament.panel", nation);
            DetailCall(world, "Tournament.Toggle"); await Frames();
            window.Position = ((GetViewportRect().Size - window.Size) / 2).Floor();
            var register = Find<Button>(panel, "tournament_register"); var unregister = Find<Button>(panel, "tournament_unregister");
            await Capture(window, panel, "tournament", "initial");
            Raise("TournamentStatusEvent", false, 12, 2); await Frames();
            Require(Find<Label>(panel, "tournament_value_0").Text == "2" && Find<Label>(panel, "tournament_value_1").Text == "12" && !register.Disabled && unregister.Disabled,
                "Round, participants and registration buttons follow the native status");
            await Capture(window, panel, "tournament", "open");
            await Hover(register); Require(register.GetDrawMode() == BaseButton.DrawMode.Hover, "Tournament Register receives real hover"); await Capture(window, panel, "tournament", "register-hover");
            await Click(register);
            Require(register.Disabled && unregister.Disabled && NativeEventWindows.Tournament(world).PendingSince != 0, "Register sends the native request and waits for the arena");
            await Capture(window, panel, "tournament", "pending");
            Raise("TournamentRegisterEvent", true); Raise("TournamentStatusEvent", true, 13, 2); await Frames();
            Require(register.Disabled && !unregister.Disabled && Find<Label>(panel, "tournament_value_2").Text == "Registered", "Registered status from the server");
            await Capture(window, panel, "tournament", "registered");
            await Click(panel.CloseButton!);
            Require(!window.Visible && !(bool)DetailField(world, "Tournament.shown")!, "Close uses the native close handler");
            layer.Free(); world.Free(); await Frames();
        }

        System.IO.File.WriteAllText($"{output}/{prefix}-events-a.json", JsonSerializer.Serialize(new { nation, checks, screens, liveRequests = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic events-a audit: " + checks.Count + " checks / " + screens.Count + " screens");
        net.Free();
    }

    private static Label TitleText(ClassicEventPanel panel) => panel.TitleLabel;
}
