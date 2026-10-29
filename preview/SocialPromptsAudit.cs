using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;

public partial class Preview
{
    /// <summary>
    /// social-audit, second part: the prompt seams and the whisper window. The Classic player menu (client rows,
    /// row and outside-click close), the Classic text prompt (invite by name: submit, cancel, limits), the
    /// friend-request consent on the Classic message box, and the Classic private-message frame on the client's
    /// room thread and composer (fa3cfc3): header actions, the thread's rows in the retail one-line look, voice
    /// notes, reactions, older history, Send, the record cell and strip, the emoticon and size popups, minimize and
    /// restore, and an incoming voice note opening a minimized conversation.
    /// </summary>
    private async Task CaptureSocialPrompts(int nation, string output, Action<bool, string> Require, Func<int, Task> Frames, List<object> screens)
    {
        string prefix = nation == 1 ? "karus" : "human";
        PluginHost.Ui.ReplacePlayerMenu(request => new ClassicPlayerMenu(request));
        PluginHost.Ui.ReplaceTextInput(request => new ClassicTextPrompt(request));
        PluginHost.Ui.ReplaceDialogs(request => new MessageBox(request));
        async Task Shot(string state, Control? subject = null)
        {
            await Frames(6);
            if (subject != null) Require(GetViewportRect().Encloses(subject.GetGlobalRect()), state + ": stays inside the viewport " + subject.GetGlobalRect());
            string file = prefix + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = subject?.GetGlobalRect().ToString() ?? "" });
        }
        void Expected(Control root, string state)
        {
            foreach (var control in Descendants(root).OfType<Control>().Where(c => c.HasMeta(ClassicWorldOverlays.ExpectedRect)))
                Require(control.GetRect() == control.GetMeta(ClassicWorldOverlays.ExpectedRect).AsRect2(), state + ": actual bounds of " + control.Name + " " + control.GetRect());
        }

        // Player menu: the client's rows for a player out of view.
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            DetailCall(world, "PlayerMenu.ShowByName", "Rikka", new Vector2(420, 260));
            var layer = (CanvasLayer)DetailField(world, "PlayerMenu.skinLayer")!;
            layer.Reparent(this);
            var menu = Descendants(layer).OfType<ClassicPlayerMenu>().Single();
            await Frames(4);
            var panel = (Control)menu.FindChild("player_menu_panel", true, false)!;
            var rows = Descendants(panel).OfType<Button>().ToArray();
            Require(rows.Length == 7 && rows.All(r => r.Size.Y == ClassicSocialLayout.MenuRow && r.GetThemeFont("font") == Plugin.Kit.Bold), "player menu: every client row on a 22 px Classic row");
            Require(Descendants(panel).OfType<Label>().Single().Text == "Rikka", "player menu: the player's name heads the menu");
            Require(panel.Position == new Vector2(422, 262), "player menu: opens at the cursor " + panel.Position);
            await Shot("player-menu", panel);
            rows.Single(r => r.Text == "Copy name").EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(DetailField(world, "PlayerMenu.skinLayer") == null, "player menu: a row runs the client's action and closes the menu");
            DetailCall(world, "PlayerMenu.ShowByName", "LongCharacterName1299", new Vector2(980, 690));
            var edge = (CanvasLayer)DetailField(world, "PlayerMenu.skinLayer")!; edge.Reparent(this); await Frames(4);
            var edgePanel = (Control)edge.FindChild("player_menu_panel", true, false)!;
            Require(GetViewportRect().Encloses(edgePanel.GetGlobalRect()), "player menu: near the screen edge it stays on screen");
            await Shot("player-menu-edge", edgePanel);
            Descendants(edge).OfType<ClassicPlayerMenu>().Single().EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(2);
            Require(DetailField(world, "PlayerMenu.skinLayer") == null, "player menu: a click outside closes it");
            if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
            if (GodotObject.IsInstanceValid(edge)) edge.QueueFree();
            world.QueueFree();
        }
        // Text prompt: invite by name.
        {
            string? submitted = null; bool cancelled = false;
            var notice = Notice.AskText(this, "Invite player by name:", text => submitted = text, "Party Invite", placeholder: "Character name", maxLength: 20, confirmText: "Send", onCancel: () => cancelled = true);
            await Frames(4);
            var prompt = Descendants(notice).OfType<ClassicTextPrompt>().Single();
            var input = (LineEdit)prompt.FindChild("prompt_input", true, false)!;
            var panel = (Control)prompt.FindChild("prompt_panel", true, false)!;

            Expected(panel, "text prompt");
            Require(input.MaxLength == 20 && input.PlaceholderText == "Character name" && input.HasFocus(), "text prompt: client limits and placeholder, focused input");
            Require(((Button)prompt.FindChild("prompt_confirm", true, false)!).Text == "Send", "text prompt: the client's confirm caption");
            Require(panel.Size == ClassicSocialLayout.PromptSize(ClassicSocialLayout.PromptLine), "text prompt: message box sized to its content " + panel.Size);
            input.Text = "Rikka"; await Shot("text-prompt", panel);
            input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text); await Frames(2);
            Require(submitted == "Rikka" && !cancelled, "text prompt: Enter submits the typed name to the client");
            var second = Notice.AskText(this, "Invite player by name:", text => submitted = text, "Party Invite", onCancel: () => cancelled = true);
            await Frames(3);
            ((Button)Descendants(second).OfType<ClassicTextPrompt>().Single().FindChild("prompt_cancel", true, false)!).EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(cancelled, "text prompt: Cancel runs the client's cancel");
        }
        // Friend request consent: the client's confirmation on the Classic message box.
        {
            bool accepted = false;
            var ask = Notice.Confirm(this, "[Rikka] sent a friend request. Will you accept?", "OK", "Cancel", () => accepted = true, title: "Friends");
            await Frames(4);
            var box = Descendants(ask).OfType<MessageBox>().Single();
            await Shot("friend-request", box);
            Descendants(box).OfType<BaseButton>().Single(c => c.Name == "btn_ok").EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(accepted, "friend request: OK accepts through the client");
        }
        // Whisper (fa3cfc3): the conversation runs on the room's thread and composer. The Classic PM frame keeps the
        // client's header actions; the thread's own rows are restyled in place into the retail one-line look; the
        // original send cell runs the composer's Send (not "Load older messages"); the record cell, the recording
        // strip and the composer's emoticon picker sit in the original frame.
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var window = (HudWindow)DetailCall(world, "Preview.BuildWhisper", false)!;
            var layer = (CanvasLayer)DetailField(world, "Whispers.whisperLayer")!;
            layer.Reparent(this); layer.AddChild(window); window.Visible = true;
            var thread = Descendants(window.Body).OfType<ChatThreadView>().Single();
            var composer = Descendants(window.Body).OfType<ChatComposer>().Single();
            var input = composer.Input;
            NativeWhispers.Prepare(window, world);
            NativeWhispers.Prepare(window, world);
            await Frames(6); window.Position = new Vector2(360, 120); await Frames(3);
            Require(window.GetChildren().Count(c => c.Name == "classic_whisper") == 1, "whisper: preparing a conversation twice keeps one Classic frame");
            var surface = (Control)window.FindChild("classic_whisper", true, false)!;
            var expanded = (Control)thread.GetParent();
            var open = Plugin.Kit.Layout("co_whisper_open_us");
            var actions = Descendants(surface).OfType<Button>().Where(b => b.Name.ToString().StartsWith("whisper_action_")).ToArray();
            Require(actions.Length == 3 && actions.All(a => a.IsVisibleInTree() && new Rect2(0, 0, 304, 24).Encloses(a.GetRect())), "whisper: the client's three header actions sit on the original title bar");
            var title = Descendants(surface).OfType<Label>().First(l => l.Text == "Rikka" && l.IsVisibleInTree());
            Require(title.GetRect().End.X <= actions[0].Position.X, "whisper: the title stops before the header actions");

            // The thread fills the original log area; its rows stay the client's own rows (reactions and voice notes
            // refresh them), restyled without bubbles or time stamps.
            var logArea = new Rect2(open.Find("exit_chat")!.Position, open.Find("exit_chat")!.SizeVec);
            Require(surface.IsAncestorOf(thread) && thread.GetRect() == logArea, "whisper: the client's thread fills the original log area " + thread.GetRect());
            var log = Native.Get<VBoxContainer>(thread, "log")!;
            var timeline = Game.Session.RoomMedia.DirectTimeline("Rikka");
            var posts = NativeWhispers.Posts(thread);
            int self = (int)DetailField(world, "MyId")!;
            Require(posts.Count == timeline.Posts.Count(p => p.Kind != LibreKO.Domain.RoomPostKind.Notice) && posts.Keys.All(r => r.GetParent() == log),
                "whisper: every post keeps the thread's own row in its log (" + posts.Count + ")");
            Require(log.GetChildren().All(r => r.HasMeta(ClassicWhisperThread.RowMeta)), "whisper: every row of the thread is restyled Classic");
            var threadBox = thread.GetGlobalRect();
            foreach (var (row, post) in posts)
            {
                var rowControl = (Control)row;
                bool mine = post.SenderId == self;
                var ink = mine ? ClassicWhisperThread.Mine : ClassicWhisperThread.Partner;
                Require(rowControl.GetGlobalRect().Position.X >= threadBox.Position.X - .5f && rowControl.GetGlobalRect().End.X <= threadBox.End.X + .5f, "whisper: post " + post.Id + " stays within the log width " + rowControl.GetGlobalRect());
                Require(Descendants(rowControl).OfType<PanelContainer>().First().GetThemeStylebox("panel") is StyleBoxEmpty, "whisper: post " + post.Id + " has no bubble");
                Require(!Descendants(rowControl).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text == post.Time.ToString("HH:mm")), "whisper: post " + post.Id + " shows no time stamp");
                if (post.Kind == LibreKO.Domain.RoomPostKind.VoiceNote)
                {
                    var play = Descendants(rowControl).OfType<Button>().Single(b => b.Name == "whisper_note_play");
                    var wave = Descendants(rowControl).OfType<VoiceWaveformView>().Single();
                    var clock = Descendants(rowControl).OfType<Label>().Single(l => l.Name == "whisper_note_clock");
                    Require(play.HasMeta(ClassicChatRoomSkin.CellMeta) && play.Size == new Vector2(ClassicWhisperLayout.PlayCell, ClassicWhisperLayout.PlayCell) && wave.Played == ink && clock.GetThemeColor("font_color") == ink,
                        "whisper: voice note " + post.Id + " has a Classic play cell and the line's ink");
                    var (p, w, c) = (play.GetGlobalRect(), wave.GetGlobalRect(), clock.GetGlobalRect());
                    Require(p.End.X <= w.Position.X && w.End.X <= c.Position.X && Math.Abs(p.GetCenter().Y - w.GetCenter().Y) <= 1 && Math.Abs(c.GetCenter().Y - w.GetCenter().Y) <= 2,
                        "whisper: voice note " + post.Id + " plays, shows its waveform and its clock on one line " + p + " " + w + " " + c);
                    Require(mine ? threadBox.End.X - c.End.X <= ClassicWhisperLayout.ReactCell + 4 : p.Position.X - threadBox.Position.X <= 1, "whisper: voice note " + post.Id + " keeps its side " + p + " " + c);
                    continue;
                }
                var text = Descendants(rowControl).OfType<RichTextLabel>().Single();
                Require(text.Text.Contains("[color=#" + ink.ToHtml(false) + "]") && text.HorizontalAlignment == (mine ? HorizontalAlignment.Right : HorizontalAlignment.Left) && text.GetThemeFont("normal_font") == Plugin.Kit.ChatStrong,
                    "whisper: post " + post.Id + (mine ? " is a white line on the right" : " is a cyan line on the left"));
                Require(text.TooltipText == post.Time.ToString("HH:mm"), "whisper: post " + post.Id + " keeps its time as the line's tooltip");
                if (post.Reactions.Total == 0 && text.GetLineCount() == 1)
                    Require(rowControl.Size.Y <= 20, "whisper: a one-line post keeps the retail line pitch " + rowControl.Size);
                // Item links keep the client's link (its tooltip on click) with the item's grade ink and name.
                if (LibreKO.Domain.ChatItemLink.Split(post.Text).FirstOrDefault(s => s.ItemId != 0) is { ItemId: > 0 } link)
                    Require(text.Text.Contains("[url=") && text.GetParsedText().Contains("[" + LibreKO.Domain.ItemData.DisplayName(link.ItemId) + "]") && !text.GetParsedText().Contains('\u0001'),
                        "whisper: post " + post.Id + " shows its item link by name: " + text.GetParsedText());
            }
            var notice = log.GetChildren().OfType<Control>().Single(r => r.GetMeta(ClassicWhisperThread.RowMeta).AsString() == "notice");
            Require(Descendants(notice).OfType<Label>().Single().GetThemeColor("font_color") == ClassicWhisperThread.Notice && Descendants(notice).OfType<Label>().Single().HorizontalAlignment == HorizontalAlignment.Left,
                "whisper: the notice is a yellow line");
            var days = log.GetChildren().OfType<Control>().Where(r => r.GetMeta(ClassicWhisperThread.RowMeta).AsString() == "day").ToArray();
            Require(days.Length == 2 && days.All(d => Descendants(d).OfType<PanelContainer>().Single().GetThemeStylebox("panel") is StyleBoxFlat dayChip && dayChip.BorderColor == ClassicDarkFrame.Line), "whisper: the day markers are Classic chips");
            var pills = Descendants(log).OfType<PanelContainer>().Where(p => p.Name == "whisper_reactions" && p.IsVisibleInTree()).ToArray();
            Require(pills.Length == 3 && pills.All(p => p.GetThemeStylebox("panel") is StyleBoxFlat { CornerRadiusTopLeft: 0 } box && box.BorderColor == ClassicDarkFrame.Line && threadBox.Position.X - .5f <= p.GetGlobalRect().Position.X && p.GetGlobalRect().End.X <= threadBox.End.X + .5f),
                "whisper: the three reacted posts carry the Classic reaction chip inside the log");
            var older = Native.Get<Button>(thread, "older")!;
            Require(older.Visible && older.Name == "whisper_older" && older.GetThemeFont("font") == Plugin.Kit.Bold && older.Size.Y == ClassicSocialLayout.ActionHeight, "whisper: 'Load older messages' is a Classic action");
            // The react button of a pinned line stays visible like the client's, on the line, inside the log.
            var gift = timeline.Find(207)!;
            var giftRow = (Control)posts.First(e => e.Value == gift).Key;
            thread.Pinned = gift; thread.ShowReactButton(gift); await Frames(2);
            thread.EnsureControlVisible(giftRow); await Frames(4);
            var react = Descendants(giftRow).OfType<Button>().Single(b => b.Name == "whisper_react");
            Require(react.IsVisibleInTree() && !react.Flat && react.GetThemeStylebox("hover") is StyleBoxFlat && react.Size == new Vector2(ClassicWhisperLayout.ReactCell, ClassicWhisperLayout.ReactCell) && threadBox.Encloses(react.GetGlobalRect()) && react.GetThemeColor("icon_normal_color") == ClassicWhisperThread.ActionInk,
                "whisper: a line's react button is the Classic title-bar icon inside the log " + react.GetGlobalRect());
            var giftText = Descendants(giftRow).OfType<RichTextLabel>().Single().GetGlobalRect();
            Require(Math.Abs(giftText.Position.X - react.GetGlobalRect().End.X) <= ClassicWhisperLayout.LineGap + 1 && Math.Abs(giftText.End.X - threadBox.End.X) <= 1,
                "whisper: an own line ends at the log's right edge with its react button right beside the text " + giftText);
            await Shot("whisper", window);
            thread.Unpin();
            // The top of the history: the Classic "Load older messages", a day marker and the partner's voice note.
            thread.ScrollVertical = 0; await Frames(4);
            Require(older.IsVisibleInTree() && threadBox.Encloses(older.GetGlobalRect()) && Math.Abs(older.GetGlobalRect().GetCenter().X - threadBox.GetCenter().X) <= 1,
                "whisper: at the top of the history 'Load older messages' sits centred in the log " + older.GetGlobalRect());
            await Shot("whisper-history", window);
            // The voice notes: the partner's (with a reaction) and the own one, each a Classic play line.
            var partnerNote = (Control)posts.First(e => e.Value.Id == 204).Key;
            thread.ScrollVertical = (int)(partnerNote.GlobalPosition.Y - ((Control)thread.GetChild(0)).GlobalPosition.Y - ClassicWhisperLayout.LinePitch); await Frames(4);
            Require(threadBox.Encloses(Descendants(partnerNote).OfType<Button>().Single(b => b.Name == "whisper_note_play").GetGlobalRect()), "whisper: the partner's voice note shows its play cell inside the log");
            await Shot("whisper-notes", window);
            thread.ScrollToEnd(); await Frames(2);
            // A reaction refresh and a new line reuse and restyle the client's rows.
            var plain = timeline.Find(205)!;
            var plainRow = (Control)posts.First(e => e.Value == plain).Key;
            plain.Reactions.Set(PreviewPartner(timeline, self), "Rikka", "\u2764");
            thread.Refresh(plain); await Frames(2);
            var plainPill = Descendants(plainRow).OfType<PanelContainer>().Single(p => p.Name == "whisper_reactions");
            Require(plainRow.GetParent() == log && plainPill.IsVisibleInTree() && plainPill.GetThemeStylebox("panel") is StyleBoxFlat { CornerRadiusTopLeft: 0 }, "whisper: a reaction refresh keeps the row and shows the Classic chip");
            var chats = (System.Collections.IDictionary)DetailField(world, "Whispers.Windows")!;
            DetailCall(world, "Whispers.AddWhisperPost", chats["Rikka"], LibreKO.Domain.RoomPost.Line(PreviewPartner(timeline, self), "Rikka", "on my way", DateTime.Now)); await Frames(3);
            var fresh = (Control)log.GetChildren().Last();
            Require(fresh.GetMeta(ClassicWhisperThread.RowMeta, "").AsString() == "line" && Descendants(fresh).OfType<RichTextLabel>().Single().Text.Contains("[color=#" + ClassicWhisperThread.Partner.ToHtml(false) + "]"), "whisper: a new line from the partner arrives as a cyan Classic line");
            // A size or transparency change re-renders the whole thread (Reset and Render) and resizes the window.
            DetailCall(world, "Whispers.OnWhisperLookChanged"); await Frames(3);
            Require(log.GetChildCount() > 0 && log.GetChildren().All(r => r.HasMeta(ClassicWhisperThread.RowMeta)) && thread.GetRect() == logArea && window.Size == open.SizeVec,
                "whisper: after a look change the re-rendered rows are Classic and the frame keeps its log area " + thread.GetRect() + " " + window.Size);

            // Composer: the field in the original edit field, the record cell beside it, Send through the original cell.
            var edit = new Rect2(open.Find("edit_chat")!.Position, open.Find("edit_chat")!.SizeVec);
            var field = (Control)input.GetParent();
            Require(field.GetParent() == expanded && field.GetRect() == edit && input.GetRect() == new Rect2(Vector2.Zero, edit.Size), "whisper: the composer's field fills the original edit field " + field.GetRect());
            var record = Descendants(expanded).OfType<Button>().Single(b => b.Name == "whisper_record");
            var scrollArt = new Rect2(open.Find("scroll")!.Position, open.Find("scroll")!.SizeVec);
            Require(record.IsVisibleInTree() && record.GetRect() == ClassicWhisperLayout.Record && record.HasMeta(ClassicChatRoomSkin.CellMeta) && !edit.Intersects(record.GetRect()) && !scrollArt.Intersects(record.GetRect()),
                "whisper: the record button sits in its own Classic cell at the end of the edit band " + record.GetRect());
            var classicSend = Descendants(surface).OfType<TextureButton>().Single(b => b.Name == "btn_chat");
            string? sent = null; int olderRequests = 0;
            composer.Submitted += wire => sent = wire;
            thread.OlderRequested += () => olderRequests++;
            input.GrabFocus(); input.Text = "see you at the gate";
            classicSend.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
            Require(sent == "see you at the gate" && olderRequests == 0, "whisper: the original send cell runs the composer's Send, not 'Load older messages'");
            Require(input.Text == "" && !input.HasFocus() && NativeWhispers.ComposeTarget(world) == "Rikka", "whisper: after sending, the input is left and the conversation stays addressed");
            sent = null; input.GrabFocus(); input.Text = "omw";
            input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text); await Frames(3);
            Require(sent == "omw" && input.Text == "" && !input.HasFocus(), "whisper: Enter sends through the composer and leaves the input");
            older.EmitSignal(BaseButton.SignalName.Pressed); await Frames(1);
            Require(olderRequests == 1, "whisper: the Classic 'Load older messages' asks the client for older history");

            // The composer's emoticon picker on the Classic menu panel.
            var emoticons = input.GetChildren().OfType<TextureButton>().Single();
            var (inputBox, faceBox) = (input.GetGlobalRect(), emoticons.GetGlobalRect());
            Require(emoticons.Name == "whisper_emoticons" && faceBox.Size == new Vector2(ClassicWhisperLayout.EmoticonSize, ClassicWhisperLayout.EmoticonSize) && inputBox.Encloses(faceBox)
                && Math.Abs(inputBox.End.X - ClassicWhisperLayout.EmoticonInset - faceBox.End.X) <= .5f && faceBox.Position.Y == record.GetGlobalRect().Position.Y && faceBox.End.Y == record.GetGlobalRect().End.Y,
                "whisper: the emoticon button sits inside the right end of the input on the band of the send and record cells " + faceBox);
            // The client creates the picker on the first press; it counts as opened when it pops up (another audit
            // process can take the focus and close an embedded popup, so it is shown again in place for the capture).
            bool popped = false;
            composer.ChildEnteredTree += node => { if (node is EmoticonPicker added) added.AboutToPopup += () => popped = true; };
            emoticons.EmitSignal(BaseButton.SignalName.Pressed); await Frames(4);
            var picker = Descendants(this).OfType<EmoticonPicker>().Single();
            Require(popped && picker.GetParent() == composer && picker.HasMeta("classic_popup") && picker.HasMeta(ClassicReactionStyle.StyledMeta) && picker.GetThemeStylebox("panel") is StyleBoxFlat, "whisper: the composer's emoticon picker opens on the Classic menu panel, in the room picker's look");
            if (!picker.Visible) { picker.Popup(); await Frames(2); }
            await Shot("whisper-emoticons", window);
            picker.Hide();
            DetailCall(world, "Whispers.BuildWhisperOptions"); DetailCall(world, "Whispers.RefreshWhisperOptions");
            var options = (PopupPanel)DetailField(world, "Whispers.whisperOptions")!;
            options.Position = (Vector2I)(window.GlobalPosition + new Vector2(150, 30)); options.Popup(); await Frames(4);
            Require(options.HasMeta("classic_popup"), "whisper: the size and transparency popup uses the Classic menu panel");
            await Shot("whisper-options", window);
            options.Hide();

            // Recording a voice note: the strip takes the edit band, the record cell sends the note, Send is off.
            composer.ShowRecording(7, .55f); await Frames(3);
            var strip = Descendants(expanded).OfType<HBoxContainer>().Single(b => b.Name == "whisper_record_strip");
            Require(strip.IsVisibleInTree() && !input.IsVisibleInTree() && strip.GetRect() == ClassicWhisperLayout.Strip, "whisper: while a note records the strip replaces the input " + strip.GetRect());
            Require(strip.GetChildren().OfType<Control>().Where(c => c.Visible).All(c => new Rect2(Vector2.Zero, strip.Size).Encloses(c.GetRect())) && strip.GetChildren().OfType<Control>().Count(c => c.Visible) == 4,
                "whisper: the dot, clock, level and cancel fit the strip " + string.Join(",", strip.GetChildren().OfType<Control>().Select(c => c.GetRect().ToString())));
            Require(classicSend.Disabled && classicSend.SelfModulate == ClassicWhisperLayout.DisabledTint && record.IsVisibleInTree(), "whisper: while a note records the record cell stays and the send cell is off, in the Classic disabled tint");
            await Shot("whisper-recording", window);
            composer.RefreshRecording(); await Frames(3);
            Require(!strip.IsVisibleInTree() && input.IsVisibleInTree() && !classicSend.Disabled && classicSend.SelfModulate == Colors.White, "whisper: after recording the input and the send cell return");

            // Minimize and restore: the client hides its header icons while minimized; restored they are back on the bar.
            window.SetMinimized(true); await Frames(3);
            Require(actions.All(a => !a.IsVisibleInTree()) && Descendants(surface).OfType<Label>().Any(l => l.Text == "Rikka" && l.IsVisibleInTree()), "whisper: minimized, only the compact bar shows");
            await Shot("whisper-minimized", window);
            window.SetMinimized(false); await Frames(3);
            Require(actions.All(a => a.Visible && a.IsVisibleInTree() && new Rect2(0, 0, 304, 24).Encloses(a.GetRect())), "whisper: restored, the header actions are back on the title bar");

            // An incoming voice note from a new partner opens a minimized conversation (the client's ShowIncomingWhisper,
            // which needs the world's viewport) that blinks in the Classic bar.
            var note = new LibreKO.Domain.RoomPost(301, LibreKO.Domain.RoomPostKind.VoiceNote, 4242, "Valka", DateTime.Now, "", 52, 12000) { Waveform = Enumerable.Range(0, 48).Select(i => (byte)(40 + (i * 37) % 180)).ToArray() };
            Game.Session.RoomMedia.DirectTimeline("Valka").Add(note);
            DetailCall(world, "Whispers.GetOrCreateWhisper", "Valka", true);
            DetailCall(world, "Whispers.OnDirectPosted", "Valka", note);
            var incoming = Native.Get<HudWindow>(chats["Valka"]!, "Window")!;
            NativeWhispers.Prepare(incoming, world); await Frames(4);
            var compact = Descendants(incoming).OfType<Label>().Single(l => l.Text == "Valka" && l.IsVisibleInTree());
            var nativeBlink = Native.Get<Tween>(chats["Valka"]!, "Blink");
            Require(incoming.Minimized && incoming.Size == Plugin.Kit.Layout("co_whisper_close_us").SizeVec && (nativeBlink == null || !nativeBlink.IsValid()),
                "whisper: an incoming voice note opens a minimized Classic conversation without the native fade " + incoming.Size);
            await Frames(40);
            Require(compact.GetThemeColor("font_color") == new Color("ffff00") || compact.GetThemeColor("font_color") == new Color("00ffff"), "whisper: the minimized conversation shows the Classic attention");
            await Shot("whisper-incoming-note", incoming);
            layer.QueueFree(); world.QueueFree();
        }
    }

    /// <summary>The preview partner's character id, read from the seeded timeline.</summary>
    private static int PreviewPartner(LibreKO.Domain.RoomTimeline timeline, int self) => timeline.Posts.First(p => p.SenderId != 0 && p.SenderId != self).SenderId;
}
