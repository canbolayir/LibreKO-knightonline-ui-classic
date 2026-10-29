using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    /// <summary>
    /// social-audit: the Classic social frame on every new social window (nation chat, clan and join-request
    /// boards, leader transfer, commander) for one nation. Each window is captured in its main
    /// states; the frame, title, close button and content are checked at their actual rendered bounds, every
    /// visible control stays inside the content, native inks are mapped to the Classic palette, and the main
    /// native actions run through the restyled controls.
    /// </summary>
    private async Task CaptureSocialAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        // The installed content: item data (whisper item links, text table) and the emoticon sheet.
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        ClassicSocialSkin.Register(PluginHost.Ui);
        var checks = new List<string>(); var screens = new List<object>();
        string prefix = nation == 1 ? "karus" : "human";
        void Require(bool value, string text) { if (!value) throw new Exception("SOCIAL_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        // Flow containers place fractional column widths; a sub-pixel overhang is not a visible defect.
        static bool Inside(Rect2 outer, Rect2 inner) => outer.Grow(1).Encloses(inner);
        var nativeInks = new[] { UiTheme.Gold, UiTheme.GoldBright, UiTheme.GoldVivid, UiTheme.TextHi, UiTheme.TextLo, UiTheme.TextDim };

        async Task Capture(HudWindow window, string state)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(3);
            string file = prefix + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            var frame = window.GetChildren().OfType<ClassicSocialFrame>().Single();
            Require(window.Size == frame.Size, state + ": the Classic frame fills the window " + frame.Size);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + ": complete window fits the viewport " + window.GetGlobalRect());
            Require(frame.Size.X >= ClassicSocialLayout.MinimumWidth, state + ": frame keeps its minimum width");
            foreach (var part in Descendants(frame).OfType<Control>().Where(c => c.HasMeta("social_expected_rect")))
                Require(part.GetRect() == part.GetMeta("social_expected_rect").AsRect2(), state + ": actual bounds of " + part.Name + " " + part.GetRect());
            var content = frame.Content; var area = content.GetGlobalRect();
            Require(Inside(frame.GetGlobalRect().Grow(-ClassicFrame.PlainEdge), area), state + ": content stays inside the frame line");
            foreach (var control in Descendants(content).OfType<Control>().Where(c => c.IsVisibleInTree()))
            {
                if (Descendants(content).OfType<ScrollContainer>().Any(s => s != control && s.IsAncestorOf(control))) continue;
                var rect = control.GetGlobalRect();
                if (rect.Size.X <= 0 || rect.Size.Y <= 0) continue;
                Require(Inside(area, rect), state + ": " + control.GetType().Name + " " + control.Name + " stays inside the content " + rect);
            }
            foreach (var button in Descendants(content).OfType<Button>().Where(b => b.IsVisibleInTree() && b.Text.Length > 0 && b is not CheckBox and not CheckButton))
            {
                Require(button.Size.Y >= ClassicSocialLayout.ActionHeight, state + ": Classic button height " + button.Text + " " + button.Size);
                var font = button.GetThemeFont("font"); int size = button.GetThemeFontSize("font_size");
                Require(font == Plugin.Kit.Bold, state + ": Arial Bold caption " + button.Text);
                float text = font.GetStringSize(button.Text, HorizontalAlignment.Left, -1, size).X;
                Require(text <= button.Size.X - 2, state + ": caption fits its button " + button.Text + " " + text + "/" + button.Size.X);
            }
            foreach (var label in Descendants(content).OfType<Label>().Where(l => l.IsVisibleInTree() && l.Text.Length > 0))
            {
                var font = label.GetThemeFont("font");
                Require(font == Plugin.Kit.Bold || font == Plugin.Kit.Regular, state + ": Arial label " + label.Text);
                Require(!nativeInks.Contains(label.GetThemeColor("font_color")), state + ": Classic ink on " + label.Text);
            }
            foreach (var edit in Descendants(content).OfType<LineEdit>().Where(e => e.IsVisibleInTree()))
                Require(edit.Size.Y >= ClassicSocialLayout.InputHeight && edit.GetThemeStylebox("normal") is StyleBoxFlat, state + ": Classic edit field " + edit.Name);
            screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }

        async Task<(WorldRoot World, HudWindow Window)> Open(string view)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var window = (HudWindow)DetailCall(world, "BuildSocialUiPreview", view)!;
            var layer = new CanvasLayer { Name = "social_" + view };
            AddChild(layer); layer.AddChild(window); window.Visible = true;
            KnightOnlineUiClassic.Native.NativeSocial.Prepare(window, world);
            await Frames();
            Require(window.GetChildren().OfType<ClassicSocialFrame>().Count() == 1, view + ": native window uses the Classic social frame");
            return (world, window);
        }
        void Done((WorldRoot World, HudWindow Window) opened)
        {
            var layer = opened.Window.GetParent();
            layer.QueueFree(); opened.World.QueueFree();
        }
        Button ButtonOf(Control root, string text) => Descendants(root).OfType<Button>().Single(b => b.Text == text && b.IsVisibleInTree());
        void Click(Control row) => row.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });

        // Nation chat: public channels with the joined channel, then a selection.
        {
            var opened = await Open("nation-public"); var (world, window) = opened;
            var rows = (VBoxContainer)DetailField(world, "NationChannels.ncPublicRows")!;
            Require(Descendants(window).OfType<Button>().Single(b => b.Name == "nation_refresh").Text == "Refresh", "nation chat: refresh carries a word, not a glyph");
            await Capture(window, "nation-public");
            Require(rows.GetChildren().OfType<PanelContainer>().Count(r => r.GetMeta("social_selected", false).AsBool()) == 1, "nation chat: one selected channel row");
            Click(rows.GetChildren().OfType<PanelContainer>().First());
            await Frames();
            Require((int)DetailField(world, "NationChannels.NationSelectedChannel")! == 1, "nation chat: a row click selects the channel natively");
            var live = rows.GetChildren().OfType<PanelContainer>().Where(r => !r.IsQueuedForDeletion()).ToArray();
            Require(live[0].GetMeta("social_selected", false).AsBool() && live[0].HasMeta(ClassicSocialStyle.Styled), "nation chat: the rebuilt selected row is styled Classic");
            Require(ButtonOf(window, "Leave").Visible && !Descendants(window).OfType<Button>().Any(b => b.Text == "Enter" && b.IsVisibleInTree()), "nation chat: joined channel shows Leave");
            await Capture(window, "nation-public-selected");
            Descendants(window).OfType<Button>().Single(b => b.Name == "social_close").EmitSignal(BaseButton.SignalName.Pressed);
            Require(!window.Visible && !(bool)DetailField(world, "NationChannels.NationShown")!, "nation chat: the Classic close button runs the native close");
            Done(opened);
        }
        // Nation chat, Private tab (fa3cfc3): the room list over a search row (room name, Search, Create room); a
        // double-click enters a room. The members and search windows are gone: members and the owner's actions
        // live in the room window.
        {
            var opened = await Open("nation-private"); var (world, window) = opened;
            var rows = (VBoxContainer)DetailField(world, "NationChannels.ncPrivateRows")!;
            var list = (ScrollContainer)rows.GetParent();
            var search = (LineEdit)DetailField(world, "NationChannels.ncSearchEdit")!;
            IEnumerable<PanelContainer> Live() => rows.GetChildren().OfType<PanelContainer>().Where(r => !r.IsQueuedForDeletion());
            await Capture(window, "nation-private");
            var find = ButtonOf(window, "Search"); var create = ButtonOf(window, "Create room");
            var content = window.GetChildren().OfType<ClassicSocialFrame>().Single().Content.GetGlobalRect();
            var (field, findBox, createBox) = (search.GetGlobalRect(), find.GetGlobalRect(), create.GetGlobalRect());
            Require(search.Name == "nation_search_edit" && search.IsVisibleInTree() && search.PlaceholderText == "Room name", "nation chat: the search row starts with the client's room name field");
            Require(findBox.Position.Y == field.Position.Y && createBox.Position.Y == field.Position.Y && findBox.Size.Y == field.Size.Y && createBox.Size.Y == field.Size.Y,
                "nation chat: room name, Search and Create room share one row " + field + " " + findBox + " " + createBox);
            Require(findBox.Position.X >= field.End.X && createBox.Position.X > findBox.End.X && Math.Abs(field.Position.X - content.Position.X) <= 1 && Math.Abs(createBox.End.X - content.End.X) <= 1,
                "nation chat: the field and Search start the row, Create room ends it at the content edge " + content);
            Require(list.GetGlobalRect().End.Y <= field.Position.Y, "nation chat: the search row sits under the room list");
            Require(list.Size.Y % 22 == 0 && Live().All(r => r.Size.Y == 22), "nation chat: the room list shows whole 22 px rows " + list.Size);
            Require(!Descendants(window).OfType<Button>().Any(b => b.IsVisibleInTree() && b.Text is "Enter" or "Leave" or "Open" or "Change options" or "Number of admissions" or "Cancel"),
                "nation chat: the Private tab has no Enter, Leave, Open, owner or Cancel button any more");
            Require(Live().All(r => r.TooltipText == "Double-click to enter" && !Descendants(r).OfType<TextureRect>().Any(t => t.IsVisibleInTree())), "nation chat: rows carry the client's double-click hint and no lock or nation icon");
            // Search: the client sends the word (an empty field lists every room) and its reply fills the Private tab.
            search.Text = "Siege"; find.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            DetailCall(world, "NationChannels.OnNationSearch", new List<NationRoom> { new(Game.Session.NationChat.NationChat.RoomId, "Siege at nine", true, LibreKO.Domain.Nations.Karus, 4, 30) });
            await Frames();
            Require(Live().Count() == 1 && Live().All(r => r.HasMeta(ClassicSocialStyle.Styled)) && search.Text == "Siege", "nation chat: a search reply lists its rooms in Classic rows and keeps the word");
            await Capture(window, "nation-private-search");
            // A double-click on the joined room shows its room window natively.
            DetailCall(world, "ChatReactions.Build"); DetailCall(world, "Whispers.Build"); DetailCall(world, "NationChannels.Build"); DetailCall(world, "ChatRoom.Build");
            Live().First().EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true }); await Frames(2);
            Require(((HudWindow)DetailField(world, "ChatRoom.RoomWindow")!).Visible, "nation chat: a double-click on the joined room shows its room window natively");
            DetailCall(world, "ChatRoom.Dispose"); DetailCall(world, "NationChannels.Dispose"); DetailCall(world, "Whispers.Dispose"); DetailCall(world, "ChatReactions.Dispose");
            // Outside a room the list keeps the plain row inks.
            Game.Session.NationChat.NationChat.Left(); DetailCall(world, "NationChannels.OnNationRooms", new List<NationRoom>
            {
                new(1001, "Moradon traders", false, LibreKO.Domain.Nations.Karus, 12, 50), new(1004, "Clan recruits", true, LibreKO.Domain.Nations.Karus, 2, 10),
            });
            await Frames();
            Require(Live().Count() == 2 && Live().All(r => r.GetMeta("social_row", false).AsBool()), "nation chat: a refreshed list outside a room keeps the Classic rows");
            await Capture(window, "nation-private-left");
            Done(opened);
        }
        foreach (var view in new[] { "nation-create", "nation-password" })
        {
            var opened = await Open(view);
            await Capture(opened.Window, view);
            Done(opened);
        }
        // Commander: order bar in both directions.
        {
            var opened = await Open("commander"); var (world, window) = opened;
            var flip = Descendants(window).OfType<Button>().Single(b => b.Name == "commander_flip");
            Require(flip.Text == "Rotate", "commander: the direction toggle carries a word");
            await Capture(window, "commander");
            var slots = Descendants(window).OfType<Button>().Where(b => b.Text.Length == 0 && b.GetChildren().OfType<TextureRect>().Any()).ToArray();
            Require(slots.Length > 0 && slots.All(s => s.GetThemeStylebox("normal") is StyleBoxTexture), "commander: order slots sit in the Classic cell");
            GD.Print("SOCIAL_AUDIT commander icons with art: " + slots.Count(s => s.GetChildren().OfType<TextureRect>().Any(t => t.Texture != null)) + "/" + slots.Length);
            flip.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
            Require(DetailField(world, "PartyCommander.commanderSlots") is VBoxContainer, "commander: Rotate runs the native flip");
            await Capture(window, "commander-vertical");
            Done(opened);
        }
        foreach (var view in new[] { "clan-board", "clan-message", "clan-join-list", "wish-board", "wish-message", "wish-requests" })
        {
            var opened = await Open(view);
            await Capture(opened.Window, view);
            Done(opened);
        }
        // Leader transfer: the preselected heir enables Confirm; another pick moves the selection.
        {
            var opened = await Open("clanhandover"); var (world, window) = opened;
            var choices = (Button[])DetailField(world, "ClanHandover.handoverChoices")!;
            Require(choices.Count(c => c.Visible && c.ButtonPressed) == 1 && !ButtonOf(window, "Confirm").Disabled, "leader transfer: preselected heir enables Confirm");
            await Capture(window, "clanhandover");
            choices[0].ButtonPressed = true; await Frames();
            Require(choices[0].ButtonPressed && !choices[1].ButtonPressed, "leader transfer: picking another heir moves the selection natively");
            Require(choices[0].GetThemeStylebox("pressed") is StyleBoxFlat picked && picked.BorderColor == ClassicSocialStyle.SelectedEdge, "leader transfer: the picked heir carries the gold selection line");
            await Capture(window, "clanhandover-picked");
            Done(opened);
        }
        await CaptureSocialPrompts(nation, output, Require, Frames, screens);
        System.IO.File.WriteAllText(output + "/" + prefix + "-social.json", JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("SOCIAL_AUDIT_OK " + prefix + " " + checks.Count + " checks / " + screens.Count + " screens");
        await Frames(); offline.Free();
    }
}
