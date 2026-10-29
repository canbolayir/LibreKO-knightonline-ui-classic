using Godot;
using LibreKO;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;
using Spec = KnightOnlineUiClassic.Layout.ClassicOverlayLayout;

// Native world overlays without a window id: confirmation prompts, the death notice, broadcast
// banners, the zone chip, war score strips and the loot window. Each overlay is
// captured once in its native look, then prepared and skinned and captured in its main states.
public partial class Preview
{
    private async Task CaptureWorldOverlaysAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/world-overlays");
        System.IO.Directory.CreateDirectory(output);
        string prefix = nation == 1 ? "karus" : "human";
        var window = GetWindow(); window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled; window.Size = new Vector2I(1280, 800);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(1275, 795), GlobalPosition = new Vector2(1275, 795) });
        // A dark ground with a light band: banners and plaques must read over both.
        AddChild(new ColorRect { Color = new Color("2f332a"), Size = new Vector2(1280, 800), MouseFilter = MouseFilterEnum.Ignore });
        AddChild(new ColorRect { Color = new Color("b2ac98"), Position = new Vector2(0, 120), Size = new Vector2(1280, 130), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        foreach (string pack in new[] { "build/client/LibreKO.pck", "build/client/source-content/knightonline.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../" + pack), false)) throw new Exception("Missing world overlay audit pack: " + pack);
        LibreKO.Domain.ItemData.EnsureLoaded();
        var catalog = LibreKO.Domain.ItemData.All().Where(i => i.Slot < 100 && !string.IsNullOrWhiteSpace(i.Name)).OrderBy(i => i.Id).ToArray();
        int stackable = catalog.Where(i => i.Countable > 0).OrderByDescending(i => i.Name.Contains("Potion")).First().Id;
        var singles = catalog.Where(i => i.Countable == 0).DistinctBy(i => i.Icon).Select(i => i.Id).Take(3).ToList();
        singles.Add(catalog.Where(i => i.Countable == 0).OrderByDescending(i => i.Name.Length).First().Id);
        var world = PreviewFixtures.Prepared(new WorldRoot());
        T Field<T>(string name) => (T)Native.Get<object>(world, name)!;
        void SetField(string name, object? value) => Native.Set(world, name, value == null ? null : Convert.ChangeType(value, Native.MemberType(world, name)!));
        void Call(string method, params object?[] args) => DetailCall(world, method, args);
        Call("DeathDialog.BuildDeathDialog"); Call("TownRecall.Build"); Call("Challenge.BuildPrompt"); Call("PartyInvites.BuildPrompts");
        Call("UpgradeNotice.BuildBanner"); Call("Shout.BuildBanner"); Call("BattleEvent.BuildUi");
        Call("ClanBattle.BuildBanner"); Call("ZoneAbility.BuildChip");
        SetField("BorderDefenseWar.HudLayer", new CanvasLayer { Layer = 95 }); world.AddChild(Field<CanvasLayer>("BorderDefenseWar.HudLayer")); Call("BorderDefenseWar.BuildBdwScoreBanner");
        Call("Loot.BuildWindow"); Call("Loot.BuildPrompt");
        foreach (var layer in world.GetChildren().OfType<CanvasLayer>().ToArray()) layer.Reparent(this);

        var checks = new List<string>(); var failures = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (valid) checks.Add(text); else { failures.Add(text); GD.PushError("WORLD_OVERLAYS_AUDIT: " + text); } }
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        object R(Rect2 r) => new { x = r.Position.X, y = r.Position.Y, w = r.Size.X, h = r.Size.Y };
        async Task Shot(string state, params (string Name, Rect2 Rect)[] bounds)
        {
            await Frames();
            string file = prefix + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output, file));
            screens.Add(new { file, state, bounds = bounds.ToDictionary(b => b.Name, b => R(b.Rect)) });
        }
        Rect2 Global(Control c) => c.GetGlobalRect();
        Rect2 Win(Window w) => new(w.Position, w.Size);
        bool Inside(Rect2 outer, Rect2 inner) => outer.Grow(0.01f).Encloses(inner);
        bool Whole(Rect2 r) => r.Position == r.Position.Round() && r.Size == r.Size.Round();
        var viewport = GetViewportRect();
        // A label's text fits its rectangle: one line within the width, wrapped lines within the height.
        bool Fits(Label label)
        {
            var font = label.GetThemeFont("font"); int size = label.GetThemeFontSize("font_size");
            if (label.AutowrapMode == TextServer.AutowrapMode.Off)
                return font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, size).X <= label.Size.X + 0.5f;
            int lines = label.GetLineCount();
            return lines * font.GetHeight(size) + (lines - 1) * label.GetThemeConstant("line_spacing") <= label.Size.Y + 0.5f;
        }
        string Measure(Label label) => label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")) + " lines " + label.GetLineCount();
        void Expected(Control control, Control parent, string what)
        {
            var expected = control.GetMeta(ClassicWorldOverlays.ExpectedRect).AsRect2();
            var actual = new Rect2(control.GlobalPosition - parent.GlobalPosition, control.Size);
            Require(actual == expected, $"{what}: rendered bounds {actual} match the declared {expected}");
            Require(Whole(control.GetGlobalRect()), $"{what}: whole-pixel geometry");
        }
        void ButtonArt(Button button, string what)
        {
            var caption = button.GetNodeOrNull<Label>("caption");
            Require(caption != null && caption.Text == button.Text && caption.IsVisibleInTree(), $"{what}: the native text '{button.Text}' shows on the art caption");
            Require(button.GetThemeStylebox("normal") is StyleBoxTexture && button.GetThemeStylebox("hover") is StyleBoxTexture
                && button.GetThemeStylebox("pressed") is StyleBoxTexture && button.GetThemeStylebox("disabled") is StyleBoxTexture, $"{what}: original art in every state");
            if (caption != null) Require(Fits(caption), $"{what}: caption fits");
        }

        // ---- Native look (before) ---------------------------------------------------------------------
        var invite = Field<ConfirmationDialog>("PartyInvites.inviteAskDialog");
        var duel = Field<ConfirmationDialog>("Challenge.askDialog");
        var recall = Field<ConfirmationDialog>("TownRecall.dialog");
        var deathLayer = Field<CanvasLayer>("DeathDialog.Layer");
        var deathPanel = Field<PanelContainer>("DeathDialog.Panel");
        var upgradeLayer = Field<CanvasLayer>("UpgradeNotice.layer"); var upgradeLabel = Field<Label>("UpgradeNotice.label");
        var shoutLayer = Field<CanvasLayer>("Shout.layer"); var shoutLabel = Field<Label>("Shout.label");
        var battleBanner = Field<PanelContainer>("BattleEvent.battleeventBanner"); var battleBannerLabel = Field<Label>("BattleEvent.battleeventBannerLbl");
        var battleResult = Field<PanelContainer>("BattleEvent.battleeventResult"); var battleResultLabel = Field<Label>("BattleEvent.battleeventResultLbl");
        var clanBattle = Field<PanelContainer>("ClanBattle.panel"); var clanBattleLabel = Field<Label>("ClanBattle.label");
        var chip = Field<PanelContainer>("ZoneAbility.ZoneabilityChip"); var chipLabel = Field<Label>("ZoneAbility.zoneabilityTypeLbl");
        var board = Field<WarScoreStrip>("BattleEvent.BattleeventBoard"); var bdw = Field<WarScoreStrip>("BorderDefenseWar.BdwScoreBanner");
        var lootLayer = Field<CanvasLayer>("Loot.layer"); var lootPanel = Field<PanelContainer>("Loot.Panel");
        var lootCells = Field<ItemSlotView[]>("Loot.cells"); var lootPrompt = Field<Label>("Loot.prompt");

        void ShowBanners()
        {
            upgradeLabel.Text = "Guardian has successfully upgraded Iron Bow(+7)!"; upgradeLayer.Visible = true;
            battleBannerLabel.Text = "The Battle Zone is now OPEN!  (Napies Gorge)"; battleBanner.Visible = true;
            clanBattleLabel.Text = "The clan battle has begun!"; clanBattle.Visible = true;
            chipLabel.Text = "PK Zone"; chipLabel.AddThemeColorOverride("font_color", UiTheme.Bad); chip.Visible = true;
            board.Centre = "12:34"; board.SetScores(1250, 980); board.Visible = true;
            bdw.Centre = "Border Defense War"; bdw.SetScores(14, 22); bdw.SetStatus("Altar held by El Morad", UiTheme.Good); bdw.Visible = true;
            lootPrompt.Visible = true;
        }
        void PlaceBanners()
        {
            battleBanner.Position = new Vector2(Mathf.Round((viewport.Size.X - battleBanner.Size.X) / 2), 268);
            clanBattle.Position = new Vector2(Mathf.Round((viewport.Size.X - clanBattle.Size.X) / 2), 316);
            board.Position = new Vector2(Mathf.Round((viewport.Size.X - board.Size.X) / 2), 380);
            bdw.Position = new Vector2(Mathf.Round((viewport.Size.X - bdw.Size.X) / 2), 460);
            lootPrompt.Position = new Vector2(520, 560);
        }
        void HideBanners()
        {
            foreach (var layer in new[] { upgradeLayer, shoutLayer }) layer.Visible = false;
            foreach (var c in new Control[] { battleBanner, battleResult, clanBattle, chip, board, bdw, lootPrompt }) c.Visible = false;
        }
        invite.DialogText = "Guardian invites you to a party.\nJoin?"; invite.PopupCentered();
        await Shot("native-prompt"); invite.Hide();
        Call("DeathDialog.OnExpLoss", 1_234_567L); Call("DeathDialog.Show");
        await Shot("native-death"); Call("DeathDialog.Hide");
        ShowBanners(); await Frames(); PlaceBanners();
        await Shot("native-banners"); HideBanners();
        var gold = new LootEntry(LootWire.GoldItemId, 1520);
        // The client places the box at its 3D anchor; the audit shows the contents as the client's refresh does.
        void ShowLoot(List<LootEntry> entries)
        {
            var list = Field<List<LootEntry>>("Loot.Entries"); list.Clear(); list.AddRange(entries);
            Call("Loot.RefreshWindow"); lootLayer.Visible = true;
        }
        SetField("Loot.openBundleId", 7);
        ShowLoot(new List<LootEntry> { gold, new(singles[0], 1), new(stackable, 25) });
        lootPanel.Position = new Vector2(560, 260);
        await Shot("native-loot"); Call("Loot.Close");

        // ---- Classic ----------------------------------------------------------------------------------
        NativeOverlays.Prepare(world);
        await Frames(2);
        foreach (var (node, what) in new (Node, string)[] { (invite, "party invite"), (duel, "duel challenge"), (recall, "town recall"), (deathPanel, "death notice"),
            (upgradeLabel.GetParent().GetParent(), "upgrade banner"), (battleBanner, "battle banner"), (clanBattle, "clan battle banner"), (chip, "zone chip"),
            (board, "battle score"), (bdw, "border war score"), (lootPanel, "loot window"), (lootPrompt, "loot prompt") })
            Require(node.HasMeta("classic_overlay"), $"{what} has the Classic skin");

        async Task Prompt(ConfirmationDialog dialog, string state, string title, bool decline)
        {
            await Frames();
            var art = dialog.GetNode<Control>("classic_prompt/message_art");
            var heading = dialog.GetNode<Label>("classic_prompt/prompt_title");
            var rect = Win(dialog);
            Require(dialog.Visible && dialog.Borderless && rect.Size.X == Spec.Message.Width, $"{state}: borderless message box {rect.Size}");
            Require(Inside(viewport, rect), $"{state}: inside the viewport");
            Require(art.Size == new Vector2(Spec.Message.Width, rect.Size.Y) && art.Position == Vector2.Zero, $"{state}: message art covers the box");
            Require(heading.Text == title && heading.Position == Spec.Message.Title.Position && Fits(heading), $"{state}: native title '{title}' in the title band");
            var label = dialog.GetLabel();
            var text = new Rect2(label.Position, label.Size);
            Require(Inside(new Rect2(Spec.Message.Text.Position, new Vector2(Spec.Message.Text.Size.X, rect.Size.Y - Spec.Message.Text.Position.Y - Spec.Message.ButtonBottom - Spec.Message.Button.Y)), text),
                $"{state}: message {text} inside the message band");
            Require(Fits(label), $"{state}: message wraps inside its band");
            // Content-sized: the message is its wrapped lines at the common top, the buttons the common gap under it.
            int lines = label.GetLineCount();
            float wrapped = lines * label.GetLineHeight() + (lines - 1) * label.GetThemeConstant("line_spacing");
            Require(text.Position == new Vector2(Spec.Message.Text.Position.X, Spec.Message.TextTop) && Mathf.Abs(text.Size.Y - wrapped) < 1f
                && Mathf.Abs(rect.Size.Y - Spec.Message.HeightFor(text.Size.Y)) <= 1, $"{state}: the box fits its {lines} lines ({text} in {rect.Size})");
            var ok = dialog.GetOkButton(); var cancel = dialog.GetCancelButton();
            // Controls of an embedded window report global rectangles in window space.
            var okRect = ok.GetGlobalRect(); var cancelRect = cancel.GetGlobalRect();
            Require(okRect.Size == Spec.Message.Button && cancelRect.Size == Spec.Message.Button, $"{state}: buttons use the 97 x 29 art ({okRect.Size}, {cancelRect.Size})");
            Require(okRect == Spec.Message.Left(rect.Size.Y) && cancelRect == Spec.Message.Right(rect.Size.Y), $"{state}: buttons at the original two-button places ({okRect}, {cancelRect})");
            ButtonArt(ok, state + " confirm"); ButtonArt(cancel, state + " cancel");
            Require(ok.HasFocus() && ok.GetThemeStylebox("focus") is StyleBoxEmpty, $"{state}: the confirm button keeps the native keyboard focus without a focus frame");
            await Shot(state, ("box", rect), ("ok", okRect), ("cancel", cancelRect), ("message", text));
            bool confirmed = false, cancelled = false;
            dialog.Confirmed += () => confirmed = true; dialog.Canceled += () => cancelled = true;
            (decline ? cancel : ok).EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Require(!dialog.Visible && (decline ? cancelled : confirmed), $"{state}: the native {(decline ? "cancel" : "confirm")} callback runs and the prompt closes");
            if (dialog.Visible) dialog.Hide();
        }
        SetField("PartyInvites.invitePending", true);
        invite.DialogText = "Guardian invites you to a party.\nJoin?"; invite.PopupCentered();
        await Prompt(invite, "party-invite", "Party Invite", false);
        Require(!Field<bool>("PartyInvites.invitePending"), "party invite: Join answers the native invite");
        SetField("Challenge.requestPending", true);
        duel.DialogText = "Extraordinarylongname challenges you to a duel.\nAccept and warp to the arena?"; duel.PopupCentered();
        await Prompt(duel, "duel-challenge", "Duel challenge", true);
        Require(!Field<bool>("Challenge.requestPending"), "duel challenge: closing declines the native request");
        recall.PopupCentered();
        await Prompt(recall, "town-recall", "Town Recall", true);

        // Death notice: experience loss, the pending (disabled) state and the native OK.
        Call("DeathDialog.OnExpLoss", 1_234_567_890L); Call("DeathDialog.Show");
        await Frames();
        var death = deathPanel.GetNode<Control>("classic_death");
        var deathLabels = death.GetChildren().OfType<Label>().Where(l => l.HasMeta(ClassicWorldOverlays.ExpectedRect)).ToArray();
        var deathOk = death.GetChildren().OfType<Button>().Single();
        async Task DeathState(string state)
        {
            await Frames();
            var hint = deathLabels[1];
            var centre = (viewport.Size - Spec.Death.Size(hint.Size.Y)) / 2;
            Require(hint.Position.Y == Spec.Message.TextTop && hint.Size.Y >= hint.GetLineCount() * hint.GetLineHeight() && deathOk.Position.Y == hint.GetRect().End.Y + Spec.Message.ActionGap,
                $"{state}: the box fits the hint ({hint.GetRect()}, OK {deathOk.GetRect()})");
            Require(death.Size == Spec.Death.Size(hint.Size.Y) && Math.Abs(Global(death).Position.X - centre.X) <= 1 && Math.Abs(Global(death).Position.Y - centre.Y) <= 1 && Whole(Global(death)),
                $"{state}: centred original message box on whole pixels {Global(death)}");
            foreach (var label in deathLabels) { Expected(label, death, $"{state} '{label.Text}'"); Require(Fits(label), $"{state}: '{label.Text}' fits ({Measure(label)} in {label.Size})"); }
            Expected(deathOk, death, state + " OK"); ButtonArt(deathOk, state + " OK");
            await Shot(state, ("box", Global(death)), ("ok", Global(deathOk)));
        }
        Require(deathLabels.Length == 2, "death notice: loss and hint labels placed");
        await DeathState("death");
        // Resurrection scrolls in the bag: the town row and one row per scroll between the hint and OK.
        var scrollType = typeof(WorldRoot).Assembly.GetType("LibreKO.Domain.ScrollSkill")!;
        var offer = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(scrollType))!;
        foreach (var (id, name) in new[] { (500, "Blessed Resurrection Scroll"), (501, "Resurrection Scroll of an Extraordinarily Long Name") })
            offer.Add(Activator.CreateInstance(scrollType, id, name, 0, 0, 0));
        Native.Set(world, "DeathDialog.scrolls", offer);
        SetField("DeathDialog.pick", 1);
        Call("DeathDialog.RefreshRows"); Call("DeathDialog.RefreshText"); Call("DeathDialog.ArmRespawnButton", "Please confirm the availability.");
        await Frames(3);
        var choices = death.GetNode<VBoxContainer>("death_choices");
        var choiceRows = choices.GetChildren().OfType<Button>().Where(b => b.Visible).ToArray();
        Require(choices.Visible && choiceRows.Length == 3, "death notice: the town row and two scroll rows are offered");
        Expected(choices, death, "death notice choices");
        Require(choiceRows.All(r => r.Size.Y == Spec.Death.RowHeight && Inside(Global(choices), Global(r))), "death notice: whole 22-pixel choice rows inside the message band");
        Require(Global(choices).End.Y + Spec.Message.ActionGap <= Global(deathOk).Position.Y + .5f && Global(choices).Position.Y >= Global(deathLabels[1]).End.Y, "death notice: choices between the hint and OK");
        Require(choiceRows[1].GetThemeColor("font_color") == new Color(1, 1, 0) && choiceRows[0].GetThemeColor("font_color") == Colors.White, "death notice: the chosen row keeps the native yellow");
        Require(choiceRows.All(r => r.TooltipText == r.Text && r.ClipText), "death notice: long scroll names are clipped with the full text as tooltip");
        await Shot("death-scrolls", ("box", Global(death)), ("choices", Global(choices)), ("ok", Global(deathOk)));
        choiceRows[0].EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left });
        await Frames(2);
        Require(Field<int>("DeathDialog.pick") == 0 && choiceRows[0].GetThemeColor("font_color") == new Color(1, 1, 0), "death notice: a row click is the native choice");
        await Shot("death-scrolls-town", ("box", Global(death)));
        Native.Set(world, "DeathDialog.scrolls", Activator.CreateInstance(typeof(List<>).MakeGenericType(scrollType)));
        Call("DeathDialog.RefreshRows"); Call("DeathDialog.RefreshText"); Call("DeathDialog.ArmRespawnButton", "Press OK to teleport back to the re-spawn point.");
        await Frames(3);
        Require(!choices.Visible && Global(death).Size == Spec.Death.Size(deathLabels[1].Size.Y), "death notice: without scrolls the box returns to the hint and OK");
        Call("DeathDialog.MarkRespawnPending");
        await Frames(2);
        Require(deathOk.Disabled && deathOk.GetNode<Label>("caption").GetThemeColor("font_color") != Colors.White, "death notice: the pending OK is disabled and greyed");
        await DeathState("death-pending");
        Call("DeathDialog.ArmRespawnButton", "Press OK to teleport back to the re-spawn point.");
        SetField("Avatar.Dead", false);
        deathOk.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Require(!deathLayer.Visible, "death notice: OK runs the native respawn callback");

        // Banners, chip and score strips over light and dark ground.
        ShowBanners(); await Frames(); PlaceBanners();
        await Frames();
        foreach (var label in new[] { upgradeLabel, battleBannerLabel, clanBattleLabel, lootPrompt })
            Require(label.GetThemeFont("font") == Plugin.Kit.Bold && label.GetThemeConstant("outline_size") > 0 && Fits(label), $"banner '{label.Text}': outlined Arial Bold that fits");
        foreach (var plate in new PanelContainer[] { battleBanner, clanBattle })
            Require(plate.GetThemeStylebox("panel") is StyleBoxEmpty, $"banner {plate.Name}: no flat panel");
        Require(chipLabel.GetThemeColor("font_color") == ClassicWorldOverlays.PlaqueInk && chip.Size.Y == Spec.Plaque.Height && Inside(viewport, Global(chip)),
            $"zone chip: plaque height {chip.Size.Y} (label {chipLabel.GetCombinedMinimumSize()}) and plaque ink over the native tint");
        var chipText = chipLabel.GetThemeFont("font").GetStringSize(chipLabel.Text, HorizontalAlignment.Left, -1, chipLabel.GetThemeFontSize("font_size")).X;
        float left = Global(chipLabel).Position.X + (chipLabel.Size.X - chipText) / 2 - Global(chip).Position.X, right = Global(chip).End.X - (Global(chipLabel).Position.X + (chipLabel.Size.X + chipText) / 2);
        Require(Math.Abs(left - right) <= 1, $"zone chip: name centred on the plaque ({left:0.#} / {right:0.#})");
        foreach (var strip in new[] { board, bdw })
        {
            Require(Inside(viewport, Global(strip)), $"{strip.Name}: inside the viewport");
            foreach (var label in Descendants(strip).OfType<Label>().Where(l => l.IsVisibleInTree()))
                Require(Inside(Global(strip), Global(label)) && Fits(label) && label.GetThemeFont("font") == Plugin.Kit.Bold, $"{strip.Name}: '{label.Text}' inside the plaque");
            var bars = Descendants(strip).OfType<ColorRect>().ToArray();
            Require(bars.All(b => b.Color.A == 0 && b.Size.Y == Spec.Plaque.BarHeight), $"{strip.Name}: original bar art at {Spec.Plaque.BarHeight} px");
        }
        await Shot("banners", ("upgrade", Global(upgradeLabel)), ("battle", Global(battleBanner)), ("clan", Global(clanBattle)), ("chip", Global(chip)),
            ("score", Global(board)), ("bdw", Global(bdw)), ("lootPrompt", Global(lootPrompt)));
        HideBanners();
        shoutLabel.Text = "[Shout] Extraordinarylongname: selling Iron Bow(+7), whisper me in Moradon!"; shoutLayer.Visible = true;
        battleResultLabel.Text = "VICTORY!"; battleResult.Visible = true;
        await Frames();
        Require(Fits(shoutLabel) && Inside(viewport, Global(shoutLabel)), "shout banner: long line fits the viewport");
        await Shot("banners-result", ("shout", Global(shoutLabel)), ("result", Global(battleResult)));
        HideBanners();

        // Loot: gold and two items, then a full box of twelve; cells are the client's own (click takes, right click sets a rule).
        SetField("Loot.openBundleId", 7);
        ShowLoot(new List<LootEntry> { gold, new(singles[0], 1), new(stackable, 25) });
        lootPanel.Position = new Vector2(560, 260);
        await Frames();
        var loot = lootPanel.GetNode<Control>("classic_loot");
        async Task LootState(string state, int filled)
        {
            await Frames();
            Require(lootPanel.Size == Spec.Loot.Size && loot.Size == Spec.Loot.Size && Inside(viewport, Global(loot)), $"{state}: original grid size {lootPanel.Size}");
            foreach (var name in new[] { "loot_take_all", "loot_filter", "loot_close" })
            {
                var control = loot.FindChild(name, true, false) as Control;
                Require(control != null && control.IsVisibleInTree(), $"{state}: {name} visible");
                if (control != null)
                {
                    Expected(control, loot, $"{state} {name}");
                    Require(Inside(new Rect2(Global(loot).Position, Spec.Loot.Header.Size), Global(control)), $"{state}: {name} on the plaque");
                    if (control is Button { Text.Length: > 0 } button)
                    {
                        ButtonArt(button, $"{state} {name}");
                        // Take all and Filter (which opens the client's Loot Filter window) keep their native callbacks.
                        Require(button.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0, $"{state}: {name} keeps its native action");
                    }
                }
            }
            for (int i = 0; i < lootCells.Length; i++)
            {
                var cell = lootCells[i];
                Require(cell.GetParent() == loot, $"{state}: cell {i} on the Classic grid");
                Expected(cell, loot, $"{state} cell {i}");
                Require(cell.GetThemeStylebox("panel") is StyleBoxEmpty, $"{state}: cell {i} shows the original art, not a modern frame");
                Require(cell.Item.IsEmpty == (i >= filled), $"{state}: cell {i} {(i < filled ? "shows its item" : "is empty")}");
            }
            await Shot(state, ("loot", Global(loot)));
        }
        await LootState("loot", 3);
        string Count(int i) => lootCells[i].GetChildren().OfType<Label>().First(l => l.Name == "item_count").Text;
        Require(Count(0) == "48210" || Count(0) == "1520", "loot: gold shows its amount");
        Require(Count(2) == "25" && Count(1) == "", "loot: stack counts from the native cells; single items show none");
        var full = new List<LootEntry> { gold, new(singles[0], 1), new(stackable, 25), new(singles[1], 1), new(singles[3], 1), new(singles[2], 1) };
        for (int i = 0; full.Count < 12; i++) full.Add(new(catalog[i * 7].Id, 1 + i));
        ShowLoot(full);
        await LootState("loot-full", 12);
        Require(Global(lootCells[6]).Position.X >= Global(loot).Position.X + Spec.Loot.GridWidth, "loot: entries 7 to 12 fill the second grid");
        var hovered = lootCells[4];
        Input.WarpMouse(Global(hovered).GetCenter());
        GetViewport().PushInput(new InputEventMouseMotion { Position = Global(hovered).GetCenter(), GlobalPosition = Global(hovered).GetCenter() });
        await Frames(3);
        await Shot("loot-hover", ("loot", Global(loot)), ("cell", Global(hovered)));
        GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(5, 5), GlobalPosition = new Vector2(5, 5) });
        int taken = -1;
        var takeSpy = Native.Get<System.Delegate>(hovered, "Clicked");
        hovered.Clicked += c => taken = c.Index;
        hovered._GuiInput(new InputEventMouseButton { Pressed = false, ButtonIndex = MouseButton.Left });
        Require(taken == hovered.Index && takeSpy != null, "loot: a cell click runs the native take");
        ((Button)loot.FindChild("loot_close", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Require(!lootLayer.Visible, "loot: Close runs the native close");

        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-world-overlays.json"),
            JsonSerializer.Serialize(new { checks, failures, screenshots = screens, liveTransactions = false }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var layer in GetChildren().OfType<CanvasLayer>().ToArray()) layer.Free();
        world.Free(); net.Free();
        await Frames(2);
        if (failures.Count > 0) throw new Exception("WORLD_OVERLAYS_AUDIT failed: " + string.Join("; ", failures));
        GD.Print("WORLD_OVERLAYS_AUDIT_OK " + checks.Count);
    }
}
