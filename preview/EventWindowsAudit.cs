using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;
using Spec = KnightOnlineUiClassic.Layout.ClassicEventFamilyLayout;
using Sched = KnightOnlineUiClassic.Layout.ClassicEventFamilyLayout.Schedule;

// The event, ranking and war windows the client builds from its own rows (event schedule, daily and board
// rankings, Border Defense War result and ranking, Chaos Dungeon, Monster Stone, Wanted, siege war, Knight
// Royale). Each is built by the client's preview builder (or a fixture), given the Classic family skin and
// captured for both nations; the audit checks the frame, the actual bounds of every visible control (the part a
// scroll list shows), the type and materials, the close button and that the native buttons keep their callbacks.
// The event schedule is checked card by card on both tabs, with "Only my level", empty, loading, a clock shift,
// the Join hover and Join opening the Classic registration of its temple while two sign-ups are open.
public partial class Preview
{
    private async Task CaptureEventWindowsAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1280, 800);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1280, 800), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        Native.Call(Game.Session.Self, "SeedPreviewEnter", new MyInfo { Nation = nation, Name = "Classic Preview" });
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool condition, string text) { if (!condition) throw new Exception("EVENT_WINDOWS_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        var viewport = GetViewportRect();
        bool Inside(Rect2 outer, Rect2 inner) => outer.Grow(.5f).Encloses(inner);
        // A tooltip the client opens on a control is a popup child of it, drawn in its own window.
        bool InPopup(Node node) { for (var up = node.GetParent(); up != null; up = up.GetParent()) if (up is Popup) return true; return false; }
        // The part of a control its clipping ancestors (scroll lists) let through; null when nothing of it is shown.
        Rect2? Shown(Control control, Control root)
        {
            var rect = control.GetGlobalRect();
            for (var parent = control.GetParent(); parent != null && parent != root; parent = parent.GetParent())
                if (parent is Control { ClipContents: true } or ScrollContainer)
                {
                    rect = rect.Intersection(((Control)parent).GetGlobalRect());
                    if (rect.Size.X <= 0 || rect.Size.Y <= 0) return null;
                }
            return rect;
        }
        float TextWidth(Control control, string text) => control.GetThemeFont("font").GetStringSize(text, HorizontalAlignment.Left, -1, control.GetThemeFontSize("font_size")).X;
        bool Connected(BaseButton button) => button.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0;
        var palette = new[] { ClassicDesign.FrameTitle, ClassicEventPanel.Caption, ClassicEventPanel.Ink, ClassicEventPanel.Muted, ClassicEventPanel.Accepted,
            ClassicEventPanel.Refused, new Color("ffe094"), new Color("9cd8f0"), new Color("efd9b4"), ClassicDesign.Muted };

        async Task<(WorldRoot World, HudWindow Window)> Open(string fixture, params object?[] args)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var window = (HudWindow)DetailCall(world, fixture, args)!;
            AddChild(window);
            window.Visible = true;
            var frame = ClassicEventFamily.Apply(window.Body);
            Require(frame != null && !frame.Ornate, window.Id + " gets the plain Classic frame");
            await Frames();
            return (world, window);
        }

        async Task Capture(HudWindow window, string state)
        {
            await Frames(); window.Position = ((viewport.Size - window.Size) / 2).Round(); await Frames(3);
            string tag = window.Id + " " + state;
            var box = window.GetGlobalRect();
            Require(Inside(viewport, box), tag + " complete window fits the viewport " + box);
            var frame = window.GetNode<ClassicFrame>("classic_frame");
            Require(frame.Size == window.Size && frame.Position == Vector2.Zero, tag + " frame covers the window");
            Require(window.GetThemeStylebox("panel") is StyleBoxEmpty, tag + " no modern window panel");
            // Everything visible sits inside the frame's straight line, with the even content margin of the family. A
            // scroll list clips its rows: only the part shown inside every clipping ancestor counts.
            var inner = box.Grow(-(ClassicFrame.PlainEdge - 2));
            foreach (var control in Descendants(window).OfType<Control>().Where(c => c.IsVisibleInTree() && c != window && c != frame && c.Size.X > 0 && c.Size.Y > 0
                && c is Label or Button or ItemSlotView or LineEdit or ProgressBar or TextureRect && !InPopup(c)))
                if (Shown(control, window) is { } shown)
                    Require(Inside(inner, shown), tag + " inside the frame: " + control.GetType().Name + " '" + (control as Label)?.Text + (control as Button)?.Text + "' " + shown);
            foreach (var label in Descendants(window.Body).OfType<Label>().Where(l => l.IsVisibleInTree() && l.Text.Length > 0 && !InPopup(l)))
            {
                Require(label.GetThemeFont("font") == Plugin.Kit.Bold, tag + " Arial Bold: '" + label.Text + "'");
                var ink = label.GetThemeColor("font_color");
                Require(palette.Any(c => c.IsEqualApprox(new Color(ink, 1))) || label.GetMeta("classic_native_ink", false).AsBool(), tag + " Classic ink " + ink.ToHtml() + ": '" + label.Text + "'");
                if (label.AutowrapMode == TextServer.AutowrapMode.Off && !label.ClipText)
                    Require(TextWidth(label, label.Text) <= label.Size.X + 1, tag + " text fits: '" + label.Text + "' " + TextWidth(label, label.Text) + " in " + label.Size.X);
                if (label.AutowrapMode != TextServer.AutowrapMode.Off)
                    Require(label.GetLineCount() * label.GetLineHeight() <= label.Size.Y + 2, tag + " wrapped text fits: '" + label.Text[..Math.Min(24, label.Text.Length)] + "'");
            }
            // The schedule's tabs and its check have their own art (checked by ScheduleChecks).
            bool Filter(Button b) => window.Id == "events" && b.ToggleMode;
            foreach (var button in Descendants(window.Body).OfType<Button>().Where(b => b.IsVisibleInTree() && b.Text.Length > 0 && b is not CheckBox and not CheckButton && !Filter(b)))
            {
                Require(button.GetThemeStylebox("normal") is StyleBoxTexture && button.GetThemeStylebox("hover") is StyleBoxTexture
                    && button.GetThemeStylebox("disabled") is StyleBoxTexture, tag + " Classic button art: " + button.Text);
                Require(button.Size.Y >= Spec.Button && TextWidth(button, button.Text) <= button.Size.X - 4, tag + " button fits its caption: " + button.Text + " " + button.Size);
            }
            foreach (var check in Descendants(window.Body).OfType<Button>().Where(b => b.IsVisibleInTree() && b.Text.Length > 0 && b is CheckBox or CheckButton))
                Require(check.GetThemeFont("font") == Plugin.Kit.Bold && check.GetThemeColor("font_color") == ClassicDarkFrame.Value,
                    tag + " check caption in Arial Bold and the light ink of both nations: " + check.Text + " " + check.GetThemeFont("font")?.ResourcePath + " " + check.GetThemeColor("font_color").ToHtml());
            foreach (var panel in Descendants(window.Body).OfType<PanelContainer>().Where(p => p.IsVisibleInTree() && p is not ItemSlotView))
                Require(panel.GetThemeStylebox("panel") is not StyleBoxFlat flat || flat.BgColor.A == 0
                    || (window.Id != "events" && flat.BgColor.IsEqualApprox(ClassicDarkFrame.SelectedRow)), tag + " no modern panel fill");
            foreach (var cell in Descendants(window.Body).OfType<ItemSlotView>().Where(c => c.IsVisibleInTree()))
                Require(cell.GetThemeStylebox("panel") is StyleBoxTexture, tag + " item cell on the inventory cell art");
            if (window.Header?.GetChildren().OfType<HBoxContainer>().FirstOrDefault() is { } bar)
            {
                var title = bar.GetChildren().OfType<Label>().Last();
                Require(title.IsVisibleInTree() && title.GetThemeFont("font") == Plugin.Kit.Bold && title.GetThemeColor("font_color") == ClassicDesign.FrameTitle, tag + " gold Arial Bold title");
                Require(Mathf.Abs(title.GetGlobalRect().Position.X - box.Position.X - Spec.Margin) < 1, tag + " title on the content edge");
                if (bar.GetChildren().OfType<Button>().FirstOrDefault(b => b.TooltipText == "Close") is { Visible: true } close)
                {
                    Require(close.Size == Spec.Close && close.GetThemeStylebox("normal") is StyleBoxTexture, tag + " original close knob");
                    Require(Mathf.Abs(box.End.X - close.GetGlobalRect().End.X - Spec.Margin) < 1, tag + " close knob 14 pixels from the right edge");
                }
            }
            string file = prefix + "-" + window.Id + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { window = window.Id, state, file, bounds = box.ToString() });
        }

        async Task Close(WorldRoot world, HudWindow window)
        {
            var close = window.Header?.GetChildren().OfType<HBoxContainer>().First().GetChildren().OfType<Button>().FirstOrDefault(b => b.TooltipText == "Close");
            if (close is { Visible: true })
            {
                close.EmitSignal(BaseButton.SignalName.Pressed);
                Require(!window.Visible, window.Id + " Close runs the native close");
            }
            window.QueueFree(); world.QueueFree(); await Frames(2);
        }

        async Task Hover(Control control, HudWindow window, string state)
        {
            var at = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(3);
            await Capture(window, state);
            GetViewport().PushInput(new InputEventMouseMotion { Position = Vector2.One, GlobalPosition = Vector2.One }); await Frames(2);
        }

        // Event schedule.
        {
            var tabArt = Plugin.Kit.Layout("{nation}_chat_us").Find("btn_normal")!;
            var checkArt = Plugin.Kit.Layout("{nation}_chat_us").Find("btn_check_normal")!;
            Rect2 Region(LayoutNode art, int state) { var image = art.Images.First(n => n.Tag == state); return new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH); }
            var insetRegion = ClassicNpcLayout.InsetBox().RegionRect;
            var cellRegion = ClassicProgressionPanel.CellBox().RegionRect;
            bool Lit(Control control) => control.GetThemeStylebox("panel") is StyleBoxTexture { ModulateColor.R: > 1.01f };
            bool OnInset(Control control) => control.GetThemeStylebox("panel") is StyleBoxTexture { DrawCenter: false } box && box.RegionRect == insetRegion;
            var statusInks = new[] { ClassicEventPanel.Accepted, ClassicEventPanel.Caption, ClassicEventFamily.Highlight };
            T Part<T>(WorldRoot world, string field) where T : class => (T)DetailField(world, field)!;
            PanelContainer[] Cards(WorldRoot world) => Part<VBoxContainer>(world, "Events.cards").GetChildren().OfType<PanelContainer>().Where(c => !c.IsQueuedForDeletion()).ToArray();
            Label[] Statuses(WorldRoot world) => Cards(world).Select(c => Descendants(c).OfType<Label>().First(l => l.HorizontalAlignment == HorizontalAlignment.Right)).ToArray();

            // Every shown card and the filters, in the composition of the given page and check state; returns the card count.
            int Schedule(WorldRoot world, HudWindow window, string state, bool races, bool mine, int? live = null)
            {
                string tag = "events " + state;
                var tabs = new[] { Part<Button>(world, "Events.tab"), Part<Button>(world, "Events.TabRaces") };
                var toggle = Part<Button>(world, "Events.myLevel");
                var scroll = Part<ScrollContainer>(world, "Events.Scroll");
                var empty = Part<Label>(world, "Events.empty");
                var filters = (Control)tabs[0].GetParent();
                Require(scroll.Size.X >= 640 && Mathf.IsEqualApprox(window.Size.X, scroll.Size.X + 2 * Spec.Margin), tag + ": the client's 640-pixel list in the plain frame " + window.Size);
                Require(tabs[0].ButtonPressed == !races && tabs[1].ButtonPressed == races, tag + ": the chosen tab is pressed");
                foreach (var tab in tabs)
                {
                    Require(tab.GetThemeStylebox("normal") is StyleBoxTexture normal && normal.RegionRect == Region(tabArt, 0)
                        && tab.GetThemeStylebox("pressed") is StyleBoxTexture pressed && pressed.RegionRect == Region(tabArt, 1)
                        && tab.GetThemeStylebox("hover") is StyleBoxTexture hover && hover.RegionRect == Region(tabArt, 2)
                        && tab.GetThemeStylebox("hover_pressed") == tab.GetThemeStylebox("pressed"), tag + ": original chat tab art on " + tab.Text);
                    Require(tab.Size.Y == Sched.TabHeight && tab.Size.X >= Sched.TabMinWidth && TextWidth(tab, tab.Text) <= tab.Size.X - 2 * Sched.TabPadding + 1,
                        tag + ": the tab fits its caption " + tab.Text + " " + tab.Size);
                    Require(tab.GetThemeFont("font") == Plugin.Kit.Bold && tab.GetThemeColor("font_color") == ClassicWorldOverlays.TabInk
                        && tab.GetThemeColor("font_pressed_color") == ClassicWorldOverlays.TabInk, tag + ": Arial Bold in the chat tab ink on " + tab.Text);
                    Require(tab.GetChildren().OfType<ColorRect>().All(line => line.Color.A == 0), tag + ": no modern underline on " + tab.Text);
                    Require(tab.GetSignalConnectionList(BaseButton.SignalName.Toggled).Count > 0, tag + ": " + tab.Text + " keeps its native page switch");
                }
                Require(Mathf.Abs(tabs[0].GetGlobalRect().Position.X - filters.GetGlobalRect().Position.X) < 1 && tabs[1].GetGlobalRect().Position.X > tabs[0].GetGlobalRect().End.X,
                    tag + ": the tabs from the content edge");
                Require(toggle.ButtonPressed == mine && toggle.Icon is AtlasTexture icon && icon.Region == Region(checkArt, mine ? 1 : 0),
                    tag + ": Only my level shows the " + (mine ? "checked" : "empty") + " chat check socket");
                Require(toggle.GetThemeFont("font") == Plugin.Kit.Bold && toggle.GetThemeColor("font_color") == ClassicDarkFrame.Value
                    && toggle.GetThemeStylebox("normal") is StyleBoxEmpty && toggle.GetThemeStylebox("pressed") is StyleBoxEmpty, tag + ": the check caption in the check ink, no chip");
                Require(Mathf.Abs(toggle.GetGlobalRect().End.X - filters.GetGlobalRect().End.X) < 1
                    && TextWidth(toggle, toggle.Text) + Region(checkArt, 0).Size.X + Sched.ToggleGap <= toggle.Size.X + 1, tag + ": the check on the right edge, caption fits " + toggle.Size);
                Require(toggle.GetSignalConnectionList(BaseButton.SignalName.Toggled).Count > 0, tag + ": Only my level keeps its native filter");
                // The list takes its cards' height, at most the client's; the bar only shows past it.
                var margin = scroll.GetChildren().OfType<MarginContainer>().Single();
                float content = margin.GetCombinedMinimumSize().Y;
                bool bar = scroll.GetVScrollBar().Visible;
                Require(Mathf.IsEqualApprox(scroll.Size.Y, Mathf.Clamp(content, Sched.EmptyHeight, Sched.MaxListHeight)), tag + $": the list is as tall as its cards ({scroll.Size.Y} for {content})");
                Require(bar == content > Sched.MaxListHeight, tag + ": the scroll bar only past the client's height");
                var cards = Cards(world).Where(c => c.IsVisibleInTree()).ToArray();
                Require(empty.Visible == (cards.Length == 0), tag + ": the empty text only without cards");
                if (empty.Visible)
                    Require(empty.Size.Y == Sched.EmptyHeight && empty.VerticalAlignment == VerticalAlignment.Center && empty.HorizontalAlignment == HorizontalAlignment.Center
                        && Inside(scroll.GetGlobalRect(), empty.GetGlobalRect()), tag + ": the empty text centred on two rows " + empty.GetGlobalRect());
                int lives = 0;
                var list = scroll.GetGlobalRect();
                foreach (var card in cards)
                {
                    var labels = Descendants(card).OfType<Label>().ToArray();
                    string at = tag + " '" + labels[0].Text + "'";
                    bool isLive = card.GetMeta(ClassicEventSchedule.LiveMeta, false).AsBool();
                    if (isLive) lives++;
                    Require(OnInset(card) && Lit(card) == isLive, at + ": the card on the quest inset, lit only while the event is live");
                    var rect = card.GetGlobalRect();
                    // Without a bar the card spans the list; with one it ends the bar gap before the bar.
                    float right = bar ? scroll.GetVScrollBar().GetGlobalRect().Position.X - Sched.BarGap : list.End.X;
                    Require(Mathf.Abs(rect.Position.X - list.Position.X) < 1 && Mathf.Abs(rect.End.X - right) < 1,
                        at + $": the card spans the list {rect} in {list}, bar {(bar ? scroll.GetVScrollBar().GetGlobalRect() : "none")}");
                    var tile = Descendants(card).OfType<PanelContainer>().First(p => p != card);
                    var glyph = Descendants(tile).OfType<TextureRect>().Single();
                    Require(tile.GetThemeStylebox("panel") is StyleBoxTexture cell && cell.RegionRect == cellRegion && Inside(tile.GetGlobalRect(), glyph.GetGlobalRect()),
                        at + ": the glyph on the nation inventory cell");
                    Require(glyph.SelfModulate == (isLive ? ClassicEventFamily.Highlight : ClassicEventPanel.Caption), at + ": the glyph in the caption gold");
                    var name = labels[0];
                    Require(name.GetThemeFontSize("font_size") == Sched.NameFont && (name.GetThemeColor("font_color") == ClassicEventPanel.Muted
                        || name.GetThemeColor("font_color") == (isLive ? ClassicEventFamily.Highlight : ClassicEventPanel.Ink)), at + ": the name ink");
                    var status = labels.First(l => l.HorizontalAlignment == HorizontalAlignment.Right);
                    Require(status.Text.Length > 0 && statusInks.Contains(status.GetThemeColor("font_color")), at + ": the status '" + status.Text + "' in the event ink");
                    Require(Inside(rect, status.GetGlobalRect()), at + ": the status inside the card");
                    foreach (var chip in Descendants(card).OfType<HFlowContainer>().SelectMany(f => f.GetChildren().OfType<PanelContainer>()))
                    {
                        Require(OnInset(chip) && !Lit(chip) && chip.Size.Y <= Spec.Row && Inside(rect, chip.GetGlobalRect()), at + ": the time chip as a small inset " + chip.GetGlobalRect());
                        Require(chip.TooltipText.StartsWith("Next: ") && Descendants(chip).OfType<Label>().Single().GetThemeColor("font_color") == ClassicEventPanel.Ink,
                            at + ": the chip keeps its local start tooltip, time in the light ink");
                    }
                    foreach (var day in Descendants(card).OfType<PanelContainer>().Where(p => p.HasMeta(ClassicEventSchedule.ActiveMeta)))
                    {
                        bool active = day.GetMeta(ClassicEventSchedule.ActiveMeta).AsBool();
                        var label = Descendants(day).OfType<Label>().Single();
                        Require(OnInset(day) && Lit(day) == active && label.GetThemeColor("font_color") == (active ? ClassicEventFamily.Highlight : ClassicEventPanel.Muted)
                            && Inside(rect, day.GetGlobalRect()), at + ": weekday " + label.Text + (active ? " lit in the highlight ink" : " in the muted ink"));
                    }
                    foreach (var action in Descendants(card).OfType<Button>())
                        Require(action.Size.Y == Spec.Button && Connected(action) && Inside(rect, action.GetGlobalRect()), at + ": " + action.Text + " is the service button with its native action");
                }
                if (live is { } expected) Require(lives == expected, tag + $": {expected} live card(s), {lives} shown");
                return cards.Length;
            }

            var (world, window) = await Open("Preview.BuildEvents");
            var buttons = Descendants(window.Body).OfType<Button>().Where(b => b.Text is "Join" or "Open").ToArray();
            Require(buttons.Length == 1 && buttons.All(Connected), "events: the sign-up row keeps its native Join action");
            int all = Schedule(world, window, "schedule", false, false, live: 1);
            // The client rewrites the status ink every second; the card keeps the Classic ink.
            DetailCall(world, "Events.TickEventClocks"); await Frames(2);
            Require(Statuses(world).All(l => statusInks.Contains(l.GetThemeColor("font_color"))), "events: the status keeps the Classic ink after the client's tick");
            await Capture(window, "schedule");
            await Hover(buttons[0], window, "join-hover");
            // A start time's tooltip (the next start in local time) in the original tooltip look.
            {
                IEnumerable<Node> All(Node node) { yield return node; foreach (var child in node.GetChildren(true)) foreach (var item in All(child)) yield return item; }
                var chip = Descendants(Cards(world)[0]).OfType<HFlowContainer>().First().GetChildren().OfType<PanelContainer>().First();
                // A focus change of another window cancels the client's tooltip timer: hover again, up to three times.
                PopupPanel? tip = null;
                for (int attempt = 0; attempt < 3 && tip == null; attempt++)
                {
                    var at = chip.GetGlobalRect().GetCenter() + new Vector2(attempt, 0);
                    GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                    ulong until = Time.GetTicksMsec() + 1500;
                    while (tip == null && Time.GetTicksMsec() < until) { await Frames(1); tip = All(chip).OfType<PopupPanel>().FirstOrDefault(p => p.Visible); }
                }
                Require(tip != null, "events: the time chip shows its tooltip");
                await Frames(4);
                var tipLabel = All(tip!).OfType<Label>().First();
                Require(tip!.GetThemeStylebox("panel", "TooltipPanel") is StyleBoxFlat { BgColor: { R: 0, G: 0, B: 0, A: < 1f } } && tipLabel.GetThemeFont("font") == Plugin.Kit.Bold
                    && tipLabel.GetThemeColor("font_color") == Colors.White && tipLabel.Text == chip.TooltipText, "events: the start tooltip in the original tooltip look: " + tipLabel.Text);
                Require(GetViewportRect().Encloses(new Rect2(tip.Position, tip.Size)), "events: the start tooltip inside the viewport");
                await Capture(window, "chip-tooltip");
                GetViewport().PushInput(new InputEventMouseMotion { Position = Vector2.One, GlobalPosition = Vector2.One }); await Frames(4);
            }
            var toggle = Part<Button>(world, "Events.myLevel");
            toggle.ButtonPressed = true; await Frames(4);
            int fitting = Schedule(world, window, "only-my-level", false, true, live: 1);
            Require(fitting > 0 && fitting < all && Cards(world).All(c => Descendants(c).OfType<Label>().First().GetThemeColor("font_color") != ClassicEventPanel.Muted),
                $"events: Only my level keeps the {fitting} of {all} events level 62 may join");
            await Capture(window, "only-my-level");
            Part<Button>(world, "Events.TabRaces").ButtonPressed = true; await Frames(4);
            Require(Schedule(world, window, "empty", true, true) == 0 && Part<Label>(world, "Events.empty").Text == "Nothing to show here.", "events: no race for level 62 shows the empty text");
            await Capture(window, "empty");
            toggle.ButtonPressed = false; Part<Button>(world, "Events.tab").ButtonPressed = true; await Frames(4);
            Require(Schedule(world, window, "schedule-again", false, false, live: 1) == all, "events: the page and the filter switch back");

            // Join with two temple sign-ups open: the card's temple becomes the shown sign-up in the Classic registration.
            DetailCall(world, "Bifrost.BuildUi");
            var layer = Part<CanvasLayer>(world, "Bifrost.layer");
            layer.Reparent(this);
            NativeOverlays.Prepare(world);
            var signUps = Game.Session.Bifrost.SignUps;
            var join = Part<Control>(world, "Bifrost.joinModal");
            var joinTitle = Part<Label>(world, "Bifrost.joinModalTitle");
            DetailCall(world, "Bifrost.OnTime", 600, TempleEventType.ForgottenTemple);
            Require(join.Visible && joinTitle.Text.Contains("FORGOTTEN TEMPLE"), "events: the client offers the first sign-up by itself");
            DetailCall(world, "Bifrost.CloseJoinModal");
            DetailCall(world, "Bifrost.OnTime", 412, TempleEventType.BorderDefenseWar);
            DetailCall(world, "Bifrost.CloseJoinModal");
            signUps.Select(TempleEventType.ForgottenTemple);
            Require(signUps.Open.Count == 2 && !join.Visible && signUps.Current == TempleEventType.ForgottenTemple, "events: two temple sign-ups open, the registration closed");
            // The filters rebuilt the cards: the Join of the rebuilt Border Defense War card.
            var cardJoin = Cards(world).SelectMany(c => Descendants(c).OfType<Button>()).Single(b => b.Text == "Join");
            Require(Connected(cardJoin) && cardJoin.TooltipText == "Border Defense War", "events: the rebuilt card keeps its native Join");
            cardJoin.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(4);
            var registration = join.GetNodeOrNull<Control>("classic_registration");
            Require(join.Visible && signUps.Current == TempleEventType.BorderDefenseWar && joinTitle.Text == "#  BORDER DEFENSE WAR  #",
                "events: Join opens the registration of its own temple (" + joinTitle.Text + ")");
            Require(registration != null && registration.Visible && !join.GetChildren().OfType<CenterContainer>().Single().Visible
                && registration.Size == ClassicOverlayLayout.Registration.Size, "events: the Classic registration box, not the client's panel");
            Require(Part<Button>(world, "Bifrost.joinModalBtn") is { Visible: true, Text: "Join" } && joinTitle.GetThemeColor("font_color") == ClassicWorldOverlays.MessageTitle,
                "events: the registration offers Join for Border Defense War");
            await Capture(window, "join-registration");
            DetailCall(world, "Bifrost.CloseJoinModal");
            layer.QueueFree();
            await Close(world, window);

            (world, window) = await Open("Preview.BuildEvents", "races");
            Require(Schedule(world, window, "races", true, false, live: 1) == 1, "events: the Collection Races page lists the race");
            await Capture(window, "races");
            await Close(world, window);
            (world, window) = await Open("Preview.BuildEvents", "end");
            await Frames(4);
            var endScroll = Part<ScrollContainer>(world, "Events.Scroll");
            Schedule(world, window, "end", false, false, live: 1);
            var last = Cards(world).Last();
            Require(endScroll.ScrollVertical > 0 && Inside(endScroll.GetGlobalRect(), last.GetGlobalRect()), "events: the end of the list shows the last card whole " + last.GetGlobalRect());
            await Capture(window, "end");
            await Close(world, window);
            (world, window) = await Open("BuildEventsScheduleUiPreview", "loading");
            Require(Schedule(world, window, "loading", false, false) == 0 && Part<Label>(world, "Events.empty").Text == "Loading the schedule...", "events: the loading text");
            Require(window.Size.Y <= Spec.TitleTop + Spec.TitleHeight + Spec.ContentGap + Sched.TabHeight + Spec.BodyGap + Sched.EmptyHeight + 4 + Spec.Margin + 8,
                "events: the loading schedule is a short box, not the empty list " + window.Size);
            await Capture(window, "loading");
            await Close(world, window);
            (world, window) = await Open("BuildEventsShiftUiPreview", "shift");
            var shift = Part<Label>(world, "Events.shift");
            Require(shift.IsVisibleInTree() && shift.Text.StartsWith("A game master moved the event clock") && shift.GetThemeColor("font_color") == ClassicEventPanel.Caption,
                "events: the clock shift line in the caption ink: " + shift.Text);
            Schedule(world, window, "clock-shift", false, false, live: 1);
            await Capture(window, "clock-shift");
            await Close(world, window);
            // A live lottery draw: its card offers Open (the client's ToggleLottery) on the same service button as Join.
            (world, window) = await Open("BuildEventsLotteryUiPreview", "lottery");
            Schedule(world, window, "lottery", false, false, live: 2);
            var liveActions = Cards(world).SelectMany(c => Descendants(c).OfType<Button>()).ToArray();
            var lotteryOpen = liveActions.Single(b => b.Text == "Open");
            var templeJoin = liveActions.Single(b => b.Text == "Join");
            Require(Connected(lotteryOpen) && lotteryOpen.TooltipText == "Moradon Grand Lottery"
                && lotteryOpen.GetThemeStylebox("normal") is StyleBoxTexture openArt && templeJoin.GetThemeStylebox("normal") is StyleBoxTexture joinArt
                && openArt.Texture == joinArt.Texture && openArt.RegionRect == joinArt.RegionRect
                && lotteryOpen.GetThemeFont("font") == templeJoin.GetThemeFont("font") && lotteryOpen.GetThemeColor("font_color") == templeJoin.GetThemeColor("font_color"),
                "events: the live lottery's Open is the service button with its native action");
            await Capture(window, "lottery");
            await Close(world, window);
        }
        // Daily rank and the rank board.
        foreach (var view in new[] { "daily-rank", "daily-rank-clan" })
        {
            var (world, window) = await Open("BuildDailyRankBoardsUiPreview", view);
            Require(Native.Get<OptionButton>(world, "DailyRankWindow.drPicker")!.Text.Length > 0, "daily_rank: the board picker shows the chosen board");
            await Capture(window, view == "daily-rank" ? "players" : "clans");
            await Close(world, window);
        }
        {
            var (world, window) = await Open("Preview.BuildRankBoard");
            await Capture(window, "filled");
            await Close(world, window);
        }
        // Border Defense War result and ranking.
        {
            var (world, window) = await Open("Preview.BuildWarResult");
            await Capture(window, "finished");
            await Close(world, window);
            (world, window) = await Open("Preview.BuildWarRanking");
            await Capture(window, "filled");
            await Close(world, window);
        }
        // Chaos Dungeon.
        foreach (bool final in new[] { false, true })
        {
            var (world, window) = await Open("Preview.BuildChaosLadder", final);
            await Capture(window, final ? "final" : "running");
            await Close(world, window);
        }
        {
            var (world, window) = await Open("Preview.BuildChaosSkillPage");
            // Read only: the family restyle leaves the client's dimming (Modulate) on the Chaos skill icons. Cube skills
            // stay dimmed until their item is in the bag, and every skill while the character blinks in the dungeon.
            var dim = Native.ClientType("LibreKO.Features.Hud.HotSlotCell")?.GetField("DimColor")?.GetValue(null) is Color color ? color : new Color(.5f, .5f, .5f);
            TextureRect[] icons = Array.Empty<TextureRect>();
            int pages = 0, cubes = 0;
            for (int page = 0; page < 3; page++)
            {
                pages++;
                if (page > 0) { DetailCall(world, "ChaosSkillWindow.FlipChaosSkillPage", 1); await Frames(2); }
                icons = Descendants(window.Body).OfType<TextureRect>().Where(t => t.GetType().Name == "ChaosSkillIcon" && !t.IsQueuedForDeletion()).ToArray();
                DetailCall(world, "ChaosSkillWindow.ChaosSkillPageTick"); await Frames(2);
                Require(icons.Length > 0 && icons.All(i => i.Modulate == ((bool)DetailCall(world, "Hotbar.SkillItemHeld", Native.Get<object>(i, "Skill"))! ? Colors.White : dim)),
                    $"chaos_skill page {page + 1}: the client's item dimming stays on the Classic page ({icons.Count(i => i.Modulate == dim)} of {icons.Length} dimmed)");
                cubes += icons.Count(i => i.Modulate == dim);
                if (page == 0) await Capture(window, "page");
                else if (icons.Any(i => i.Modulate == dim)) { await Capture(window, "page-items"); break; }
            }
            checks.Add($"chaos_skill: {cubes} item skill(s) dimmed without their item, {pages} page(s) checked");
            Native.Set(Game.Session.Progress, "SelfAbnormal", RespawnBlink.Blinking);
            DetailCall(world, "ChaosSkillWindow.ChaosSkillPageTick"); await Frames(2);
            Require(icons.All(i => i.Modulate == dim), "chaos_skill: every skill dimmed while blinking in the dungeon");
            await Capture(window, "page-blink");
            Native.Set(Game.Session.Progress, "SelfAbnormal", 0);
            await Close(world, window);
            (world, window) = await Open("Preview.BuildChaosGuide");
            await Capture(window, "guide");
            await Close(world, window);
        }
        // Monster Stone.
        foreach (var view in new[] { "short", "long" })
        {
            var (world, window) = await Open("BuildNestUiPreview", view);
            var actions = Descendants(window.Body).OfType<Button>().Where(b => b.Text.Length > 0).ToArray();
            Require(actions.Length == 3 && actions.All(Connected), "nestindun: Enter, Receive EXP and Cancel keep their native actions");
            await Capture(window, view);
            var labels = Descendants(window.Body).OfType<Label>().Where(l => l.IsVisibleInTree()).ToArray();
            var description = labels.First(l => l.AutowrapMode != TextServer.AutowrapMode.Off);
            var question = labels.First(l => l.GetGlobalRect().Position.Y > description.GetGlobalRect().Position.Y);
            float band = question.GetGlobalRect().Position.Y - description.GetGlobalRect().End.Y;
            Require(band <= 12, $"nestindun {view}: the question follows the description without an empty band ({band:0} px)");
            await Close(world, window);
        }
        // Wanted.
        foreach (var (mode, view) in new[] { (1, "invite"), (3, "invite-mini"), (2, "list"), (4, "list-mini") })
        {
            var (world, window) = await Open("Preview.BuildVanguard", mode);
            var actions = Descendants(window.Body).OfType<Button>().Where(b => b.Text.Length > 0 && b is not CheckBox).ToArray();
            if (view == "list") Require(actions.Length == 0 && Descendants(window.Body).OfType<CheckBox>().Single().GetSignalConnectionList(BaseButton.SignalName.Toggled).Count > 0,
                window.Id + ": the Wanted list keeps its native hide option");
            else Require(actions.Length > 0 && actions.All(Connected), window.Id + ": Accept, Reject and Detail keep their native actions");
            await Capture(window, view);
            await Close(world, window);
        }
        // Castle siege war and Knight Royale.
        foreach (var view in new[] { "unheld", "held" })
        {
            var (world, window) = view == "held" ? await Open("Preview.BuildSiegeWar") : await Open("BuildSiegeWarStateUiPreview", view);
            await Capture(window, view);
            await Close(world, window);
        }
        {
            var (world, window) = await Open("Preview.BuildRoyaleScore");
            await Capture(window, "running");
            await Close(world, window);
        }

        System.IO.File.WriteAllText(output + "/" + prefix + "-event-windows.json",
            JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        offline.Free(); GD.Print("EVENT_WINDOWS_AUDIT_OK " + checks.Count);
    }
}
