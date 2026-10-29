using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureMapFortuneAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        var checks = new List<string>(); var screens = new List<object>();
        string prefix = nation == 1 ? "karus" : "human";
        void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string text) { if (!condition) throw new Exception("MAP_FORTUNE_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        T Named<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().Single(c => c.Name == name);
        Rect2 Within(Control panel, Control control) => new(control.GetGlobalRect().Position - panel.GetGlobalRect().Position, control.GetGlobalRect().Size);
        async Task Capture(HudWindow window, string id, string state, Vector2 size, Control? lifted = null)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            string tag = id + " " + state;
            var panel = Named<Control>(window, "classic_map_fortune");
            Require(window.Size == size, tag + " exact Classic window footprint " + window.Size);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), tag + " complete window fits viewport");
            var frame = new Rect2(Vector2.Zero, window.Size);
            foreach (var control in Descendants(window).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta(ClassicMapFortuneSkin.ExpectedRect)))
            {
                var expected = control.GetMeta(ClassicMapFortuneSkin.ExpectedRect).AsRect2();
                if (control == lifted) expected.Position -= new Vector2(0, 16);
                var actual = Within(panel, control);
                Require(actual == expected, tag + " measured bounds " + control.Name + " = " + actual);
                Require(frame.Encloses(actual), tag + " inside the frame " + control.Name);
                if (control is Label { AutowrapMode: TextServer.AutowrapMode.Off } label && label.Text.Length > 0)
                {
                    float width = label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X;
                    Require(width <= label.Size.X, tag + " caption fits " + control.Name + " " + width + "/" + label.Size.X);
                }
            }
            string file = prefix + "-" + id + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { window = id, state, file });
        }
        async Task Hover(Control control, Vector2? at = null)
        {
            Vector2 hit = at ?? control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = hit, GlobalPosition = hit }); await Frames(2);
        }
        async Task Leave() { GetViewport().PushInput(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero }); await Frames(2); }
        async Task Click(Control control, Vector2? at = null)
        {
            Vector2 hit = at ?? control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = hit, GlobalPosition = hit });
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.Left, Pressed = true });
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.Left, Pressed = false });
            await Frames(2);
        }
        async Task Drag(HudWindow window, string what)
        {
            window.Position = new Vector2(100, 80); Vector2 origin = window.Position, pointer = origin + new Vector2(120, 12);
            Named<Control>(window, "party_drag").EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = pointer });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = pointer + new Vector2(30, 15) });
            window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, GlobalPosition = pointer + new Vector2(30, 15) });
            Require(window.Position == origin + new Vector2(30, 15), what + " title band uses native dragging");
            await Frames(2);
        }
        async Task Escape(WorldRoot world, HudWindow window, string field, string what)
        {
            Require(window.Visible && (bool)DetailField(world, field)!, what + " open before Escape");
            // The native escape stack closes its first open entry. Entries for systems a preview world never
            // builds cannot be queried; skipping them leaves the stack order intact for everything else.
            DetailCall(world, "BuildEscapeOrder");
            foreach (var (isOpen, close, _) in PreviewFixtures.EscapeEntries(world))
            {
                bool open;
                try { open = isOpen(); } catch (NullReferenceException) { continue; }
                if (open) { close(); break; }
            }
            await Frames(2);
            Require(!window.Visible && !(bool)DetailField(world, field)!, what + " Escape invokes the native close");
        }
        bool Connected(BaseButton button) => button.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0;

        // World Map.
        {
            var size = ClassicMapFortuneLayout.Map.Size;
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, "BuildGlobalMapPreview")!;
            AddChild(window); var panel = (ClassicMapPanel)ClassicMapFortuneSkin.Apply(window.Body)!;
            Require(panel != null, "World Map Classic skin applied to the prepared native window");
            await Frames();
            var rows = Named<VBoxContainer>(window, "map_rows"); var total = Named<Label>(panel, "map_total");
            var close = panel.CloseButton; var rail = panel.Rail;
            Require(window.Visible && (bool)DetailField(world, "GlobalMap.shown")!, "World Map opens through the native toggle");
            Require(rows.GetChildCount() == 0 && total.Text.Length == 0, "World Map waits for the native zone reply");
            Require(Connected(close), "World Map close keeps the native handler");
            await Capture(window, "globalmap", "loading", size);
            DetailCall(world, "FillGlobalMap", "empty"); await Frames();
            var notice = rows.GetChildren().OfType<Label>().Single(l => !l.IsQueuedForDeletion());
            Require(notice.Visible && notice.Text == "No zones available." && total.Text.Length == 0, "World Map empty reply shows the native notice without a total");
            Require(notice.GetGlobalRect().Size.Y == ClassicMapFortuneLayout.Map.List.Size.Y && notice.HorizontalAlignment == HorizontalAlignment.Center, "World Map empty notice is centred in the list");
            await Capture(window, "globalmap", "empty", size);
            void CheckRows(int expected)
            {
                var list = Named<ScrollContainer>(window, "map_scroll").GetGlobalRect();
                var zones = rows.GetChildren().OfType<PanelContainer>().Where(r => !r.IsQueuedForDeletion()).ToArray();
                Require(zones.Length == expected, "World Map shows " + expected + " native zone rows");
                var heads = Enumerable.Range(0, 3).Select(i => Named<Label>(window, "map_head_" + i)).ToArray();
                foreach (var head in heads)
                {
                    float width = head.GetThemeFont("font").GetStringSize(head.Text, HorizontalAlignment.Left, -1, head.GetThemeFontSize("font_size")).X;
                    Require(width + 4 <= head.Size.X, "World Map heading " + head.Text + " fits its column");
                }
                foreach (var row in zones)
                {
                    var rect = row.GetGlobalRect();
                    Require(rect.Size.X == list.Size.X && rect.Position.X == list.Position.X && rect.Size.Y == ClassicMapFortuneLayout.Row, "World Map row spans the list at the 22 px pitch " + rect);
                    var cells = Descendants(row).OfType<Label>().ToArray();
                    Require(cells.Length == 3, "World Map row keeps zone, players and nation");
                    for (int i = 0; i < cells.Length; i++)
                    {
                        var cell = cells[i].GetGlobalRect(); var head = heads[i].GetGlobalRect();
                        Require(Mathf.IsEqualApprox(cell.Position.X, head.Position.X) && Mathf.IsEqualApprox(cell.Size.X, head.Size.X), "World Map cell " + i + " lies exactly under its heading");
                        Require(cell.End.X <= list.End.X + .5f, "World Map cell " + i + " inside the list");
                    }
                    var name = cells[0];
                    float width = name.GetThemeFont("font").GetStringSize(name.Text, HorizontalAlignment.Left, -1, name.GetThemeFontSize("font_size")).X;
                    Require(width + 4 <= name.Size.X || (name.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && name.TooltipText == name.Text), "World Map zone name fits or trims with a tooltip: " + name.Text);
                    var ink = cells[2].GetThemeColor("font_color");
                    var nationInk = cells[2].Text switch { "Karus" => ClassicMapPanel.Karus, "El Morad" => ClassicMapPanel.ElMorad, _ => KnightOnlineUiClassic.ClassicDesign.Muted };
                    Require(ink == nationInk, "World Map nation ink for " + cells[2].Text);
                }
            }

            DetailCall(world, "FillGlobalMap", "filled"); await Frames();
            CheckRows(8);
            var footer = rows.GetChildren().OfType<Label>().Single(l => !l.IsQueuedForDeletion());
            Require(!footer.Visible && total.Text == footer.Text && total.Text.StartsWith("Total players online: "), "World Map total mirrors the native footer below the inset: " + total.Text);
            Require(rail.Up.Disabled && rail.Down.Disabled, "World Map short list leaves the scroll arrows idle");
            await Capture(window, "globalmap", "filled", size);
            await Hover(close); Require(close.GetDrawMode() == BaseButton.DrawMode.Hover, "World Map close receives actual mouse hover");
            await Capture(window, "globalmap", "close-hover", size); await Leave();

            DetailCall(world, "FillGlobalMap", "long"); await Frames();
            CheckRows(18);
            var trimmed = Descendants(rows).OfType<Label>().First(l => l.Text.StartsWith("Battle Zone"));
            Require(trimmed.TooltipText == trimmed.Text && trimmed.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis, "World Map long zone name is trimmed with a tooltip");
            Require(rail.Bar.MaxValue > rail.Bar.Page && rail.Up.Disabled && !rail.Down.Disabled, "World Map long list overflows into the scroll bar");
            await Capture(window, "globalmap", "long", size);
            var scroll = Named<ScrollContainer>(window, "map_scroll"); Vector2 hit = scroll.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1 });
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.WheelDown, Pressed = false, Factor = 1 });
            await Frames(4);
            Require(rail.Bar.Value > 0 && rail.Bar.Value % ClassicMapFortuneLayout.Row == 0, "World Map mouse wheel scrolls the contained list by whole rows " + rail.Bar.Value);
            for (int i = 0; i < 20; i++) rail.Down.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(4);
            Require(rail.Down.Disabled && !rail.Up.Disabled, "World Map down arrow reaches the list end");
            Require(close.IsVisibleInTree() && total.IsVisibleInTree(), "World Map close and total stay reachable with a long list");
            await Capture(window, "globalmap", "long-scrolled", size);

            // The client counts rows it has queued for deletion, so a later empty reply keeps a zero total.
            DetailCall(world, "FillGlobalMap", "empty"); await Frames();
            Require(rows.GetChildren().OfType<PanelContainer>().All(r => r.IsQueuedForDeletion()) && total.Text == "Total players online: 0", "World Map later empty reply clears the rows and keeps the native zero total");
            await Capture(window, "globalmap", "cleared", size);

            DetailCall(world, "FillGlobalMap", "filled"); await Frames();
            CheckRows(8);
            await Click(close);
            Require(!window.Visible && !(bool)DetailField(world, "GlobalMap.shown")!, "World Map close mouse click invokes the native close");
            DetailCall(world, "GlobalMap.Toggle"); await Frames(2);
            Require(window.Visible, "World Map toggles open again natively");
            await Escape(world, window, "GlobalMap.shown", "World Map");
            DetailCall(world, "GlobalMap.Toggle"); await Frames(2);
            await Drag(window, "World Map");
            DetailCall(world, "GlobalMap.Dispose");
            window.QueueFree(); await Frames(2); world.Free(); await Frames(2);
        }

        // Fortune Teller.
        {
            var size = ClassicMapFortuneLayout.Fortune.Size;
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, "BuildFortuneWindowPreview", false)!;
            AddChild(window); var panel = ClassicMapFortuneSkin.Apply(window.Body)!;
            Require(panel is ClassicFortunePanel, "Fortune Classic skin applied to the prepared native window");
            await Frames();
            var deck = (FortuneDeck)DetailField(world, "Fortune.Deck")!;
            Require(deck.Readings.Length > 0 && deck.FramesOf(Fortune.SpinFrames).Length > 0 && deck.FramesOf(Fortune.FinalFrames).Length > 0, "Fortune deck has readings and both card animations");
            var pick = Named<Control>(window, "fortune_pick"); var stage = Named<Control>(window, "fortune_stage");
            var cards = Enumerable.Range(0, Fortune.CardsShown).Select(i => Named<TextureButton>(window, "fortune_card_" + i)).ToArray();
            var ok = Named<Button>(window, "fortune_ok"); var name = Named<Label>(window, "fortune_name");
            var reveal = Named<Label>(window, "fortune_reveal"); var stars = Named<HBoxContainer>(window, "fortune_stars");
            var lines = Enumerable.Range(0, 3).Select(i => Named<Label>(window, "fortune_line_" + i)).ToArray();
            var spin = Named<TextureRect>(window, "fortune_spin"); var face = Named<TextureRect>(window, "fortune_face");
            Require(pick.Visible && !stage.Visible, "Fortune opens on the card fan");
            Require(cards.All(c => c.TextureNormal != null && c.TextureHover != null && Connected(c)), "Fortune cards keep the native card back, hover art and pick handler");
            // Cards overlap; only the left strip of each card except the last is uncovered.
            Vector2 Strip(Control card) => new(card.GetGlobalRect().Position.X + 12, card.GetGlobalRect().GetCenter().Y);
            var inset = new Rect2(13, 38, size.X - 26, ClassicMapFortuneLayout.Fortune.InsetBottom - 38);
            Require(cards.All(c => inset.Encloses(Within(panel, c)) && inset.Encloses(Within(panel, c) with { Position = Within(panel, c).Position - new Vector2(0, 16) })), "Fortune card fan and its hover lift stay inside the inset");
            await Capture(window, "fortune", "pick", size);
            await Hover(cards[6], Strip(cards[6]));
            Require(cards[6].GetDrawMode() == BaseButton.DrawMode.Hover && Within(panel, cards[6]).Position.Y == ClassicMapFortuneLayout.Fortune.CardTop - 16, "Fortune card lifts natively under the real mouse");
            await Capture(window, "fortune", "card-hover", size, cards[6]);
            await Hover(panel.CloseButton); Require(panel.CloseButton.GetDrawMode() == BaseButton.DrawMode.Hover, "Fortune close receives actual mouse hover");
            Require(Within(panel, cards[6]).Position.Y == ClassicMapFortuneLayout.Fortune.CardTop, "Fortune card drops back when the mouse leaves");
            await Capture(window, "fortune", "close-hover", size); await Leave();

            // A real click on the visible strip of a card picks it and plays the native turn.
            await Click(cards[3], Strip(cards[3]));
            Require(!pick.Visible && stage.Visible && reveal.Visible && !ok.Visible && !name.Visible, "Fortune card click starts the native turn");
            Require(spin.Visible && spin.IsProcessing(), "Fortune turn animation plays over the card face");
            await Frames(6);
            await Capture(window, "fortune", "spin", size);
            await ToSignal(GetTree().CreateTimer(2.4), SceneTreeTimer.SignalName.Timeout); await Frames(4);
            var drawn = (FortuneReading)DetailField(world, "Fortune.Reading")!;
            Require(name.Visible && name.Text == drawn.Title && ok.Visible && !reveal.Visible, "Fortune turn completes into the native reading");
            Require(face.Texture != null && stars.GetChildren().OfType<TextureRect>().Count(s => !s.IsQueuedForDeletion()) == Math.Clamp(drawn.Level, 0, Fortune.MaxStars), "Fortune reveals the card face and the reading's stars");
            void CheckLines()
            {
                var column = ClassicMapFortuneLayout.Fortune.Reading;
                foreach (var line in lines)
                {
                    Require(line.GetVisibleLineCount() == line.GetLineCount(), "Fortune reading line fully shown: " + line.Text);
                    var rect = Within(panel, line);
                    Require(column.Encloses(rect), "Fortune reading line stays in the text column " + rect);
                }
                foreach (var part in new Control[] { stars, name })
                    Require(column.Encloses(Within(panel, part)), "Fortune " + part.Name + " stays in the reading column");
            }
            CheckLines();
            await Capture(window, "fortune", "reading", size);
            var longest = deck.Readings.OrderByDescending(r => r.Lines.Select(l => l.Length).DefaultIfEmpty(0).Max()).First();
            DetailCall(world, "Fortune.Reveal", longest); Named<Flipbook>(window, "fortune_final").Stop(); await Frames(4);
            Require(name.Text == longest.Title && lines.Any(l => l.GetLineCount() > 1), "Fortune longest reading line wraps inside the column");
            CheckLines();
            await Capture(window, "fortune", "reading-long", size);
            await Hover(ok); Require(ok.GetDrawMode() == BaseButton.DrawMode.Hover, "Fortune OK receives actual mouse hover");
            await Capture(window, "fortune", "ok-hover", size);
            await Click(ok);
            Require(!window.Visible && !(bool)DetailField(world, "Fortune.shown")!, "Fortune OK mouse click invokes the native close");
            DetailCall(world, "Fortune.Open"); await Frames(2);
            Require(window.Visible && pick.Visible && !stage.Visible && cards.All(c => Within(panel, c) == c.GetMeta(ClassicMapFortuneSkin.ExpectedRect).AsRect2()), "Fortune reopens on a fresh card fan");
            await Click(panel.CloseButton);
            Require(!window.Visible && !(bool)DetailField(world, "Fortune.shown")!, "Fortune close mouse click invokes the native close");
            window.QueueFree(); await Frames(2); world.Free(); await Frames(2);
        }
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, "BuildFortuneDragPreview")!;
            AddChild(window); ClassicMapFortuneSkin.Apply(window.Body); await Frames();
            await Escape(world, window, "Fortune.shown", "Fortune");
            DetailCall(world, "Fortune.Open"); await Frames(2);
            await Drag(window, "Fortune");
            window.QueueFree(); await Frames(2); world.Free(); await Frames(2);
        }
        System.IO.File.WriteAllText(output + "/" + prefix + "-map-fortune.json",
            JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        offline.Free(); GD.Print("MAP_FORTUNE_AUDIT_OK " + checks.Count);
    }
}
