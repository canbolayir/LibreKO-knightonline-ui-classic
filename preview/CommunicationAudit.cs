using Godot;
using LibreKO;
using LibreKO.Network;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureCommunicationAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool value, string text) { if (!value) throw new Exception("COMMUNICATION_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task KeyInput(Key key)
        {
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
            await Frames(1);
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }); await Frames();
        }
        async Task Click(Control control)
        {
            var point = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point }); await Frames();
        }
        async Task Capture(HudWindow window, string state)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            var panel = window.GetChildren().OfType<ClassicCommunicationPanel>().Single();
            Require(window.Size == panel.CustomMinimumSize, state + " fixed frame size");
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete window fits viewport");
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("communication_expected_rect")))
            {
                Require(control.GetRect() == control.GetMeta("communication_expected_rect").AsRect2(), state + " actual control bounds " + control.Name + " " + control.GetRect());
                Require(control.GetParent() is Control parent && new Rect2(Vector2.Zero, parent.Size).Encloses(control.GetRect()), state + " control stays inside its composition " + control.Name);
            }
            foreach (var text in Descendants(panel).OfType<Label>().Where(l => l.IsVisibleInTree() && l.Name == "communication_body"))
            {
                Require(text.Size.Y >= 15 && text.Size.X >= 260, state + " native message has readable actual bounds " + text.Size);
                Require(!text.ClipText && text.AutowrapMode == TextServer.AutowrapMode.WordSmart, state + " wrapped message contributes its real text height");
            }
            foreach (var edit in Descendants(panel).OfType<LineEdit>()) Require(edit.TextDirection == Control.TextDirection.Ltr, state + " LTR input " + edit.Name);
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }
        foreach (string id in new[] { "messenger" })
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); var layer = (CanvasLayer)DetailCall(world, "BuildCommunicationUiPreview", id)!; AddChild(layer);
            var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicCommunicationSkin.Apply(window.Body)!;
            await Frames(); window.Position = new Vector2(50, 30); await Frames(2);
            var grip = Descendants(panel).OfType<Control>().Single(c => c.Name == "party_drag"); var start = window.Position;
            grip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = start + new Vector2(30, 15) });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = start + new Vector2(63, 36) });
            window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Require(window.Position == start + new Vector2(33, 21), id + " header forwards native dragging");
            await Capture(window, id);
            if (id == "messenger")
            {
                var rows = (VBoxContainer)DetailField(world, "Messenger.msgrList")!; var to = (LineEdit)DetailField(world, "Messenger.msgrToInput")!;
                var message = (LineEdit)DetailField(world, "Messenger.msgrTextInput")!;
                Require(to.MaxLength == 20 && message.MaxLength == 128, "Messenger retains native input limits");
                Require(rows.GetChildren().OfType<PanelContainer>().Count() == 3, "Messenger preserves online and offline rows");
                var action = Descendants(rows).OfType<Button>().First(); await Click(action);
                Require(to.Text == "Rikka" && message.HasFocus(), "Whisper click selects native recipient and focuses message");
                to.GrabFocus(); await KeyInput(Key.Enter); Require(message.HasFocus(), "Recipient Enter advances to message without sending");
                message.Text = "Ready for the hunt."; await Capture(window, "whisper-ready"); await KeyInput(Key.Enter);
                Require(message.Text == "" && to.Text == "Rikka", "Message Enter follows native send and clears only message");
                message.Text = "  "; DetailCall(world, "Messenger.DoSend"); Require(message.Text == "  ", "Blank message does not send");
                to.Text = ""; message.Text = "Keep draft"; DetailCall(world, "Messenger.DoSend"); Require(message.Text == "Keep draft", "Missing recipient does not send");
                var many = new List<MessengerBuddy>(); for (int i = 0; i < 24; i++) many.Add(new MessengerBuddy { CharId = i + 1, Name = "LongCharacterName1299", Online = i % 2 == 0 });
                DetailCall(world, "OnMessengerList", many); await Frames();
                Require(rows.GetChildCount() == 24, "Refresh immediately replaces old buddy children");
                Require(Descendants(panel).OfType<ScrollContainer>().Single().GetVScrollBar().Visible, "Long roster scrolls inside fixed viewport");
                await Capture(window, "messenger-full");
                DetailCall(world, "OnMessengerList", new List<MessengerBuddy>()); Require(rows.GetChildCount() == 1 && rows.GetChild(0) is Label { Text: "No one else online." }, "Empty buddy response has immediate empty state");
                await Capture(window, "messenger-empty"); to.GrabFocus(); await KeyInput(Key.Escape);
                Require(!window.Visible && !(bool)DetailField(world, "Messenger.msgrShown")!, "Focused recipient Escape follows native Messenger close");
                DetailCall(world, "Messenger.Dispose");
            }
            layer.Free(); world.Free();
        }
        await CaptureNationRooms(nation, output, Require, Frames, screens);
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-communication.json", JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic communication audit: " + checks.Count + " checks / " + screens.Count + " screens"); offline.Free();
    }
}

public partial class Preview
{
    /// <summary>
    /// The chat-room pass on the nation chat rooms that replaced the old chat-room window: refreshed room
    /// lists replace their rows at once and scroll by whole rows inside the fixed list, the empty reply leaves
    /// no stale rows, the Private tab is a search row (fa3cfc3) whose rooms are entered with a double-click, a
    /// locked room asks for its password in the Classic frame, and room lines keep their own channel tag.
    /// </summary>
    private async Task CaptureNationRooms(int nation, string output, Action<bool, string> Require, Func<int, Task> Frames, List<object> screens)
    {
        ClassicSocialSkin.Register(LibreKO.Plugins.PluginHost.Ui);
        string prefix = nation == 1 ? "karus" : "human";
        var me = Game.Session.Self.LastEnter; me.Name = "Tester10"; me.Nation = LibreKO.Domain.Nations.Karus; Native.Call(Game.Session.Self, "SeedPreviewEnter", me);
        var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, "BuildSocialUiPreview", "nation-private")!;
        var layer = new CanvasLayer(); AddChild(layer); layer.AddChild(window); window.Visible = true;
        KnightOnlineUiClassic.Native.NativeSocial.Prepare(window, world);
        async Task Capture(string state)
        {
            await Frames(6); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(3);
            var frame = window.GetChildren().OfType<ClassicSocialFrame>().Single();
            Require(window.Size == frame.Size && GetViewportRect().Encloses(window.GetGlobalRect()), state + " Classic frame fills the window inside the viewport");
            string file = prefix + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }
        IEnumerable<PanelContainer> Live(VBoxContainer rows) => rows.GetChildren().OfType<PanelContainer>().Where(r => !r.IsQueuedForDeletion());
        Button Shown(string text) => Descendants(window).OfType<Button>().Single(b => b.Text == text && b.IsVisibleInTree());
        var rows = (VBoxContainer)DetailField(world, "NationChannels.ncPrivateRows")!; var scroll = (ScrollContainer)rows.GetParent();
        await Capture("nation-rooms");
        float height = window.Size.Y, pitch = 22;
        Require(scroll.Size.Y % pitch == 0 && Live(rows).All(r => r.Size.Y == pitch), "Room list shows whole 22 px rows " + scroll.Size + " " + string.Join(",", Live(rows).Select(r => r.Size.Y)));
        bool NoRoomButtons() => !Descendants(window).OfType<Button>().Any(b => b.IsVisibleInTree() && b.Text is "Enter" or "Leave" or "Open");
        Require(Shown("Search").Visible && Shown("Create room").Visible && NoRoomButtons(), "The Private tab is a search row: Search and Create room, no Enter, Leave or Open");
        var many = new List<LibreKO.Network.NationRoom>();
        for (int i = 0; i < 24; i++) many.Add(new LibreKO.Network.NationRoom(2000 + i, "A long room name for the market " + i, i % 3 == 0, LibreKO.Domain.Nations.Karus, 12, 50));
        DetailCall(world, "NationChannels.OnNationRooms", many); await Frames(6);
        Require(Live(rows).Count() == 24, "Room refresh immediately replaces prior rows");
        Require(scroll.GetVScrollBar().Visible && window.Size.Y == height, "Long room list scrolls within the fixed list");
        Require(Live(rows).All(r => r.HasMeta(ClassicSocialStyle.Styled) && r.GetMeta("social_row", false).AsBool()), "Refreshed rooms use the Classic row");
        await Capture("nation-rooms-full");
        DetailCall(world, "NationChannels.OnNationRooms", new List<LibreKO.Network.NationRoom>()); await Frames(6);
        Require(!Live(rows).Any() && window.Size.Y == height, "Empty room reply leaves no stale rows and keeps the frame");
        Game.Session.NationChat.NationChat.Left(); DetailCall(world, "NationChannels.RefreshNationButtons"); await Frames(2);
        Require(Shown("Search").Visible && NoRoomButtons(), "Without a room the search row stays");
        await Capture("nation-rooms-empty");
        DetailCall(world, "NationChannels.OnNationRooms", new List<LibreKO.Network.NationRoom> { new(3001, "Clan recruits", true, LibreKO.Domain.Nations.Karus, 2, 10) }); await Frames(4);
        var password = (HudWindow)DetailField(world, "NationChannels.ncPassword")!;
        Live(rows).First().EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(2);
        Require(!password.Visible && (int)DetailField(world, "NationChannels.NationSelectedRoom")! == 3001, "A single click selects the room without entering");
        Live(rows).First().EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true }); await Frames(4);
        Require(password.Visible && password.GetChildren().OfType<ClassicSocialFrame>().Count() == 1, "A double-click on a locked room asks for its password in the Classic frame");
        Require(((LineEdit)DetailField(world, "NationChannels.ncPasswordEdit")!).Text == "", "Password prompt opens empty");
        Require(KnightOnlineUiClassic.ClassicChatFormat.Channel((byte)ChatType.ChatRoom).Tag == "room", "Room lines carry the Classic room channel tag");
        DetailCall(world, "NationChannels.Dispose"); layer.QueueFree(); world.QueueFree(); await Frames(2);
    }
}
