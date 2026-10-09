using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureMailClassicAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool value, string text) { if (!value) throw new Exception("MAIL_CLASSIC_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Capture(HudWindow window, string state)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            var panel = window.GetChildren().OfType<ClassicMailPanel>().Single();
            Require(window.Size == panel.CustomMinimumSize, state + " fixed frame size");
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete window fits viewport");
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("mail_expected_rect")))
            {
                Require(control.GetRect() == control.GetMeta("mail_expected_rect").AsRect2(), state + " measured bounds " + control.Name + " " + control.GetRect());
                Require(new Rect2(Vector2.Zero, panel.Size).Encloses(control.GetRect()), state + " content stays inside frame " + control.Name);
            }
            foreach (var field in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("mail_row_expected_rect")))
            {
                Require(field.GetRect() == field.GetMeta("mail_row_expected_rect").AsRect2(), state + " rendered row column bounds " + field.Name);
                Require(field.ClipContents, state + " row column clips long text " + field.Name);
            }
            foreach (var edit in Descendants(panel).OfType<LineEdit>()) Require(edit.TextDirection == Control.TextDirection.Ltr, state + " typing direction " + edit.Name);
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }
        foreach (string view in new[] { "inbox", "empty", "read", "store", "compose" })
        {
            var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildMailClassicUiPreview", view)!; AddChild(layer);
            DetailCall(world, "BuildItemTooltip"); var tooltipLayer = (CanvasLayer)DetailField(world, "_itemTipLayer")!;
            world.RemoveChild(tooltipLayer); AddChild(tooltipLayer);
            foreach (var win in layer.GetChildren().OfType<HudWindow>()) ClassicMailSkin.Apply(win.Body);
            await Frames();
            var window = layer.GetChildren().OfType<HudWindow>().Single(w => w.Visible);
            var panel = window.GetChildren().OfType<ClassicMailPanel>().Single();
            window.Position = new Vector2(50, 30); await Frames(2);
            var grip = Descendants(panel).OfType<Control>().Single(c => c.Name == "party_drag");
            var start = window.Position;
            grip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = start + new Vector2(30, 15) });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = start + new Vector2(63, 36) });
            window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Require(window.Position == start + new Vector2(33, 21), view + " original header forwards native dragging");
            await Capture(window, view);
            if (view == "inbox")
            {
                var rows = (VBoxContainer)DetailField(world, "_mailList")!;
                Require(rows.GetChildren().OfType<PanelContainer>().Count() == 28, "Inbox includes player, event and store deliveries");
                Require(rows.GetChildren().OfType<PanelContainer>().All(r => r.Size.Y == 26), "Inbox row height is uniform");
                Require(((ScrollContainer)DetailField(world, "_mailListScroll")!).GetVScrollBar().Visible, "Inbox scrolls within its fixed table");
                var filter = (CheckButton)DetailField(world, "_mailUnreadOnly")!; filter.ButtonPressed = true; await Frames();
                Require(rows.GetChildren().OfType<PanelContainer>().All(r => !r.GetMeta("mail_read").AsBool()), "Unread filter preserves native list semantics");
                await Capture(window, "unread");
                var firstRow = rows.GetChildren().OfType<PanelContainer>().First(); var point = firstRow.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point }); await Frames();
                var reader = layer.GetChildren().OfType<HudWindow>().Single(w => w.Id == "mailread");
                Require(reader.Visible && (int)DetailField(world, "_mailSelectedId")! == 3, "Actual left-click selection opens the native reading window");
                reader.Visible = false; await Capture(window, "selected"); reader.Visible = true;
                DetailCall(world, "OnMailRead", 3, true, "Read successfully."); await Frames();
                Require(rows.GetChildren().OfType<PanelContainer>().All(r => !r.GetMeta("mail_selected").AsBool()), "Read response removes the selected row from unread-only inbox");
            }
            else if (view == "empty") Require(((VBoxContainer)DetailField(world, "_mailList")!).GetChildren().OfType<Label>().Single().Text == "Your mailbox is empty.", "Empty mailbox remains readable");
            else if (view is "read" or "store")
            {
                var text = (Label)DetailField(world, "_mailReadBody")!;
                Require(text.GetParent() is ScrollContainer, "Read body is contained in a scroll viewport");
                if (view == "read") Require(((ScrollContainer)text.GetParent()).GetVScrollBar().Visible, "Long read body scrolls without changing frame size");
                else Require(((ScrollContainer)DetailField(world, "_mailReadAttachmentScroll")!).GetVScrollBar().Visible, "Store attachments remain accessible inside the fixed attachment area");
                var mails = (List<MailEntry>)DetailField(world, "_mails")!;
                int id = (int)DetailField(world, "_mailSelectedId")!; var selected = mails.Single(m => m.Id == id);
                Require(((Button)DetailField(world, "_mailDeleteBtn")!).Disabled, "Pending attachments prevent deletion");
                Require(((Button)DetailField(world, "_mailClaimBtn")!).Visible, "Pending attachments offer claim all");
                var claimRows = (VBoxContainer)DetailField(world, "_mailReadAttachments")!;
                var single = claimRows.GetChildren().OfType<PanelContainer>().First();
                var remainingBefore = selected.Items.Select(i => i.Remaining).ToArray();
                single.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                Require(selected.Items.Select(i => i.Remaining).SequenceEqual(remainingBefore), "Claim clicks wait for server acknowledgement before marking attachments received");
                selected.Attachments = MailAttachmentState.Claimed; DetailCall(world, "OnMailList", mails); await Frames();
                Require(!((Button)DetailField(world, "_mailClaimBtn")!).Visible && !((Button)DetailField(world, "_mailDeleteBtn")!).Disabled, "Claimed response removes claim action and enables deletion");
                await Capture(window, view + "-claimed");
                panel._UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                Require(!window.Visible && (int)DetailField(world, "_mailSelectedId")! == -1, "Read Escape closes through the native header callback");
            }
            else
            {
                var to = (LineEdit)DetailField(world, "_mailTo")!; var subject = (LineEdit)DetailField(world, "_mailSubject")!; var body = (TextEdit)DetailField(world, "_mailBody")!;
                Require(((Label)DetailField(world, "_mailBodyRemaining")!).Text == (Net.MailBodyMax - body.Text.Length).ToString(), "Initial draft remaining count matches its native body text");
                to.EmitSignal(LineEdit.SignalName.TextSubmitted, to.Text); Require(subject.HasFocus(), "Recipient Enter moves to subject");
                subject.EmitSignal(LineEdit.SignalName.TextSubmitted, subject.Text); Require(body.HasFocus(), "Subject Enter moves to message without sending");
                Require(!((Button)DetailField(world, "_mailSendBtn")!).Disabled, "Entering message does not submit mail");
                to.GrabFocus(); to.Text = ""; DetailCall(world, "RefreshMailRecipientSuggestions"); await Frames();
                await Capture(window, "suggestions");
                var suggestions = (VBoxContainer)DetailField(world, "_mailToSuggest")!;
                Require(suggestions.GetChildren().Count == 8, "Recipient suggestions retain native eight-contact cap");
                suggestions.GetChildren().OfType<Button>().First().EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Require(!suggestions.Visible && subject.HasFocus(), "Choosing a contact fills recipient and advances focus");
                body.Text = new string('a', Net.MailBodyMax + 20); DetailCall(world, "OnMailBodyChanged");
                Require(body.Text.Length == Net.MailBodyMax && ((Label)DetailField(world, "_mailBodyRemaining")!).Text == "0", "Message limit and remaining count stay authoritative");
                body.Text = "A short letter."; DetailCall(world, "OnMailBodyChanged");
                var picks = (List<(int Slot, int Count)>)DetailField(world, "_mailAttachments")!;
                var rows = (VBoxContainer)DetailField(world, "_mailAttachRows")!;
                var spin = Descendants(rows).OfType<SpinBox>().Single(); spin.Value = 7;
                Require(picks[0].Count == 7, "Attachment quantity adjusts the native outgoing item count");
                spin.GetLineEdit().GrabFocus(); spin.GetLineEdit().Text = "8";
                GetViewport().PushInput(new InputEventKey { Keycode = Key.Enter, Pressed = true });
                GetViewport().PushInput(new InputEventKey { Keycode = Key.Enter, Pressed = false }); await Frames();
                Require(picks[0].Count == 8 && !((Button)DetailField(world, "_mailSendBtn")!).Disabled, "Attachment amount Enter applies the number without sending the letter");
                var drop = (Control)DetailField(world, "_mailDropZone")!;
                var data = new Godot.Collections.Dictionary { { "invFrom", Inventory.GridStart + 2 } };
                Require(drop._CanDropData(Vector2.Zero, data), "Valid inventory drag is accepted by native drop zone"); drop._DropData(Vector2.Zero, data); await Frames();
                Require(((PanelContainer)drop).GetThemeStylebox("panel") == drop.GetMeta("classic_mail_idle").AsGodotObject(), "Drop completion restores the original Classic inset material");
                Require(picks.Count == 3 && picks.Last().Count == 5, "Native drop preserves the full stack count");
                Require(!drop._CanDropData(Vector2.Zero, data), "Duplicate attachment drag is rejected");
                Require(!drop._CanDropData(Vector2.Zero, new Godot.Collections.Dictionary { { "invFrom", 0 } }), "Equipment slots cannot be mailed");
                await Capture(window, "attachments");
                var inv = (Inventory)typeof(World).GetProperty("Inv", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(world)!;
                inv[Inventory.GridStart + 3] = new ItemSlot { ItemId = 379154000, Count = 1, Durability = 1 };
                var fourth = new Godot.Collections.Dictionary { { "invFrom", Inventory.GridStart + 3 } };
                Require(drop._CanDropData(Vector2.Zero, fourth), "Fourth native attachment is available"); drop._DropData(Vector2.Zero, fourth); await Frames();
                Require(picks.Count == Net.MailItemAttachmentsMax && !drop._CanDropData(Vector2.Zero, new Godot.Collections.Dictionary { { "invFrom", Inventory.GridStart + 4 } }), "Full composer enforces native four-item capacity");
                Require(!Descendants(drop).OfType<ScrollContainer>().Single().GetVScrollBar().Visible, "All four outgoing attachment rows fit without scrolling");
                await Capture(window, "full-attachments");
                Descendants(rows.GetChildren().OfType<Control>().Last()).OfType<Button>().Single().EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Require(picks.Count == 3, "Remove action returns an attachment capacity slot without changing other picks");
                rows.GetChildren().OfType<Control>().First().EmitSignal(Control.SignalName.MouseEntered); await Frames();
                Require(world.ItemTooltipVisible, "Attachment hover opens the existing native item tooltip");
                var tip = (PanelContainer)DetailField(world, "_itemTipPanel")!;
                Require(Descendants(tip).OfType<Label>().Any(l => l.Text.Contains("Apples")), "Attachment tooltip describes the hovered native item");
                rows.GetChildren().OfType<Control>().First().EmitSignal(Control.SignalName.MouseExited); await Frames();
                Require(!world.ItemTooltipVisible, "Attachment mouse exit closes the native item tooltip");
                to.Text = ""; DetailCall(world, "SendComposedMail");
                Require(((Label)DetailField(world, "_mailComposeStatus")!).Text.Contains("recipient"), "Missing recipient retains native validation");
                to.Text = "Rikka"; ((MoneyEdit)DetailField(world, "_mailGold")!).Value = 100_001; DetailCall(world, "SendComposedMail");
                Require(((Label)DetailField(world, "_mailComposeStatus")!).Text.Contains("gold"), "Excess coin transfer retains native balance validation");
                await Capture(window, "invalid");
                ((MoneyEdit)DetailField(world, "_mailGold")!).Value = 100; DetailCall(world, "SendComposedMail");
                Require(((Button)DetailField(world, "_mailSendBtn")!).Disabled, "Native send waits for server acknowledgement");
                DetailCall(world, "OpenMailCompose");
                Require(((Button)DetailField(world, "_mailSendBtn")!).Disabled && (bool)DetailField(world, "_mailSending")!, "Opening compose again cannot bypass a pending send");
                await Capture(window, "sending");
                DetailCall(world, "SendComposedMail"); Require(((Button)DetailField(world, "_mailSendBtn")!).Disabled, "Repeated submit cannot duplicate a pending send");
                DetailCall(world, "OnMailSendResult", false, "Recipient does not exist.");
                Require(!((Button)DetailField(world, "_mailSendBtn")!).Disabled && picks.Count == 3, "Server rejection preserves editable draft and attached picks");
                await Capture(window, "send-refused");
                to.GrabFocus(); GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = false }); await Frames();
                Require(!window.Visible && picks.Count == 3 && body.Text == "A short letter." && to.Text == "Rikka", "Compose Escape hides the window and keeps the draft");
                DetailCall(world, "OpenMailCompose"); await Frames();
                Require(window.Visible && picks.Count == 3 && body.Text == "A short letter." && to.Text == "Rikka", "Reopened composer restores the kept draft");
                Descendants(panel).OfType<Button>().Single(b => b.Name == "mail_cancel").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Require(!window.Visible && picks.Count == 0 && body.Text == "" && to.Text == "", "Compose Cancel discards draft through native cancellation");
                DetailCall(world, "OpenMailCompose"); await Frames(); await Capture(window, "empty-compose");
                Require(((Label)DetailField(world, "_mailBodyRemaining")!).Text == "512", "Reopened composer starts with an empty native draft");
                to.Text = "Rikka"; subject.Text = "Kept through close";
                Descendants(panel).OfType<Button>().Single(b => b.TooltipText == "Close").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Require(!window.Visible && to.Text == "Rikka" && subject.Text == "Kept through close", "Header close hides compose and keeps the draft like Escape");
            }
            tooltipLayer.Free(); layer.Free(); world.Free();
        }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-mail.json", JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic mail audit: " + checks.Count + " checks / " + screens.Count + " screens"); offline.Free();
    }
}
