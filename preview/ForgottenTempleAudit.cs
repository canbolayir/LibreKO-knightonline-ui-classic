using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using System.Reflection;
using System.Text.Json;

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
        var world = new World();
        DetailCall(world, "BuildBifrostUi");
        DetailCall(world, "BuildInZoneLeaveUi");
        foreach (var layer in world.GetChildren().OfType<CanvasLayer>().ToArray()) layer.Reparent(this);
        PluginHost.Ui.ReplaceDialogs(request => new KnightOnlineUiClassic.Windows.MessageBox(request));
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
        var zone = typeof(World).GetField("_zone", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void SetZone(int value) => zone.SetValue(world, Convert.ChangeType(value, zone.FieldType));
        var banner = (Control)DetailField(world, "_inZoneLeaveBanner")!;
        var title = (Label)DetailField(world, "_inZoneLeaveTitleLbl")!;
        DetailCall(world, "OnBifrostTime", 600, TempleEventType.ForgottenTemple);
        var join = (Control)DetailField(world, "_joinModal")!;
        var joinTitle = (Label)DetailField(world, "_joinModalTitle")!;
        Require(join.Visible && joinTitle.Text.Contains("FORGOTTEN TEMPLE"), "FT registration uses its event title and existing join UI");
        await Capture("ft-registration");
        DetailCall(world, "OnBifrostJoinResult", true, 55);
        Require(((Button)DetailField(world, "_joinModalBtn")!).Text == "Cancel", "Joining FT updates registration state");
        await Capture("ft-registered");
        DetailCall(world, "EndBifrostEvent");
        SetZone(55);
        DetailCall(world, "RefreshInZoneLeaveUi");
        await Frames();
        banner.Position = new Vector2(480, 24);
        Require(banner.Visible && title.Text == "Forgotten Temple", "FT leave banner appears after zone readiness refresh");
        Require(GetViewportRect().Encloses(banner.GetGlobalRect()), "FT leave banner fits the viewport");
        await Capture("ft-leave-banner");
        foreach (int eventZone in new[] { 55, 84, 85, 86, 87 })
        {
            SetZone(eventZone);
            DetailCall(world, "RefreshInZoneLeaveUi");
            Require(banner.Visible, $"Zone {eventZone} retains an event leave banner");
            DetailCall(world, "OnInZoneLeavePressed");
            var notice = NativeEvents.LeavePrompt(world)!;
            notice.Reparent(this);
            var dialog = Descendants(notice).OfType<Control>().First(c => c.GetType().FullName == "KnightOnlineUiClassic.Windows.MessageBox");
            Require(dialog != null, $"Zone {eventZone} uses the Classic confirmation");
            DetailCall(world, "OnInZoneLeavePressed");
            Require(ReferenceEquals(notice, NativeEvents.LeavePrompt(world)), $"Zone {eventZone} prevents duplicate confirmations");
            if (eventZone == 55) await Capture("ft-leave-confirmation");
            dialog!._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            await Frames();
            Require(NativeEvents.LeavePrompt(world) == null && banner.Visible, $"Zone {eventZone} Escape cancels without hiding the banner");
        }
        SetZone(55);
        DetailCall(world, "OnInZoneLeavePressed");
        var stale = NativeEvents.LeavePrompt(world)!;
        stale.Reparent(this);
        SetZone(86);
        DetailCall(world, "RefreshInZoneLeaveUi");
        await Frames();
        Require(NativeEvents.LeavePrompt(world) == null && banner.Visible, "Moving between event zones cancels the old confirmation");
        SetZone(21);
        DetailCall(world, "RefreshInZoneLeaveUi");
        DetailCall(world, "OnInZoneLeavePressed");
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
