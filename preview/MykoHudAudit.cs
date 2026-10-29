using Godot;
using System.Reflection;
using System.Text.Json;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Native;
using KnightOnlineUiClassic.Windows;
using L = KnightOnlineUiClassic.Layout.ClassicMykoHudLayout;

// The HUD pieces on the bundled 2.x art (myko-hud-audit): the status bar's letter plates and clock (the client's
// Events window keeps its Classic frame) and the client's event title chips as notice bars with the time box, the GM List
// bar and the content box. myko-hud-fallback-audit turns the art off: then every piece must keep the Classic design
// (no clock, plaque chips).
public partial class Preview
{
    private async Task CaptureMykoHudAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1280, 800);
        AddChild(new ColorRect { Color = new Color("2f332a"), Size = new Vector2(1280, 800), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        Native.Call(Game.Session.Self, "SeedPreviewEnter", new MyInfo { Nation = nation, Name = "Classic Preview" });
        ItemData.EnsureLoaded();
        bool local = ClassicMykoPlates.Available && ClassicNoticeBars.Available;
        string variant = local ? "local" : "classic";
        string prefix = (nation == 1 ? "karus" : "human") + "-" + variant;
        var checks = new List<string>(); var screens = new List<object>(); var bounds = new Dictionary<string, object>();
        void Require(bool condition, string text) { if (!condition) throw new Exception("MYKO_HUD_AUDIT: " + text); checks.Add(text); }
        Require(local == !ClassicFallback, "the bundled 2.x art is " + (ClassicFallback ? "off in the fallback mode" : "on by default") + " (" + Plugin.Kit.Local.Root + ")");
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        object Rect(Rect2 r) => new { x = r.Position.X, y = r.Position.Y, w = r.Size.X, h = r.Size.Y };
        bool Inside(Rect2 outer, Rect2 inner) => outer.Grow(.5f).Encloses(inner);
        bool Near(Vector2 a, Vector2 b) => (a - b).Abs() is var d && d.X < .51f && d.Y < .51f;
        float TextWidth(Label label) => label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X;
        async Task Shot(string state, Rect2 crop)
        {
            await Frames();
            using var image = GetViewport().GetTexture().GetImage();
            var r = new Rect2I((Vector2I)crop.Position.Floor(), (Vector2I)crop.Size.Ceil()).Intersection(new Rect2I(Vector2I.Zero, image.GetSize()));
            string file = prefix + "-" + state + ".png";
            using (var part = image.GetRegion(r)) part.SavePng(System.IO.Path.Combine(output, file));
            screens.Add(new { state, file, crop = Rect(crop) });
        }
        async Task Hover(Vector2 at) { GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(3); }
        async Task Click(Vector2 at)
        {
            await Hover(at);
            foreach (bool down in new[] { true, false })
            {
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
                await Frames(1);
            }
            await Frames(3);
        }
        async Task Away() => await Hover(new Vector2(1, 799));
        Require(local == (Plugin.Kit.Local.Layout(L.HpMenu.Replace("{nation}", nation == 1 ? "ka" : "el")) != null), variant + ": the local layouts are " + (local ? "installed" : "absent") + " at " + Plugin.Kit.Local.Root);

        // ---- Status bar: letter plates and the clock.
        {
            var status = new StatusHud(); AddChild(status); await Frames(8);
            // Two rows (plugin be6856d): Q P A D G, then E R, the clock and mail.
            var letters = Descendants(status).OfType<ClassicStatusShortcut>().Where(s => s.HasBackground).OrderBy(s => s.GetGlobalRect().Position.Y).ThenBy(s => s.GetGlobalRect().Position.X).ToArray();
            var clock = Descendants(status).OfType<ClassicStatusClock>().SingleOrDefault();
            Require(letters.Select(s => s.Letter).SequenceEqual(new[] { "Q", "P", "A", "D", "G", "E", "R" }), "the letter rows stay Q P A D G / E R");
            Require(letters.Take(5).All(s => s.GetGlobalRect().Position.Y == letters[0].GetGlobalRect().Position.Y) && letters.Skip(5).All(s => s.GetGlobalRect().Position.Y == letters[5].GetGlobalRect().Position.Y)
                && letters[5].GetGlobalRect().Position.Y - letters[0].GetGlobalRect().End.Y == 2 && letters[5].GetGlobalRect().Position.X == letters[0].GetGlobalRect().Position.X,
                "the second row starts under Q, 2 px below the first");
            Require(local == (clock != null), local ? "the original clock follows the letter row" : "no clock without the 2.x art");
            var row = letters.Aggregate(letters[0].GetGlobalRect(), (r, s) => r.Merge(s.GetGlobalRect()));
            if (clock != null)
            {
                var last = letters.Single(s => s.Letter == "R").GetGlobalRect(); float pitch = letters[1].GetGlobalRect().Position.X - letters[0].GetGlobalRect().Position.X;
                var c = clock.GetGlobalRect();
                Require(Near(c.Position, last.Position + new Vector2(pitch, 0)) && c.Size == last.Size, $"the clock sits one pitch after R in the second row with the plate size ({c} after {last})");
                Require(status.GetGlobalRect().Grow(.5f).Encloses(c) && Inside(GetViewportRect(), c), "the clock lies inside the status HUD and the screen");
                Require(clock.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0 && clock.TooltipText == ClassicStatusClock.Title, "the clock opens the event schedule");
                bounds["clock"] = Rect(c);
                row = row.Merge(c);
            }
            foreach (var s in letters) bounds["letter-" + s.Letter] = Rect(s.GetGlobalRect());
            var crop = row.Grow(6);
            await Shot("row-normal", crop);
            var buttons = letters.Cast<BaseButton>().Concat(clock != null ? new BaseButton[] { clock } : Array.Empty<BaseButton>()).ToArray();
            foreach (var b in buttons) { b.EmitSignal(Control.SignalName.MouseEntered); b.QueueRedraw(); }
            await Shot("row-hover", crop);
            foreach (var b in buttons) { b.ToggleMode = true; b.SetPressedNoSignal(true); b.QueueRedraw(); }
            if (clock != null) Require(clock.State == KnightOnlineUiClassic.Layout.ButtonState.Pressed, "the clock draws its pressed art");
            await Shot("row-pressed", crop);
            foreach (var b in buttons) { b.SetPressedNoSignal(false); b.ToggleMode = false; b.EmitSignal(Control.SignalName.MouseExited); b.Disabled = true; b.QueueRedraw(); }
            await Shot("row-disabled", crop);
            status.QueueFree(); await Frames(2);
        }

        // ---- Events window: the clock and E open the client's Events window, which keeps its Classic family frame.
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var window = (HudWindow)DetailCall(world, "Preview.BuildEvents")!;
            AddChild(window); window.Visible = true;
            var frame = ClassicEventFamily.Apply(window.Body);
            await Frames(8);
            window.Position = ((GetViewportRect().Size - window.Size) / 2).Round();
            await Frames(4);
            Require(frame != null && window.GetNodeOrNull<Control>("classic_frame") != null, "events: the Classic family frame, with or without the 2.x art");
            await Shot("events-classic", window.GetGlobalRect().Grow(4));
            window.QueueFree(); world.QueueFree(); await Frames(2);
        }

        // ---- Top-right group: premium, clan premium, time, the client's event title chips, GM List, content box.
        void Premium(int status, int type, int hours, params PremiumBonus[] bonuses)
        {
            foreach (var (name, value) in new (string, object)[] { ("PremiumAccountStatus", status), ("PremiumType", type), ("PremiumHours", hours),
                ("PremiumBonuses", (IReadOnlyList<PremiumBonus>)bonuses) })
                typeof(Net).GetProperty(name)!.SetValue(Net.I, value);
        }
        var layer = new CanvasLayer { Layer = 70 }; AddChild(layer);
        foreach (bool expanded in new[] { false, true })
        {
            NativeGmList.Clear();
            Premium(0, 0, 0);
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var column = (Container)DetailCall(world, "Preview.BuildEventTitles", 3, expanded)!;
            column.GetParent()?.RemoveChild(column);
            layer.AddChild(column); column.Visible = true;
            column.Position = new Vector2(1280 - 190 - 169, 40);
            NativeOverlays.PrepareLater(world);
            await Frames(8);
            string tag = expanded ? "expanded" : "collapsed";
            var chips = column.GetChildren().OfType<PanelContainer>().ToList();
            Require(chips.Count == (expanded ? 3 : 1), tag + ": the client's chips (" + chips.Count + ")");
            var stack = layer.GetNodeOrNull<ClassicNoticeStack>(ClassicNoticeBars.StackName);
            if (!local)
            {
                Require(stack == null && chips.All(c => c.GetThemeStylebox("panel") is StyleBoxEmpty && c.GetSignalConnectionList(CanvasItem.SignalName.Draw).Count > 0 && !c.HasMeta("classic_notice_bar")),
                    tag + ": the Classic plaque chips without the 2.x art");
                await Shot("notices-classic-" + tag, column.GetGlobalRect().Grow(6));
                layer.RemoveChild(column); column.QueueFree(); world.Free(); await Frames(2);
                continue;
            }
            Require(stack != null, tag + ": the top-right group follows the column");
            bool inClan = expanded; long clanEnds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (3 * 24 + 2) * 3600 + 120;
            stack!.ClockText = () => "14:33  Clear";
            stack.ClanSource = () => new NativePremium.Clan(inClan, expanded, expanded ? clanEnds : 0);
            // The clan online bonus (3277df5): 24 members online lights three of six marks.
            const int bonusOnline = 24;
            stack.BonusSource = () => inClan ? bonusOnline : ClanSession.NoClanBonus;
            var bonusLines = NativePremium.BonusLines(true, bonusOnline);
            stack.Sync(); await Frames(4);
            var gm = stack.GmBar; var box = stack.Box; var premium = stack.PremiumBar; var clan = stack.ClanBar; var time = stack.TimePlate;
            float right = 1280 - L.GroupRight;
            // Order and geometry: premium at the top, clan premium (in a clan), time, notice bars, GM List.
            var p = premium.GetGlobalRect();
            Require(p.Size == L.Bar && Near(p.Position, new Vector2(right - L.Bar.X, L.GroupTop)), $"{tag}: the premium bar tops the group at the right edge ({p})");
            Require(clan.Visible == inClan, $"{tag}: the clan premium bar shows only in a clan");
            float y = p.End.Y + L.BarGap;
            if (inClan) { var c = clan.GetGlobalRect(); Require(c.Size == L.Bar && Near(c.Position, new Vector2(right - L.Bar.X, y)), $"{tag}: the clan premium bar follows ({c})"); y = c.End.Y + L.BarGap; }
            var bonusBar = stack.BonusBar;
            Require(bonusBar.Visible == inClan, $"{tag}: the clan bonus bar shows only while the server reports the clan's online count");
            if (inClan)
            {
                var bb = bonusBar.GetGlobalRect();
                Require(bb.Size == L.Bar && Near(bb.Position, new Vector2(right - L.Bar.X, y)), $"{tag}: the clan bonus bar follows the clan premium bar ({bb})");
                var bonusCaption = Descendants(bonusBar).OfType<Label>().Single();
                Require(stack.BonusCaption == ClassicNoticeBars.BonusCaption + "   " + bonusLines.Marks && bonusCaption.GetThemeColor("font_color") == Colors.White
                    && TextWidth(bonusCaption) <= bonusCaption.Size.X, $"{tag}: the clan bonus bar shows the client's marks in white ('{stack.BonusCaption}', {TextWidth(bonusCaption)} px)");
                bounds[$"{tag}-bonus"] = Rect(bb);
                y = bb.End.Y + L.BarGap;
            }
            var t = time.GetGlobalRect();
            Require(t.Size == L.Bar && Near(t.Position, new Vector2(right - L.Bar.X, y)), $"{tag}: the time box follows, bar-sized and right-aligned ({t})");
            Require(stack.TimeValue.Text == L.TimeNote + "14:33" && time.TooltipText == "Server time 14:33, Clear", $"{tag}: the time box shows the client's clock ({stack.TimeValue.Text} / {time.TooltipText})");
            // The time box is the content box in one line: its frame (without the scroll bar) and its text style, centred.
            var contentArt = ClassicMykoArt.Layout(L.Content)!;
            Require(time.GetThemeStylebox("panel") is StyleBoxTexture { Texture: { } frame } timeBox && frame.GetSize() == new Vector2(contentArt.W, contentArt.Images.Where(n => n.X < contentArt.Find("scroll")!.X).Max(n => n.Y + n.H))
                && timeBox.TextureMarginLeft == L.TimeFrame && timeBox.TextureMarginRight == L.TimeFrame && timeBox.TextureMarginTop == L.TimeFrame && timeBox.TextureMarginBottom == L.TimeFrame,
                $"{tag}: the time box is drawn on the content box's frame");
            var tv = stack.TimeValue; var tr = tv.GetGlobalRect();
            Require(tv.GetThemeColor("font_color") == stack.Text.GetThemeColor("font_color") && tv.GetThemeFont("font") == stack.Text.GetThemeFont("font")
                && tv.GetThemeFontSize("font_size") == stack.Text.GetThemeFontSize("font_size"), $"{tag}: the time uses the content box's text style ({tv.GetThemeColor("font_color")})");
            Require(Inside(t, tr) && tr.Position.X >= t.Position.X + L.TimeFrame - .5f && tr.End.X <= t.End.X - L.TimeFrame + .5f && TextWidth(tv) <= tr.Size.X && Mathf.Abs(tr.GetCenter().X - t.GetCenter().X) < .51f && Mathf.Abs(tr.GetCenter().Y - t.GetCenter().Y) < .51f
                && tv.HorizontalAlignment == HorizontalAlignment.Center && tv.VerticalAlignment == VerticalAlignment.Center, $"{tag}: the time sits centred and whole inside the frame ({tr}, {TextWidth(tv)} px)");
            using (var screen = GetViewport().GetTexture().GetImage())
            {
                Color At(float x, float yy) => screen.GetPixel((int)x, (int)yy);
                Color border = At(t.Position.X + 2, t.GetCenter().Y), inner = At(t.Position.X + 5, t.GetCenter().Y);
                Require(border.Luminance > inner.Luminance + .15f && At(t.GetCenter().X, t.Position.Y + 2).Luminance > inner.Luminance + .15f
                    && At(t.GetCenter().X, t.End.Y - 2).Luminance > inner.Luminance + .15f && At(t.End.X - 3, t.GetCenter().Y).Luminance > inner.Luminance + .15f,
                    $"{tag}: the frame border shows on all four sides of the time box ({border} over {inner})");
            }
            y = t.End.Y + L.BarGap;
            for (int i = 0; i < chips.Count; i++)
            {
                var r = chips[i].GetGlobalRect();
                Require(r.Size == L.Bar && Near(r.Position, new Vector2(right - L.Bar.X, y)), $"{tag}: notice bar {i} is 169×23 in the group ({r}, expected y {y})");
                var caption = Descendants(chips[i]).OfType<Label>().Single();
                Require(Inside(r, caption.GetGlobalRect()) && caption.ClipText && Mathf.Abs(caption.GetGlobalRect().GetCenter().X - r.GetCenter().X) < 1.01f,
                    $"{tag}: caption '{caption.Text}' centred inside its bar ({caption.GetGlobalRect()})");
                bounds[$"{tag}-bar-{i}"] = Rect(r);
                y = r.End.Y + L.BarGap;
            }
            var knob = Descendants(chips[0]).OfType<Button>().SingleOrDefault();
            Require(knob != null && knob.Size == new Vector2(16, 16) && knob.GetThemeStylebox("normal") is StyleBoxTexture, tag + ": the first bar keeps the client's toggle on the original knob");
            var k = knob!.GetGlobalRect(); var first = chips[0].GetGlobalRect();
            Require(Mathf.Abs(k.Position.X - first.Position.X - 152) < 1.01f && Mathf.Abs(k.Position.Y - first.Position.Y - L.KnobTop) < 1.01f, $"{tag}: the knob sits at the original (152, 5) ({k.Position - first.Position})");
            var g = gm.GetGlobalRect();
            Require(g.Size == L.Bar && Near(g.Position, new Vector2(right - L.Bar.X, y)), $"{tag}: the GM List bar follows the last notice bar ({g})");
            Require(!box.Visible, tag + ": the content box starts closed");
            foreach (var (name, control) in new (string, Control)[] { ("premium", premium), ("clan", clan), ("time", time), ("gm", gm) }) if (control.Visible) bounds[$"{tag}-{name}"] = Rect(control.GetGlobalRect());
            Rect2 Group() => new Control[] { premium, clan, stack.BonusBar, time, gm }.Concat(column.GetChildren().OfType<Control>()).Where(c => c.IsVisibleInTree())
                .Aggregate(p, (r, c) => r.Merge(c.GetGlobalRect())).Merge(box.Visible ? box.GetGlobalRect() : p).Grow(6);
            // Premium: none (muted), then active; a click shows the time left and the benefits, again or outside closes.
            Require(stack.PremiumCaption == "No Premium" && ((Label)Descendants(premium).OfType<Label>().Single()).GetThemeColor("font_color") == L.Muted, tag + ": no premium reads 'No Premium' in the muted ink");
            await Shot("group-" + tag, Group());
            await Click(p.GetCenter()); await Away();
            Require(box.Visible && stack.OpenKey == ClassicNoticeBars.PremiumKey && stack.Text.Text.StartsWith("No Premium"), tag + ": the premium bar opens 'No Premium' details");
            await Click(p.GetCenter()); await Away();
            Require(!box.Visible, tag + ": pressing the premium bar again closes the box");
            // PremiumNames has no type constants since 3277df5 (the names come from the text table); 5 is Gold. The bar
            // keeps the approved title case ("Gold Premium"), not the table's upper case.
            const int goldPremium = 5;
            Premium(1, goldPremium, 52, new PremiumBonus(PremiumBonusKind.Experience, 10), new PremiumBonus(PremiumBonusKind.Drops, 5));
            stack.Sync(); await Frames(3);
            Require(stack.PremiumCaption == "Gold Premium" && ((Label)Descendants(premium).OfType<Label>().Single()).GetThemeColor("font_color") == Colors.White, tag + ": an active premium shows its name in white: " + stack.PremiumCaption);
            await Click(p.GetCenter()); await Away();
            Require(box.Visible && stack.Text.Text == "Gold Premium\n2 days 4 hours left\nEXP +10%\nDrop chance +5%", tag + ": a click shows the time left and the benefits: " + stack.Text.Text.Replace("\n", " / "));
            var b = box.GetGlobalRect();
            Require(b.Size == new Vector2(169, 149) && Near(b.Position, new Vector2(right - 169, g.End.Y + L.ContentGap)), $"{tag}: the 169×149 box opens under the group ({b})");
            await Shot("premium-open-" + tag, Group());
            await Click(new Vector2(300, 600)); await Away();
            Require(!box.Visible && stack.OpenKey == null, tag + ": a click outside closes the box");
            if (inClan)
            {
                await Click(clan.GetGlobalRect().GetCenter()); await Away();
                Require(box.Visible && stack.Text.Text.StartsWith("Clan Premium\n3 days 2 hours left"), "the clan premium bar shows the clan's time left: " + stack.Text.Text.Replace("\n", " / "));
                await Shot("clan-open", Group());
                await Click(clan.GetGlobalRect().GetCenter()); await Away();
                await Click(stack.BonusBar.GetGlobalRect().GetCenter()); await Away();
                Require(box.Visible && stack.OpenKey == ClassicNoticeBars.BonusKey && stack.Text.Text == bonusLines.Tooltip && !stack.Text.Text.Contains("%d"),
                    "the clan bonus bar shows the client's bonus text: " + stack.Text.Text.Replace("\n", " / "));
                await Shot("clan-bonus-open", Group());
                await Click(stack.BonusBar.GetGlobalRect().GetCenter()); await Away();
                Require(!box.Visible, "pressing the clan bonus bar again closes the box");
            }
            await Hover(chips[0].GetGlobalRect().GetCenter() - new Vector2(30, 0));
            await Shot("group-" + tag + "-hover", Group());
            await Away();
            if (!expanded)
            {
                // The knob is the client's own toggle: it expands the column.
                await Click(k.GetCenter()); await Away();
                Require(Game.Session.Zone.EventTitles.Expanded && column.GetChildren().OfType<PanelContainer>().Count() == 3, "the + knob expands the notices through the client");
                Require(Descendants(column.GetChildren().OfType<PanelContainer>().First()).OfType<Button>().Single().GetThemeStylebox("normal") is StyleBoxTexture, "the expanded first bar shows the − knob");
                await Frames(4);
                await Shot("group-knob-expanded", Group());
                layer.RemoveChild(column); column.QueueFree(); stack.QueueFree(); world.Free(); await Frames(2);
                continue;
            }
            // A notice: its message in the box under the GM bar; the bar shows its pressed art; again closes it.
            var notice = chips[0]; string title = Descendants(notice).OfType<Label>().Single().Text;
            await Click(notice.GetGlobalRect().GetCenter() - new Vector2(30, 0)); await Away();
            Require(box.Visible && stack.OpenKey == ClassicNoticeBars.KeyOf(title), "a notice bar opens the content box with its notice");
            Require(stack.Text.Text == Game.Session.Zone.EventTitles.Entries[0].Message, "the box shows the notice's message");
            var clip = stack.Text.GetParent<Control>().GetGlobalRect();
            Require(Inside(box.GetGlobalRect(), clip) && clip.Size.X == 141 && clip.Size.Y <= 139 && clip.Size.Y > 139 - stack.Text.GetLineHeight()
                && Mathf.IsEqualApprox(clip.Size.Y % stack.Text.GetLineHeight(), 0), "the text area is the original text_content rect, cut to whole lines " + clip);
            await Shot("notice-open", Group());
            // A long message scrolls by lines with the art's buttons.
            Game.Session.Zone.EventTitles.Show(title, string.Join(" ", Enumerable.Repeat("Siege practice tonight at nine, meet at the Moradon gate with full potions and scrolls.", 6)));
            stack.Refresh(); await Frames(3);
            Require(stack.MaxScroll > 0 && stack.Scroll == 0, "a long message can scroll (" + stack.MaxScroll + " lines)");
            var down = Descendants(box).OfType<TextureButton>().First(x => x.Name == "notice_scroll_down");
            await Click(down.GetGlobalRect().GetCenter()); await Click(down.GetGlobalRect().GetCenter()); await Away();
            Require(stack.Scroll == 2 && box.Visible, "the down arrow scrolls by a line (" + stack.Scroll + ")");
            var thumb = Descendants(box).OfType<Control>().First(x => x.Name == "notice_scroll_thumb");
            Require(thumb.Position.Y > 0, "the thumb follows the text");
            await Shot("notice-scrolled", box.GetGlobalRect().Grow(4));
            await Click(notice.GetGlobalRect().GetCenter() - new Vector2(30, 0)); await Away();
            Require(!box.Visible && stack.OpenKey == null, "pressing the open bar again closes the box");
            // GM List: empty for now, filled through NativeGmList.
            await Click(gm.GetGlobalRect().GetCenter()); await Away();
            Require(box.Visible && stack.OpenKey == ClassicNoticeBars.GmKey && stack.Text.Text == L.NoGms, "the GM List bar opens the box with '" + L.NoGms + "'");
            await Shot("gm-empty", Group());
            NativeGmList.Set(new[] { "[GM]Moira", "[GM]Hepa", "[GM]Clepio" }); await Frames(3);
            Require(stack.Text.Text == "[GM]Moira\n[GM]Hepa\n[GM]Clepio", "the open GM list follows NativeGmList");
            await Shot("gm-names", Group());
            // A removed notice closes its open box; with no notice the GM bar follows the time box.
            await Click(notice.GetGlobalRect().GetCenter() - new Vector2(30, 0)); await Away();
            Game.Session.Zone.EventTitles.Clear();
            // The preview world does not listen to the change event; the client's own refresh runs as live.
            DetailCall(world, "EventTitlesWindow.Refresh");
            ((Action?)Native.Get<object>(Game.Session.Zone, "EventTitlesChangedEvent"))?.Invoke();
            await Frames(6);
            Require(!box.Visible, "a removed notice closes its box");
            Require(Mathf.Abs(gm.GetGlobalRect().Position.Y - time.GetGlobalRect().End.Y - L.BarGap) < .5f, "without notices the GM List bar follows the time box " + gm.GetGlobalRect());
            await Shot("group-no-notices", Group());
            NativeGmList.Clear(); Premium(0, 0, 0);
            layer.RemoveChild(column); column.QueueFree(); stack.QueueFree(); world.Free(); await Frames(2);
        }

        var result = new { nation, variant, localRoot = Plugin.Kit.Local.Root, checks, screens, bounds };
        File.WriteAllText(System.IO.Path.Combine(output, prefix + "-myko-hud.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"MYKO_HUD_AUDIT_OK: {variant}, {checks.Count} checks, {screens.Count} screens");
    }
}
