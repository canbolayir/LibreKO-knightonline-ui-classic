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
        foreach (string id in new[] { "messenger", "chatrooms" })
        {
            var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildCommunicationUiPreview", id)!; AddChild(layer);
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
                var rows = (VBoxContainer)DetailField(world, "_msgrList")!; var to = (LineEdit)DetailField(world, "_msgrToInput")!;
                var message = (LineEdit)DetailField(world, "_msgrTextInput")!;
                Require(to.MaxLength == 20 && message.MaxLength == 128, "Messenger retains native input limits");
                Require(rows.GetChildren().OfType<PanelContainer>().Count() == 3, "Messenger preserves online and offline rows");
                var action = Descendants(rows).OfType<Button>().First(); await Click(action);
                Require(to.Text == "Rikka" && message.HasFocus(), "Whisper click selects native recipient and focuses message");
                to.GrabFocus(); await KeyInput(Key.Enter); Require(message.HasFocus(), "Recipient Enter advances to message without sending");
                message.Text = "Ready for the hunt."; await Capture(window, "whisper-ready"); await KeyInput(Key.Enter);
                Require(message.Text == "" && to.Text == "Rikka", "Message Enter follows native send and clears only message");
                message.Text = "  "; DetailCall(world, "DoMessengerSend"); Require(message.Text == "  ", "Blank message does not send");
                to.Text = ""; message.Text = "Keep draft"; DetailCall(world, "DoMessengerSend"); Require(message.Text == "Keep draft", "Missing recipient does not send");
                var many = new List<MessengerBuddy>(); for (int i = 0; i < 24; i++) many.Add(new MessengerBuddy { CharId = i + 1, Name = "LongCharacterName1299", Online = i % 2 == 0 });
                DetailCall(world, "OnMessengerList", many); await Frames();
                Require(rows.GetChildCount() == 24, "Refresh immediately replaces old buddy children");
                Require(Descendants(panel).OfType<ScrollContainer>().Single().GetVScrollBar().Visible, "Long roster scrolls inside fixed viewport");
                await Capture(window, "messenger-full");
                DetailCall(world, "OnMessengerList", new List<MessengerBuddy>()); Require(rows.GetChildCount() == 1 && rows.GetChild(0) is Label { Text: "No one else online." }, "Empty buddy response has immediate empty state");
                await Capture(window, "messenger-empty"); to.GrabFocus(); await KeyInput(Key.Escape);
                Require(!window.Visible && !(bool)DetailField(world, "_msgrShown")!, "Focused recipient Escape follows native Messenger close");
                DetailCall(world, "MessengerDispose");
            }
            else
            {
                var rows = (VBoxContainer)DetailField(world, "_chatRoomList")!; var log = (VBoxContainer)DetailField(world, "_chatRoomLog")!;
                var name = (LineEdit)DetailField(world, "_chatRoomNameInput")!; var message = (LineEdit)DetailField(world, "_chatRoomSayInput")!;
                Require(name.MaxLength == 30 && message.MaxLength == 128, "Room name and message limits remain native");
                Require(rows.GetChildren().OfType<PanelContainer>().Single(r => r.GetMeta("communication_selected").AsBool()).GetChildren().Count > 0, "Current room keeps Joined state");
                name.GrabFocus(); name.Text = "   "; await KeyInput(Key.Enter); Require(name.Text == "   ", "Blank room name cannot create");
                name.Text = "New room"; name.GrabFocus(); Require(name.HasFocus(), "Room name is focused for create Enter"); await KeyInput(Key.Enter); Require(name.Text == "", "Room name Enter follows native create callback; text=" + name.Text + "; focus=" + name.HasFocus());
                message.GrabFocus(); message.Text = "On my way"; await KeyInput(Key.Enter); Require(message.Text == "", "Room message Enter follows native send");
                var many = new List<ChatRoomEntry>(); for (int i = 0; i < 24; i++) many.Add(new ChatRoomEntry { RoomId = i + 1, Name = "A long room name for the market", MemberCount = 123 });
                DetailCall(world, "OnChatRoomList", many); await Frames(); Require(rows.GetChildCount() == 24, "Room refresh immediately replaces prior children");
                Require(Descendants(panel).OfType<ScrollContainer>().Single(s => s.Name == "rooms_scroll").GetVScrollBar().Visible, "Long room list scrolls within fixed viewport");
                await Capture(window, "rooms-full");
                for (int i = 0; i < 120; i++) DetailCall(world, "OnChatRoomSay", 7, "Rikka", "Message " + i);
                Require(log.GetChildCount() == 100, "120 native message callbacks return and retain exactly 100 latest lines");
                Require(Descendants(log.GetChild(0)).OfType<Label>().Single(l => l.Name == "communication_body").Text == "Message 20", "Log cap removes oldest messages immediately");
                Require(Descendants(log.GetChild(99)).OfType<Label>().Single(l => l.Name == "communication_body").Text == "Message 119", "Log cap retains latest native message");
                await Capture(window, "rooms-log-cap");
                DetailCall(world, "OnChatRoomList", new List<ChatRoomEntry>()); Require(rows.GetChildCount() == 1 && rows.GetChild(0) is Label, "Empty room response has immediate empty state");
                DetailCall(world, "OnChatRoomLeave", false); Require((int)DetailField(world, "_chatRoomCurrentId")! == 7, "Rejected leave retains membership");
                DetailCall(world, "OnChatRoomLeave", true); await Frames(); Require((int)DetailField(world, "_chatRoomCurrentId")! == 0, "Accepted leave clears membership");
                Require(Descendants(panel).OfType<Button>().Where(b => b.Name.ToString() is "rooms_send" or "rooms_leave").All(b => b.Disabled), "No-room Send and Leave show disabled state");
                message.Text = "Cannot send yet"; message.GrabFocus(); await KeyInput(Key.Enter); Require(message.Text == "Cannot send yet", "Sending without membership cannot clear or submit a message");
                await Capture(window, "rooms-empty");
                DetailCall(world, "OnChatRoomJoin", 9, false); Require((int)DetailField(world, "_chatRoomCurrentId")! == 0, "Rejected join preserves no-room state");
                DetailCall(world, "OnChatRoomJoin", 9, true); await Frames(); Require((int)DetailField(world, "_chatRoomCurrentId")! == 9, "Accepted join updates native membership");
                Require(Descendants(panel).OfType<Button>().Where(b => b.Name.ToString() is "rooms_send" or "rooms_leave").All(b => !b.Disabled), "Joined-room Send and Leave return enabled");
                message.GrabFocus(); await KeyInput(Key.Escape); Require(!window.Visible && (int)DetailField(world, "_chatRoomCurrentId")! == 9, "Focused message Escape closes window without leaving room");
                DetailCall(world, "ChatRoomDispose");
            }
            layer.Free(); world.Free();
        }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-communication.json", JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic communication audit: " + checks.Count + " checks / " + screens.Count + " screens"); offline.Free();
    }
}
