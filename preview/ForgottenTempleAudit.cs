using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using System.Reflection;
using System.Text.Json;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using Spec = KnightOnlineUiClassic.Layout.ClassicOverlayLayout;

public partial class Preview
{
    private async Task CaptureForgottenTempleAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0)
            output = ProjectSettings.GlobalizePath("res://../../research/forgotten-temple-integration/events");
        System.IO.Directory.CreateDirectory(output);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size = new Vector2I(1280, 800);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1280, 800), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net();
        typeof(Net).GetProperty("I")!.SetValue(null, net);
        var world = PreviewFixtures.Prepared(new WorldRoot());
        DetailCall(world, "Bifrost.BuildUi");
        DetailCall(world, "BuildInZoneLeaveUi");
        foreach (var layer in world.GetChildren().OfType<CanvasLayer>().ToArray()) layer.Reparent(this);
        PluginHost.Ui.ReplaceDialogs(request => new KnightOnlineUiClassic.Windows.MessageBox(request));
        NativeOverlays.Prepare(world);
        var checks = new List<string>();
        var screenshots = new List<object>();
        void Require(bool valid, string message)
        {
            if (!valid) throw new Exception(message);
            checks.Add(message);
        }
        async Task Frames()
        {
            for (int i = 0; i < 10; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        async Task Capture(string state)
        {
            await Frames();
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            var bounds = Descendants(this).OfType<Control>().Where(c => c.IsVisibleInTree())
                .Select(c => new { name = c.Name.ToString(), type = c.GetType().Name, x = c.GlobalPosition.X, y = c.GlobalPosition.Y, w = c.Size.X, h = c.Size.Y }).ToArray();
            screenshots.Add(new { file, state, bounds });
        }
        var viewport = GetViewportRect();
        bool Whole(Rect2 r) => r.Position == r.Position.Round() && r.Size == r.Size.Round();
        bool Fits(Label label)
        {
            var font = label.GetThemeFont("font"); int size = label.GetThemeFontSize("font_size");
            if (label.AutowrapMode == TextServer.AutowrapMode.Off)
                return font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, size).X <= label.Size.X + 0.5f;
            int lines = label.GetLineCount();
            return lines * font.GetHeight(size) + (lines - 1) * label.GetThemeConstant("line_spacing") <= label.Size.Y + 0.5f;
        }
        void Expected(Control control, Control parent, string what)
        {
            var expected = control.GetMeta(ClassicWorldOverlays.ExpectedRect).AsRect2();
            var actual = new Rect2(control.GlobalPosition - parent.GlobalPosition, control.Size);
            Require(actual == expected && Whole(control.GetGlobalRect()), $"{what}: rendered bounds {actual} match the declared {expected}");
        }
        void Art(Button button, string text, string what)
        {
            var caption = button.GetNodeOrNull<Label>("caption");
            Require(caption != null && caption.Text == text && button.Text == text && Fits(caption), $"{what}: the native '{text}' on the art caption");
            Require(button.GetThemeStylebox("normal") is StyleBoxTexture && button.GetThemeStylebox("hover") is StyleBoxTexture
                && button.GetThemeStylebox("pressed") is StyleBoxTexture, $"{what}: original button art in every state");
            Require(button.GetThemeColor("font_color").A == 0 && button.GetThemeColor("font_hover_color").A == 0, $"{what}: only the art caption draws '{text}'");
        }
        var zone = typeof(WorldRoot).GetProperty("ZoneId", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void SetZone(int value) => zone.SetValue(world, Convert.ChangeType(value, zone.PropertyType));
        var banner = (Control)DetailField(world, "Bifrost.inZoneLeaveBanner")!;
        var title = (Label)DetailField(world, "Bifrost.inZoneLeaveTitleLbl")!;
        DetailCall(world, "Bifrost.OnTime", 600, TempleEventType.ForgottenTemple);
        var join = (Control)DetailField(world, "Bifrost.joinModal")!;
        var joinTitle = (Label)DetailField(world, "Bifrost.joinModalTitle")!;
        Require(join.Visible && joinTitle.Text.Contains("FORGOTTEN TEMPLE"), "FT registration uses its event title and existing join UI");
        var countdown = (PanelContainer)DetailField(world, "Bifrost.banner")!;
        var joinButton = (Button)DetailField(world, "Bifrost.joinModalBtn")!;
        var closeButton = (Button)DetailField(world, "Bifrost.joinModalCloseBtn")!;
        var timerLabel = (Label)DetailField(world, "Bifrost.joinModalTimer")!;
        var statusLabel = (Label)DetailField(world, "Bifrost.joinModalStatus")!;
        var bar = (ProgressBar)DetailField(world, "Bifrost.joinModalBar")!;
        var countdownTimer = (Label)DetailField(world, "Bifrost.timerLbl")!;
        var countdownBar = (ProgressBar)DetailField(world, "Bifrost.bar")!;
        var signUps = Game.Session.Bifrost.SignUps;
        string other = "";
        // The registration box in one state: the shown sign-up's countdown, its status and the actions it offers.
        // Registered elsewhere: the character is signed up for another open event, so Join is hidden and Close is alone.
        async Task Registration(string state, bool urgent, bool registered, bool elsewhere = false, bool count = false)
        {
            await Frames();
            countdown.Position = new Vector2(Mathf.Round((viewport.Size.X - countdown.Size.X) / 2), 24);
            await Frames();
            var panel = join.GetNode<Control>("classic_registration");
            var countLabel = (Label)DetailField(world, "Bifrost.joinModalCount")!;
            Require(join.Visible && !join.GetChildren().OfType<CenterContainer>().Single().Visible, $"{state}: the flat native panel is hidden");
            Require(panel.Size == Spec.Registration.SizeFor(count) && panel.GlobalPosition == ((viewport.Size - panel.Size) / 2).Round(), $"{state}: centred message box {panel.GetGlobalRect()}");
            Require(countLabel.Visible == count, $"{state}: the sign-up count line {(count ? "shows" : "is hidden")} for this event");
            foreach (var label in count ? new[] { joinTitle, timerLabel, statusLabel, countLabel } : new[] { joinTitle, timerLabel, statusLabel })
            { Expected(label, panel, $"{state} '{label.Text}'"); Require(Fits(label), $"{state}: '{label.Text}' fits"); }
            Expected(bar, panel, state + " bar"); Expected(closeButton, panel, state + " close");
            Require(closeButton.Size == Spec.Message.Button, $"{state}: 97 x 29 message buttons");
            Art(closeButton, "Close", state + " close");
            bool alone = elsewhere || !joinButton.Visible;
            if (elsewhere)
                Require(!joinButton.Visible && statusLabel.Text == $"You are registered for {other}.", $"{state}: '{statusLabel.Text}' without Join");
            if (!alone)
            {
                Expected(joinButton, panel, state + " join");
                Require(joinButton.Size == Spec.Message.Button, $"{state}: 97 x 29 Join");
                Art(joinButton, registered ? "Cancel" : "Join", state + " join");
            }
            else Require(new Rect2(closeButton.GlobalPosition - panel.GlobalPosition, closeButton.Size) == Spec.Message.Single(panel.Size.Y), $"{state}: Close takes the single centred place");
            if (count) Require(countLabel.GetGlobalRect().End.Y + Spec.Message.ActionGap <= closeButton.GetGlobalRect().Position.Y + .5f, $"{state}: the count line keeps the gap above the buttons");
            Require(timerLabel.GetThemeColor("font_color") == (urgent ? ClassicWorldOverlays.UrgentOnMessage : ClassicWorldOverlays.MessageText), $"{state}: {(urgent ? "urgent" : "calm")} countdown ink");
            Require(((StyleBoxTexture)bar.GetThemeStylebox("fill")).RegionRect.Position.Y == (urgent ? 462 : 389), $"{state}: {(urgent ? "red" : "orange")} original bar fill");
            var statusInk = registered ? ClassicEventPanel.Accepted : elsewhere ? ClassicDesign.Muted : ClassicDesign.Text;
            Require(statusLabel.GetThemeColor("font_color") == statusInk, $"{state}: {(registered ? "registered" : elsewhere ? "registered elsewhere" : "open")} status ink");
            Plaque(state, urgent);
            await Capture(state);
        }
        void Plaque(string state, bool urgent)
        {
            Require(countdown.Visible && countdown.Size == Spec.Countdown.Size && viewport.Encloses(countdown.GetGlobalRect()), $"{state}: countdown plaque {countdown.Size}");
            var surface = countdown.GetNode<Control>("classic_countdown");
            foreach (var control in new Control[] { (Label)DetailField(world, "Bifrost.titleLbl")!, countdownTimer, countdownBar })
            {
                Expected(control, surface, $"{state} countdown {control.Name}");
                if (control is Label label) Require(Fits(label), $"{state}: countdown '{label.Text}' fits");
            }
            Require(countdownTimer.GetThemeColor("font_color") == (urgent ? ClassicWorldOverlays.UrgentOnPlaque : ClassicWorldOverlays.PlaqueInk), $"{state}: {(urgent ? "urgent" : "calm")} countdown plaque ink");
            Require(((StyleBoxTexture)countdownBar.GetThemeStylebox("fill")).RegionRect.Position.Y == (urgent ? 462 : 389), $"{state}: {(urgent ? "red" : "orange")} plaque bar fill");
        }
        await Registration("ft-registration", false, false);
        // Join asks the server; the sign-up is registered when the server's reply names the temple's zone.
        joinButton.EmitSignal(BaseButton.SignalName.Pressed);
        Require(DetailField(world, "Bifrost.pendingJoin") is TempleEventType.ForgottenTemple && signUps.Registered == TempleEventType.None && joinButton.Text == "Join",
            "Join sends the native request and waits for the server");
        DetailCall(world, "Bifrost.OnJoinResult", (byte)TempleJoinResult.Joined, 55);
        Require(signUps.Registered == TempleEventType.ForgottenTemple && joinButton.Text == "Cancel", "The server's reply for zone 55 registers Forgotten Temple");
        await Registration("ft-registered", false, true);
        DetailCall(world, "Bifrost.OnTime", 20, TempleEventType.ForgottenTemple);
        await Registration("ft-registration-urgent", true, true);
        DetailCall(world, "Bifrost.OnTime", 600, TempleEventType.ForgottenTemple);
        // The server's sign-up count adds its line under the status; the box grows by that line only.
        DetailCall(world, "Bifrost.OnTempleSignUps", (int)TempleEventType.ForgottenTemple, 14, 9);
        await Registration("ft-registration-count", false, true, count: true);
        Require(((Label)DetailField(world, "Bifrost.joinModalCount")!).Text == TempleJoin.CountLine(false, 14, 9), "registration: the Forgotten Temple count per nation");
        // A second sign-up opens while registered for Forgotten Temple: the box stays on Forgotten Temple until the
        // player picks the other event (the events window's Join), which then reads "registered for" without Join.
        DetailCall(world, "Bifrost.OnTime", 600, TempleEventType.Chaos);
        Require(signUps.Open.Count == 2 && signUps.Current == TempleEventType.ForgottenTemple && joinTitle.Text.Contains("FORGOTTEN TEMPLE"),
            "two sign-ups: the registration stays on the registered event");
        DetailCall(world, "Bifrost.ShowSignUp", TempleEventType.Chaos);
        other = "Forgotten Temple";
        Require(signUps.Current == TempleEventType.Chaos && joinTitle.Text.Contains("CHAOS DUNGEON"), "two sign-ups: the other event is shown on request");
        await Registration("registered-elsewhere", false, false, elsewhere: true);
        // Counts are kept per event: the Chaos sign-up (event id 24) has its own line.
        DetailCall(world, "Bifrost.OnTempleSignUps", TempleJoin.ChaosSignUpEventId, 11, 7);
        await Registration("registered-elsewhere-count", false, false, elsewhere: true, count: true);
        Require(((Label)DetailField(world, "Bifrost.joinModalCount")!).Text == TempleJoin.CountLine(true, 11, 7), "registration: the Chaos sign-up count (event id 24)");
        DetailCall(world, "Bifrost.ShowSignUp", TempleEventType.ForgottenTemple);
        await Frames();
        Require(joinButton.Visible && joinButton.Text == "Cancel" && ((Label)DetailField(world, "Bifrost.joinModalCount")!).Text == TempleJoin.CountLine(false, 14, 9),
            "two sign-ups: back on Forgotten Temple with Cancel and its own count");
        // Cancelling the registration, then Juraid Mountain, which signs up at an NPC: no Join, Close alone.
        DetailCall(world, "Bifrost.OnDisband");
        Require(signUps.Registered == TempleEventType.None, "the server's disband clears the registration");
        DetailCall(world, "Bifrost.OnTime", 600, TempleEventType.JuraidMountain);
        Require(signUps.Open.Count == 3 && signUps.Current == TempleEventType.JuraidMountain && !joinButton.Visible && closeButton.Visible, "juraid: the client hides Join for the NPC sign-up");
        await Registration("juraid-registration", false, false);
        Require(statusLabel.Text.Contains("Reserve Knight Captain") && joinTitle.Text == "#  JURAID MOUNTAIN  #", "juraid: the NPC sign-up hint under the English capitals " + joinTitle.Text);
        DetailCall(world, "Bifrost.ShowSignUp", TempleEventType.ForgottenTemple);
        closeButton.EmitSignal(BaseButton.SignalName.Pressed);
        Require(!join.Visible && countdown.Visible, "Close runs the native close and keeps the countdown plaque");
        await Frames();
        Plaque("ft-countdown", false);
        await Capture("ft-countdown");
        // The server ends each sign-up with a zero time; without one the plaque counts the plain Bifrost time.
        foreach (var type in new[] { TempleEventType.ForgottenTemple, TempleEventType.Chaos, TempleEventType.JuraidMountain })
            DetailCall(world, "Bifrost.OnTime", 0, type);
        await Frames();
        Require(!signUps.AnyOpen && !countdown.Visible && !join.Visible, "zero times close every sign-up and the plaque");
        DetailCall(world, "Bifrost.OnTime", 20, TempleEventType.None);
        await Frames();
        Require(((Label)DetailField(world, "Bifrost.titleLbl")!).Text == "Bifrost" && !join.Visible, "the plain Bifrost countdown has no registration");
        countdown.Position = new Vector2(Mathf.Round((viewport.Size.X - countdown.Size.X) / 2), 24);
        await Frames();
        Plaque("bifrost-countdown-urgent", true);
        await Capture("bifrost-countdown-urgent");
        DetailCall(world, "Bifrost.OnTime", 0, TempleEventType.None);
        await Frames();
        Require(!countdown.Visible, "a zero Bifrost time hides the plaque");
        SetZone(55);
        DetailCall(world, "RefreshInZoneLeaveUi");
        await Frames();
        banner.Position = new Vector2(480, 24);
        Require(banner.Visible && title.Text == "Forgotten Temple", "FT leave banner appears after zone readiness refresh");
        Require(GetViewportRect().Encloses(banner.GetGlobalRect()), "FT leave banner fits the viewport");
        var leaveButton = (Button)DetailField(world, "Bifrost.inZoneLeaveBtn")!;
        var leaveSurface = banner.GetNode<Control>("classic_leave");
        Require(banner.Size == Spec.Leave.Size && banner.GetThemeStylebox("panel") is StyleBoxEmpty, $"FT leave plaque {banner.Size} replaces the flat panel");
        Expected(title, leaveSurface, "leave title"); Expected(leaveButton, leaveSurface, "leave action");
        Require(leaveButton.Size == Spec.Plaque.Action, "Leave uses the 55 x 15 chat tab art");
        Art(leaveButton, "Leave", "leave action");
        Require(title.GetThemeColor("font_color") == ClassicWorldOverlays.PlaqueInk && title.GetThemeFont("font") == Plugin.Kit.Bold, "Leave plaque caption uses the plaque ink and Arial Bold");
        await Capture("ft-leave-banner");
        foreach (int eventZone in new[] { 55, 84, 85, 86, 87 })
        {
            SetZone(eventZone);
            DetailCall(world, "RefreshInZoneLeaveUi");
            Require(banner.Visible, $"Zone {eventZone} retains an event leave banner");
            await Frames();
            Require(Fits(title) && banner.Size == Spec.Leave.Size, $"Zone {eventZone} name '{title.Text}' fits the leave plaque");
            DetailCall(world, "Bifrost.OnInZoneLeavePressed");
            var notice = NativeEvents.LeavePrompt(world)!;
            notice.Reparent(this);
            var dialog = Descendants(notice).OfType<Control>().First(c => c.GetType().FullName == "KnightOnlineUiClassic.Windows.MessageBox");
            Require(dialog != null, $"Zone {eventZone} uses the Classic confirmation");
            DetailCall(world, "Bifrost.OnInZoneLeavePressed");
            Require(ReferenceEquals(notice, NativeEvents.LeavePrompt(world)), $"Zone {eventZone} prevents duplicate confirmations");
            if (eventZone == 55) await Capture("ft-leave-confirmation");
            dialog!._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            await Frames();
            Require(NativeEvents.LeavePrompt(world) == null && banner.Visible, $"Zone {eventZone} Escape cancels without hiding the banner");
        }
        SetZone(55);
        DetailCall(world, "Bifrost.OnInZoneLeavePressed");
        var stale = NativeEvents.LeavePrompt(world)!;
        stale.Reparent(this);
        SetZone(86);
        DetailCall(world, "RefreshInZoneLeaveUi");
        await Frames();
        Require(NativeEvents.LeavePrompt(world) == null && banner.Visible, "Moving between event zones cancels the old confirmation");
        SetZone(21);
        DetailCall(world, "RefreshInZoneLeaveUi");
        DetailCall(world, "Bifrost.OnInZoneLeavePressed");
        Require(NativeEvents.LeavePrompt(world) == null && !banner.Visible, "Moradon has no stale leave prompt or banner");
        await Capture("outside-event");
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-verification.json",
            JsonSerializer.Serialize(new { checks, screenshots, liveTransactions = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("FORGOTTEN_TEMPLE_AUDIT_OK " + checks.Count);
        foreach (var layer in GetChildren().OfType<CanvasLayer>().ToArray()) layer.Free();
        world.Free();
        net.Free();
        await Frames();
    }
}
