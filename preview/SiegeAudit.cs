using Godot;
using LibreKO;
using LibreKO.Network;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureSiegeAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        var checks = new List<string>(); var screens = new List<object>();
        string prefix = nation == 1 ? "karus" : "human";
        void Require(bool condition, string text) { if (!condition) throw new Exception("SIEGE_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        T Named<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().Single(c => c.Name == name);
        async Task Capture(HudWindow window, string id, string state, Vector2 size)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            string tag = id + " " + state;
            Require(window.Size == size, tag + " exact original window footprint " + window.Size);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), tag + " complete window fits viewport");
            var frame = new Rect2(Vector2.Zero, window.Size);
            foreach (var control in Descendants(window).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("siege_expected_rect")))
            {
                Require(control.GetRect() == control.GetMeta("siege_expected_rect").AsRect2(), tag + " measured bounds " + control.Name + " = " + control.GetRect());
                Require(frame.Encloses(control.GetRect()), tag + " inside original border " + control.Name);
                if (control is Label { AutowrapMode: TextServer.AutowrapMode.Off } label && label.Text.Length > 0)
                {
                    float width = label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X;
                    Require(width <= label.Size.X || label.TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming, tag + " caption contained " + control.Name);
                }
            }
            if (Descendants(window).OfType<ScrollContainer>().SingleOrDefault(c => c.Name == "siege_scroll") is { } scroll)
            {
                foreach (var head in Enumerable.Range(0, 3).Select(i => Named<Label>(window, "siege_head_" + i)))
                {
                    var font = head.GetThemeFont("font"); int px = head.GetThemeFontSize("font_size");
                    Require(font.GetStringSize(head.Text, HorizontalAlignment.Left, -1, px).X <= head.Size.X, tag + " heading " + head.Text + " fits at " + px + " px");
                }
                var list = scroll.GetGlobalRect();
                foreach (var row in Named<VBoxContainer>(window, "siege_rows").GetChildren().OfType<PanelContainer>())
                {
                    var rect = row.GetGlobalRect();
                    Require(Mathf.IsEqualApprox(rect.Size.X, list.Size.X) && Mathf.IsEqualApprox(rect.Position.X, list.Position.X), tag + " row spans the original list " + rect);
                    Require(rect.Size.Y == 17, tag + " row pitch 17");
                    var cells = Descendants(row).OfType<Label>().ToArray();
                    var heads = Enumerable.Range(0, 3).Select(i => Named<Label>(window, "siege_head_" + i)).ToArray();
                    for (int i = 0; i < cells.Length; i++)
                    {
                        var cell = cells[i].GetGlobalRect();
                        Require(cell.Position.X >= list.Position.X && cell.End.X <= list.End.X + .5f, tag + " cell inside list " + i);
                        // Each column lies under its heading; the Members heading also spans the header cell above the bar.
                        float centre = heads[i].GetGlobalRect().GetCenter().X;
                        Require(cell.Position.X <= centre && cell.End.X >= centre, tag + " cell under heading " + i);
                    }
                }
            }
            string file = prefix + "-" + id + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { window = id, state, file });
        }
        async Task Hover(Control control)
        {
            Vector2 hit = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = hit, GlobalPosition = hit }); await Frames(2);
        }
        async Task Leave() { GetViewport().PushInput(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero }); await Frames(2); }
        async Task Click(Control control)
        {
            Vector2 hit = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = hit, GlobalPosition = hit });
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.Left, Pressed = true });
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.Left, Pressed = false });
            await Frames(2);
        }
        async Task<(WorldRoot World, HudWindow Window, ClassicSiegePanel Panel)> Open(string view)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, "BuildSiegeWindowPreview", view)!;
            AddChild(window); var panel = ClassicSiegeSkin.Apply(window.Body)!;
            Require(panel != null, view + " Classic siege skin applied to the prepared native window");
            await Frames(); return (world, window, panel!);
        }
        async Task Close((WorldRoot World, HudWindow Window, ClassicSiegePanel Panel) open)
        {
            open.Window.QueueFree(); await Frames(2); open.World.Free(); await Frames(2);
        }
        int Notices(WorldRoot world) => world.GetChildren().OfType<Notice>().Count();
        bool Shown(WorldRoot world, string field) => (bool)DetailField(world, field)!;
        bool Connected(Button button) => button.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0;

        // NPC talk windows.
        var speechSize = new Vector2(279, 306);
        foreach (var (view, id, field) in new[] { ("siege-guard", "siegeguard", "Siege.guardShown"), ("siege-office", "siegeoffice", "Siege.officeShown"), ("siege-taxlist", "siegetaxlist", "Siege.taxListShown") })
        {
            var open = await Open(view);
            var options = Enumerable.Range(0, 4).Select(i => Named<Button>(open.Panel, "siege_option_" + i)).ToArray();
            Require(options.All(o => o.Text.Length > 0 && Connected(o)), id + " four native menu options keep their callbacks");
            Require(Named<Label>(open.Panel, "siege_greeting").Text.Length > 0 && Named<Label>(open.Panel, "siege_question").Text.Length > 0, id + " native speech lines shown");
            await Capture(open.Window, id, "ready", speechSize);
            await Hover(options[1]);
            Require(options[1].GetDrawMode() == BaseButton.DrawMode.Hover, id + " menu option receives actual mouse hover");
            await Capture(open.Window, id, "hover", speechSize);
            await Leave();
            if (id == "siegeoffice")
            {
                int before = Notices(open.World); options[1].EmitSignal(BaseButton.SignalName.Pressed);
                Require(Notices(open.World) == before + 1, "Collect tax opens the native collection confirmation");
                options[2].EmitSignal(BaseButton.SignalName.Pressed);
                Require(!open.Window.Visible && !Shown(open.World, field), "Castle maintenance keeps its native close");
            }
            if (id == "siegetaxlist")
            {
                foreach (int i in new[] { 0, 1, 2 })
                {
                    int before = Notices(open.World); options[i].EmitSignal(BaseButton.SignalName.Pressed);
                    Require(Notices(open.World) == before + 1, "Tax option " + i + " opens the native rate confirmation");
                }
            }
            if (id == "siegeguard")
            {
                options[0].EmitSignal(BaseButton.SignalName.Pressed);
                Require(!open.Window.Visible && !Shown(open.World, field), "Challenger closes the guard and requests the native list");
                DetailCall(open.World, "Siege.OpenGuard"); await Frames(2);
            }
            if (id == "siegeoffice") { DetailCall(open.World, "Siege.OpenOffice", new SiegeOffice(4_250_000, 0)); await Frames(2); }
            Require(open.Window.Visible && Shown(open.World, field), id + " open before closing through the last option");
            await Click(options[3]);
            Require(!open.Window.Visible && !Shown(open.World, field), id + " last option mouse click invokes the native close");
            await Close(open);
        }
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, "BuildSiegeDragPreview")!;
            AddChild(window); ClassicSiegeSkin.Apply(window.Body); await Frames();
            window.Position = new Vector2(100, 80); Vector2 origin = window.Position, pointer = origin + new Vector2(200, 8);
            Named<Control>(window, "party_drag").EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = pointer });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = pointer + new Vector2(30, 15) });
            window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, GlobalPosition = pointer + new Vector2(30, 15) });
            Require(window.Position == origin + new Vector2(30, 15), "Siege window artwork uses native dragging");
            window.QueueFree(); await Frames(2); world.Free();
        }

        // List windows.
        var tableSize = new Vector2(286, 366);
        async Task Scrolled(HudWindow window, string id, string state)
        {
            var rail = Named<ClassicSiegeScrollRail>(window, "siege_scroll_rail");
            await Frames(2);
            Require(rail.Bar.MaxValue > rail.Bar.Page, id + " long list overflows into the original scroll bar");
            Require(rail.Up.Disabled && !rail.Down.Disabled, id + " scroll arrows reflect the list top");
            await Capture(window, id, state, tableSize);
            var scroll = Named<ScrollContainer>(window, "siege_scroll"); Vector2 hit = scroll.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1 });
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.WheelDown, Pressed = false, Factor = 1 });
            await Frames(2);
            Require(rail.Bar.Value > 0, id + " mouse wheel scrolls the contained list");
            for (int i = 0; i < 40; i++) rail.Down.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Require(rail.Down.Disabled && !rail.Up.Disabled, id + " down arrow reaches the list end");
            Require(Named<Button>(window, "siege_close").IsVisibleInTree(), id + " Close stays reachable with a long list");
            await Capture(window, id, state + "-scrolled", tableSize);
        }
        {
            var open = await Open("siege-schedule");
            Require(Named<VBoxContainer>(open.Window, "siege_rows").GetChildCount() == 3, "Schedule shows the native rows");
            await Capture(open.Window, "siegeschedule", "filled", tableSize);
            var ok = Named<Button>(open.Panel, "siege_ok"); var again = Named<Button>(open.Panel, "siege_refresh");
            await Hover(again); Require(again.GetDrawMode() == BaseButton.DrawMode.Hover, "Schedule Read again receives actual hover");
            await Capture(open.Window, "siegeschedule", "hover", tableSize); await Leave();
            Require(Connected(again), "Schedule Read again keeps the native request");
            DetailCall(open.World, "FillSiegeLongSchedule", false); await Scrolled(open.Window, "siegeschedule", "long");
            DetailCall(open.World, "FillSiegeLongSchedule", true); await Frames();
            Require(Named<VBoxContainer>(open.Window, "siege_rows").GetChildCount() == 0, "Schedule empty reply clears the list");
            await Capture(open.Window, "siegeschedule", "empty", tableSize);
            await Click(ok);
            Require(!open.Window.Visible && !Shown(open.World, "Siege.scheduleShown"), "Schedule OK mouse click invokes the native close");
            await Close(open);
        }
        {
            var open = await Open("siege-challengers");
            var apply = Named<Button>(open.Panel, "siege_apply"); var cancel = Named<Button>(open.Panel, "siege_apply_cancel");
            var defenders = Named<Button>(open.Panel, "siege_defenders");
            Require(apply.Visible && !cancel.Visible, "Challengers offers Application when the clan has not applied");
            foreach (string note in new[] { "siege_signed_up", "siege_chosen", "siege_fee", "siege_period" })
            {
                var label = Named<Label>(open.Panel, note);
                Require(label.Text.Length > 0, "Challenger note " + note + " shows native text");
                Require(label.GetVisibleLineCount() == label.GetLineCount(), "Challenger note " + note + " fits its original line: " + label.Text + " " + label.GetVisibleLineCount() + "/" + label.GetLineCount());
            }
            Require(Connected(apply) && Connected(defenders), "Application and View Defending Confederacy keep their native requests");
            await Capture(open.Window, "siegechallengers", "filled", tableSize);
            await Hover(defenders); Require(defenders.GetDrawMode() == BaseButton.DrawMode.Hover, "View Defending Confederacy receives actual hover");
            await Capture(open.Window, "siegechallengers", "hover", tableSize); await Leave();
            DetailCall(open.World, "FillSiegeLongChallengers", true); await Frames();
            Require(!apply.Visible && cancel.Visible, "Own clan in the list switches to the native Cancel action");
            var longCell = Descendants(Named<VBoxContainer>(open.Window, "siege_rows")).OfType<Label>().First(l => l.Text.StartsWith("Brotherhood"));
            Require(longCell.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && longCell.TooltipText == longCell.Text && longCell.Size.X == 163, "Long clan name is trimmed in its column and kept as tooltip");
            await Scrolled(open.Window, "siegechallengers", "applied-long");
            int before = Notices(open.World); await Click(cancel);
            Require(Notices(open.World) == before + 1, "Cancel mouse click opens the native withdrawal confirmation");
            await Click(Named<Button>(open.Panel, "siege_close"));
            Require(!open.Window.Visible && !Shown(open.World, "Siege.challengersShown"), "Challengers Close invokes the native close");
            await Close(open);
        }
        {
            var open = await Open("siege-challengers-applied");
            Require(!Named<Button>(open.Panel, "siege_apply").Visible && Named<Button>(open.Panel, "siege_apply_cancel").Visible, "Applied fixture shows Cancel");
            await Capture(open.Window, "siegechallengers", "applied", tableSize);
            await Close(open);
        }
        {
            var open = await Open("siege-defenders");
            await Capture(open.Window, "siegedefenders", "filled", tableSize);
            var list = Named<Button>(open.Panel, "siege_challengers"); var again = Named<Button>(open.Panel, "siege_refresh");
            Require(Connected(list) && Connected(again), "Challenge List and Read Again keep their native requests");
            await Hover(list); Require(list.GetDrawMode() == BaseButton.DrawMode.Hover, "Challenge List receives actual hover");
            await Capture(open.Window, "siegedefenders", "hover", tableSize); await Leave();
            DetailCall(open.World, "FillSiegeLongDefenders", false); await Scrolled(open.Window, "siegedefenders", "long");
            DetailCall(open.World, "FillSiegeLongDefenders", true); await Frames();
            await Capture(open.Window, "siegedefenders", "empty", tableSize);
            var close = Named<Button>(open.Panel, "siege_close");
            await Hover(close); await Capture(open.Window, "siegedefenders", "close-hover", tableSize);
            await Click(close);
            Require(!open.Window.Visible && !Shown(open.World, "Siege.defendersShown"), "Defenders Close invokes the native close");
            await Close(open);
        }
        System.IO.File.WriteAllText(output + "/" + prefix + "-siege.json",
            JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        offline.Free(); GD.Print("SIEGE_AUDIT_OK " + checks.Count);
    }
}
