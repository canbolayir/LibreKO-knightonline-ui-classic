using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureAuctionAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 740);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 850), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        PluginHost.Ui.ReplaceDialogs(request => new ClassicServiceNotice(request));
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool condition, string text) { if (!condition) throw new Exception("AUCTION_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Click(Control c)
        {
            var p = c.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(2);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
        }
        async Task KeyInput(Key key, uint unicode = 0)
        {
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true }); await Frames(1);
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = false }); await Frames();
        }
        var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildAuctionClassicUiPreview")!; AddChild(layer);
        var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicAuctionSkin.Apply(window.Body)!;
        window.Position = new Vector2(60, 65);
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == "auction_" + name);
        var tracker = (AuctionTransactions)DetailField(world, "_auctionTransactions")!;
        var millions = Find<SpinBox>("millions"); var checkInput = Find<SpinBox>("check_input");
        var bids = (List<AuctionBidRow>)DetailField(world, "_auctionBids")!; var wins = (List<AuctionBidRow>)DetailField(world, "_auctionWins")!;
        var bidList = Find<VBoxContainer>("bid_list"); var winList = Find<VBoxContainer>("win_list");
        var today = (AuctionToday)DetailField(world, "_auctionToday")!;
        void Cooldown() => typeof(World).GetField("_auctionClickedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, double.NegativeInfinity);
        void AttachNotices() { foreach (var n in world.GetChildren().OfType<Notice>().ToArray()) n.Reparent(this, false); }
        Notice[] Notices() => GetChildren().OfType<Notice>().Where(n => !n.IsQueuedForDeletion()).ToArray();
        async Task Dismiss() { AttachNotices(); await Frames(); for (int i = 0; i < 4 && Notices().Length > 0; i++) await KeyInput(Key.Escape); Require(Notices().Length == 0, "Native notice dismisses independently of the auction window"); }
        async Task Capture(string state)
        {
            DetailCall(world, "HideItemTooltip");
            AttachNotices(); await Frames(); Require(window.Size == ClassicAuctionLayout.Size, state + " fixed frame size " + window.Size);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " entire frame fits viewport");
            foreach (var c in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("auction_expected_rect")))
            {
                Require(c.GetRect() == c.GetMeta("auction_expected_rect").AsRect2(), state + " actual declared bounds " + c.Name + " " + c.GetRect());
                Require(window.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " inside outer frame " + c.Name);
            }
            foreach (var c in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("classic_auction_control") && c is Label or Button or LineEdit))
            {
                Require(c.GetThemeFont("font") == Plugin.Kit.Bold, state + " shared bold font " + c.Name);
                if (c is Button b && b.Text.Length > 0) Require(b.GetThemeFont("font").GetStringSize(b.Text, HorizontalAlignment.Left, -1, 13).X + 8 <= b.Size.X, state + " complete button caption fits " + b.Text);
            }
            Require(Descendants(panel).OfType<Button>().Count(b => b.HasMeta("auction_tab_selected") && b.ButtonPressed) == 1, state + " exactly one tab has the selected visual state");
            foreach (var slot in Descendants(panel).OfType<ItemSlotView>().Where(s => s.IsVisibleInTree()))
            {
                Node? parent = slot.GetParent(); while (parent != null && parent is not ScrollContainer) parent = parent.GetParent();
                if (parent is ScrollContainer scroll)
                {
                    Require(scroll.ClipContents && window.GetGlobalRect().Encloses(scroll.GetGlobalRect()), state + " scrolling slot has a bounded clipping viewport");
                    Require(slot.GlobalPosition.X >= scroll.GlobalPosition.X && slot.GetGlobalRect().End.X <= scroll.GetGlobalRect().End.X, state + " scrolling slot fits content width");
                }
                else Require(window.GetGlobalRect().Encloses(slot.GetGlobalRect()), state + " item slot remains inside frame " + slot.Name);
                Require(slot.GetThemeStylebox("panel") is StyleBoxTexture, state + " original slot frame survives native state updates");
                Require(slot.CountLabel.GetGlobalRect().End == slot.GetGlobalRect().End - new Vector2(2, 2), state + " inventory-identical count insets");
                if (!slot.Item.IsEmpty) Require(ItemData.Icon(slot.Item.ItemId) != null, state + " actual populated item artwork " + slot.Item.ItemId);
            }
            foreach (var notice in Notices())
            {
                var dialog = notice.GetChildren().OfType<ClassicServiceNotice>().Single();
                var box = (Control)typeof(ClassicServiceNotice).GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Require(GetViewportRect().Encloses(box.GetGlobalRect()), state + " original notice frame fits viewport");
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }
        try
        {
            await Frames(16); await Capture("ongoing-eight-lots");
            Require(Find<GridContainer>("lots_grid").GetChildCount() == 8 && Find<ItemSlotView>("lot_4").Item.Count == 20, "Eight real slots and stack quantities retained");
            var initial = window.Position; var grip = Descendants(panel).OfType<Control>().Single(c => c.Name == "party_drag"); var p = grip.GetGlobalRect().Position + new Vector2(100, 12);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseMotion { Position = p + new Vector2(11, 7), GlobalPosition = p + new Vector2(11, 7), Relative = new Vector2(11, 7), ButtonMask = MouseButtonMask.Left }, true);
            GetViewport().PushInput(new InputEventMouseButton { Position = p + new Vector2(11, 7), GlobalPosition = p + new Vector2(11, 7), ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Require(window.Position == initial + new Vector2(11, 7), "Actual header drag forwards native HudLayout " + initial + " -> " + window.Position); window.Position = initial;
            await Click(Find<ItemSlotView>("lot_4")); Require((int)DetailField(world, "_auctionSelected")! == 4, "Actual lot click updates native selection"); await Capture("selected-stack");
            GetViewport().PushInput(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero }, true); await Frames();
            p = Find<ItemSlotView>("lot_4").GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames();
            Require(world.ItemTooltipVisible, "Actual native lot hover opens item tooltip"); DetailCall(world, "HideItemTooltip");
            var edit = millions.GetLineEdit(); edit.GrabFocus(); await Frames(); edit.SelectAll(); await KeyInput(Key.Key2, '2'); await KeyInput(Key.Key5, '5'); await KeyInput(Key.Enter);
            Require(millions.Value == 25 && !tracker.Busy && Notices().Length == 0, "Actual numeric typing and Enter commit amount without bidding: value=" + millions.Value + " text=" + edit.Text + " notices=" + Notices().Length); await Capture("typed-amount");
            Require(Find<Label>("total").Text == 1_025_000_000L.ToString("n0"), "Visible bid total updates from actual typed value without a stale native no-signal amount");
            millions.Value = 999; DetailCall(world, "RefreshAuctionBalance"); Require(millions.Value == 845, "Native Noah input clamps to current balance");
            checkInput.Value = 99; DetailCall(world, "RefreshAuctionBalance"); Require(checkInput.Value == 2, "Native check input clamps to real inventory checks"); await Capture("balance-limits");
            await Click(Find<ItemSlotView>("lot_1")); millions.Value = 13; checkInput.Value = 1; Cooldown(); await Click(Find<Button>("place_bid")); AttachNotices(); await Capture("bid-confirmation");
            Require(Notices().Length == 1 && !tracker.Busy, "Native bid confirmation precedes request"); await KeyInput(Key.Escape); Require(window.Visible && !tracker.Busy, "Confirmation Escape keeps auction and selection intact");
            Cooldown(); await Click(Find<Button>("place_bid")); AttachNotices(); await Frames(); var no = Descendants(Notices().Single()).OfType<Button>().Single(b => b.Text == "No"); await Click(no);
            Require(!tracker.Busy && window.Visible, "Actual No returns to unchanged auction");
            Cooldown(); await Click(Find<Button>("place_bid")); AttachNotices(); await Frames(); await KeyInput(Key.Enter);
            Require(tracker.Bid?.Total == 1_013_000_000 && tracker.Bid.Lot.Slot == 1, "Confirmation Enter submits snapshot for selected lot");
            Require(Find<Button>("place_bid").Disabled && Find<Button>("refresh").Disabled && !millions.Editable && !checkInput.Editable, "Pending bid disables economic controls and refresh"); await Capture("bid-pending");
            await Click(Find<ItemSlotView>("lot_4")); Require((int)DetailField(world, "_auctionSelected")! == 1, "Pending bid cannot select another lot");
            millions.Value = 7; checkInput.Value = 0; DetailCall(world, "OnAuctionBid", (short)1); AttachNotices();
            var lots = (IReadOnlyList<AuctionLot>)DetailField(world, "_auctionLots")!;
            Require(lots.Single(l => l.Slot == 1).Current == 1_013_000_000 && lots.Single(l => l.Slot == 4).Current == 1_000_000, "Reply uses submitted amount despite later input mutation and updates only submitted lot"); await Capture("bid-success"); await Dismiss();
            DetailCall(world, "OnAuctionBid", (short)1); Require(Notices().Length == 0 && !tracker.Busy, "Duplicate bid reply is ignored");
            millions.Value = 1; checkInput.Value = 0; Cooldown(); await Click(Find<Button>("place_bid")); AttachNotices(); await Frames(); await KeyInput(Key.Enter); AttachNotices();
            Require(!tracker.Busy && Notices().Length == 1, "Insufficient minimum bid is refused after actual confirmation"); await Capture("insufficient-bid"); await Dismiss();
            millions.Value = 14; checkInput.Value = 1; Cooldown(); await Click(Find<Button>("place_bid")); AttachNotices(); await Frames(); await KeyInput(Key.Enter);
            DetailCall(world, "OnAuctionBid", (short)-3); AttachNotices(); Require(!tracker.Busy, "Server stale-price refusal releases pending request"); await Capture("price-refusal"); await Dismiss();
            millions.Value = 14; checkInput.Value = 1; Cooldown(); await Click(Find<Button>("place_bid")); AttachNotices(); await Frames(); await KeyInput(Key.Enter);
            DetailCall(world, "CloseSpecialAuction"); Require(!window.Visible && tracker.Busy, "Closing retains outstanding request identity");
            DetailCall(world, "OpenSpecialAuction"); AttachNotices(); Require(!window.Visible && tracker.Busy, "Reopen is guarded until old reply arrives"); await Dismiss();
            DetailCall(world, "OnAuctionBid", (short)1); Require(!tracker.Busy && !window.Visible && Notices().Length == 0, "Late reply completes old request without opening window or notice");
            DetailCall(world, "OpenSpecialAuction"); DetailCall(world, "OnAuctionToday", today); await Frames(); Require(window.Visible, "Fresh open works after old request settles");
            await Capture("reopened");
            Cooldown(); await Click(Find<Button>("tab_Schedule")); Require(Find<Control>("page_Schedule").Visible, "Actual Schedule tab opens native schedule"); await Capture("schedule-secret");
            var schedule = Find<VBoxContainer>("schedule"); var scheduleScroll = (ScrollContainer)schedule.GetParent();
            Require(schedule.GetChildCount() == 11 && scheduleScroll.GetVScrollBar().MaxValue > scheduleScroll.GetVScrollBar().Page, "Eleven future-day rows scroll inside fixed content area");
            p = scheduleScroll.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames();
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.WheelDown, Pressed = true }, true); await Frames();
            Require(scheduleScroll.ScrollVertical > 0, "Actual wheel input scrolls native schedule");
            scheduleScroll.ScrollVertical = 1000; await Capture("schedule-scrolled"); scheduleScroll.ScrollVertical = 0;
            var rows = Enumerable.Range(0, 12).Select(i => new AuctionBidRow(1, 2, (byte)i, 1310610106, 1, 9779 + i, 0, 25_000_000 + i * 1_000_000, i % 2 == 0 ? SpecialAuction.Outbid : SpecialAuction.TopBidder)).Concat(Enumerable.Range(0, 4).Select(i => new AuctionBidRow(1, 1, (byte)(20 + i), 1310610106, 1, 9750 + i, 0, 6_000_000, SpecialAuction.Won))).ToArray();
            Cooldown(); await Click(Find<Button>("tab_MyInfo")); DetailCall(world, "OnAuctionMyInfo", (short)1, rows); await Capture("my-auction-status");
            Require(bids.Count == 12 && wins.Count == 4 && Descendants(bidList).OfType<Button>().Count() == 6, "Only eligible outbid rows offer refunds; won rows retain receipt actions");
            var bidScroll = (ScrollContainer)bidList.GetParent(); bidScroll.ScrollVertical = 1000; await Capture("my-status-scrolled"); bidScroll.ScrollVertical = 0; await Frames();
            Cooldown(); await Click(Descendants(bidList).OfType<Button>().First()); Require(tracker.Row?.Row == rows[0] && tracker.Row.Claim == false, "Actual refund button snapshots exact outbid row"); await Capture("refund-pending");
            DetailCall(world, "OnAuctionClaim", (short)1); Require(tracker.Busy && bids.Count == 12, "Wrong receipt reply cannot clear refund request");
            Cooldown(); Descendants(winList).OfType<Button>().First().EmitSignal(BaseButton.SignalName.Pressed); Require(tracker.Row?.Row == rows[0], "Cross-kind receipt action cannot replace pending refund");
            DetailCall(world, "OnAuctionCollect", (short)-1); AttachNotices(); Require(!tracker.Busy && bids.Contains(rows[0]), "Failed refund retains original row and unlocks controls"); await Capture("refund-failed"); await Dismiss();
            Cooldown(); await Click(Descendants(bidList).OfType<Button>().First()); DetailCall(world, "OnAuctionCollect", (short)2); AttachNotices(); Require(bids.Count == 11 && !bids.Contains(rows[0]), "Successful refund removes exact requested row"); await Capture("refund-success"); await Dismiss();
            Cooldown(); var won = wins[0]; await Click(Descendants(winList).OfType<Button>().First()); Require(tracker.Row?.Row == won && tracker.Row.Claim, "Actual receive button snapshots won item"); await Capture("receive-pending");
            DetailCall(world, "OnAuctionCollect", (short)1); Require(tracker.Busy && wins.Contains(won), "Wrong refund reply cannot complete receipt");
            DetailCall(world, "OnAuctionClaim", (short)1); AttachNotices(); Require(wins.Count == 3 && !wins.Contains(won), "Successful receive removes exact won row"); await Capture("receive-success"); await Dismiss();
            DetailCall(world, "OnAuctionClaim", (short)1); Require(Notices().Length == 0, "Duplicate receipt result is ignored");
            var log = Enumerable.Range(0, 14).Select(day => (IReadOnlyList<AuctionResultLine>)Enumerable.Range(0, 4).Select(i => new AuctionResultLine(1310610106, 1_234_000_000, i == 0 ? SpecialAuction.Cancelled : SpecialAuction.Won)).ToArray()).ToArray();
            Cooldown(); await Click(Find<Button>("tab_Log")); DetailCall(world, "OnAuctionLog", (object)log); await Capture("history-long");
            var logScroll = (ScrollContainer)Find<VBoxContainer>("log").GetParent(); Require(logScroll.GetVScrollBar().MaxValue > logScroll.GetVScrollBar().Page, "History scrolls within fixed bounds"); logScroll.ScrollVertical = 1000; await Capture("history-scrolled");
            DetailCall(world, "OnAuctionLog", (object)Array.Empty<IReadOnlyList<AuctionResultLine>>()); await Capture("history-empty");
            DetailCall(world, "OnAuctionMyInfo", (short)1, Array.Empty<AuctionBidRow>()); Require(bidList.GetChildCount() == 0 && winList.GetChildCount() == 0, "Refreshed empty lists remove old controls immediately"); await Capture("my-status-empty");
            DetailCall(world, "OnAuctionToday", today); DetailCall(world, "RefreshAuctionToday"); DetailCall(world, "RefreshAuctionToday"); Require(Find<Label>("status").Visible, "Native five-second refresh cooldown remains visible"); await Capture("refresh-cooldown");
            int seconds = (int)(float)DetailField(world, "_auctionSecondsLeft")!; DetailCall(world, "TickSpecialAuction"); Require(Find<Label>("clock").Text == SpecialAuction.Clock(seconds - 1), "Native countdown decrements while Today is active");
            foreach (short status in new[] { SpecialAuction.NothingToBid, SpecialAuction.CollectOnly, SpecialAuction.Settling })
            {
                DetailCall(world, "OnAuctionToday", today with { Status = status }); AttachNotices(); Require(Find<Button>("place_bid").Disabled, "No live lots means no actionable bid in status " + status); await Capture("auction-state-" + status); await Dismiss();
            }
            DetailCall(world, "OnAuctionToday", today); GetWindow().Size = new Vector2I(1200, 850); window.Position = new Vector2(160, 120); await Capture("wide-viewport");
            millions.GetLineEdit().GrabFocus(); await KeyInput(Key.Escape); Require(!window.Visible && !(bool)DetailField(world, "_specialAuctionShown")!, "Focused numeric Escape closes through native window callback");
        }
        finally { foreach (var notice in Notices()) notice.Free(); layer.Free(); world.Free(); net.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-auction.json", JsonSerializer.Serialize(new { nation, checks, screens, fixture = "Native controls with explicit offline eight-lot and thirteen-day schedule fixtures", liveTransactions = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic auction audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
