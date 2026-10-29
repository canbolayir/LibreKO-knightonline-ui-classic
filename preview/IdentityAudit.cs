using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureIdentityAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        PluginHost.Ui.ReplaceDialogs(request => new ClassicServiceNotice(request));
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool condition, string text) { if (!condition) throw new Exception("IDENTITY_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Capture(HudWindow window, string state)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete frame fits viewport");
            var panel = window.GetChildren().OfType<ClassicIdentityPanel>().Single();
            Require(window.Size == panel.CustomMinimumSize, state + " original complete frame proportions");
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("identity_expected_rect")))
            {
                Require(control.GetRect() == control.GetMeta("identity_expected_rect").AsRect2(), state + " measured bounds " + control.Name + " = " + control.GetRect());
                Require(new Rect2(Vector2.Zero, panel.Size).Encloses(control.GetRect()), state + " content inside frame " + control.Name);
            }
            foreach (var input in Descendants(panel).OfType<LineEdit>()) Require(input.TextDirection == Control.TextDirection.Ltr, state + " input uses left-to-right typing");
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }
        async Task<(WorldRoot, HudWindow, ClassicIdentityPanel)> Build(string method, params object[] args)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, method, args)!;
            AddChild(window); ClassicIdentitySkin.Apply(window.Body); await Frames();
            var panel = window.GetChildren().OfType<ClassicIdentityPanel>().Single();
            window.Position = new Vector2(100, 80); await Frames(2);
            var grip = Descendants(panel).OfType<Control>().Single(c => c.Name == "party_drag");
            Vector2 pointer = window.Position + new Vector2(12, 8), before = window.Position;
            grip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = pointer });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = pointer + new Vector2(35, 20) });
            window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, GlobalPosition = pointer + new Vector2(35, 20) });
            Require(window.Position == before + new Vector2(35, 20), window.Id + " original top grip preserves native dragging");
            return (world, window, panel);
        }
        var (clanWorld, clan, clanPanel) = await Build("BuildClanCreateUiPreview");
        await Capture(clan, "clan-name");
        var name = (LineEdit)DetailField(clanWorld, "ClanCreate.nameInput")!;
        name.Text = "x"; name.EmitSignal(LineEdit.SignalName.TextSubmitted, name.Text); await Frames();
        Require(DetailField(clanWorld, "ClanCreate.notice") == null, "Invalid clan name never opens spending confirmation");
        await Capture(clan, "clan-invalid");
        name.Text = "canCLAN"; name.EmitSignal(LineEdit.SignalName.TextSubmitted, name.Text);
        var notice = (Notice)DetailField(clanWorld, "ClanCreate.notice")!;
        Require(notice != null, "Clan Enter opens the original second-stage fee confirmation");
        clanWorld.RemoveChild(notice!); AddChild(notice!); await Frames();
        var fee = notice!.GetChildren().OfType<ClassicServiceNotice>().Single();
        DetailCall(clanWorld, "ClanCreate.Submit");
        Require(ReferenceEquals(notice, DetailField(clanWorld, "ClanCreate.notice")), "Repeated clan submit does not create duplicate confirmations");
        GetViewport().GetTexture().GetImage().SavePng(output + "/" + (nation == 1 ? "karus" : "human") + "-clan-fee.png");
        screens.Add(new { state = "clan-fee", file = (nation == 1 ? "karus" : "human") + "-clan-fee.png" });
        fee._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true }); await Frames();
        Require(DetailField(clanWorld, "ClanCreate.notice") == null && clan.Visible && name.Text == "canCLAN", "Clan fee Escape returns to the unchanged name entry");
        name.EmitSignal(LineEdit.SignalName.TextSubmitted, name.Text);
        notice = (Notice)DetailField(clanWorld, "ClanCreate.notice")!; clanWorld.RemoveChild(notice); AddChild(notice); await Frames();
        notice.GetChildren().OfType<ClassicServiceNotice>().Single()._Input(new InputEventKey { Keycode = Key.Enter, Pressed = true }); await Frames();
        Require(!clan.Visible && DetailField(clanWorld, "ClanCreate.notice") == null, "Clan fee Enter submits and closes the name entry");
        clan.Free(); clanWorld.Free();

        var (renameWorld, rename, renamePanel) = await Build("BuildNameChangeUiPreview");
        await Capture(rename, "name-change");
        var edit = (LineEdit)DetailField(renameWorld, "NameChange.edit")!;
        edit.Text = "a"; edit.EmitSignal(LineEdit.SignalName.TextSubmitted, edit.Text); await Frames();
        Require(((Label)DetailField(renameWorld, "NameChange.status")!).Text == "Name must be 3-20 characters.", "Name Enter retains the native length validation");
        await Capture(rename, "name-invalid");
        renamePanel._UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Require(!rename.Visible && !(bool)DetailField(renameWorld, "NameChange.shown")!, "Name Escape invokes the native close behavior");
        rename.Free(); renameWorld.Free();

        // The clan ID change reuses the rename window; the client switches its mode when it opens.
        var (clanRenameWorld, clanRename, clanRenamePanel) = await Build("BuildClanNameChangeUiPreview");
        await Capture(clanRename, "clan-name-change");
        var clanHint = Descendants(clanRenamePanel.Window).OfType<Label>().Single(l => l.Name == "name_change_hint");
        Require(clanHint.Text == ClassicIdentityPanel.ClanRenameHint, "Clan rename shows the Classic clan explanation");
        Require(Descendants(clanRenamePanel).OfType<Label>().Count(l => l.IsVisibleInTree() && l.Text == "Change Clan Name") == 1, "Clan rename shows its own title");
        Require(((LineEdit)DetailField(clanRenameWorld, "NameChange.edit")!).Text == "", "Clan rename starts with an empty name");
        DetailCall(clanRenameWorld, "NameChange.Open", false); await Frames();
        Require(clanHint.Text == ClassicIdentityPanel.RenameHint, "Reopening as character rename restores the character explanation");
        clanRenamePanel._UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Require(!clanRename.Visible, "Clan rename Escape invokes the native close behavior");
        clanRename.Free(); clanRenameWorld.Free();

        foreach (bool empty in new[] { false, true })
        {
            var (world, window, panel) = await Build("BuildDisguiseUiPreview", empty);
            await Capture(window, empty ? "transformation-empty" : "transformation-ready");
            var ok = Descendants(panel).OfType<Button>().Single(b => b.Name == "disguise_accept");
            Require(ok.Disabled == empty, "Transformation confirms only when a form exists " + empty);
            if (!empty)
            {
                var rows = (VBoxContainer)DetailField(world, "DisguiseWindow.formRows")!;
                Require(rows.GetChildren().OfType<Button>().All(b => b.Size.Y == 19), "Transformation uses original dense one-line rows");
                Require(rows.GetChildren().OfType<Button>().All(b => b.IsVisibleInTree() && b.Size.X >= 120), "Transformation form text is visible across the original list width");
                var formScroll = (ScrollContainer)rows.GetParent();
                var rail = Descendants(panel).OfType<ClassicIdentityScrollRail>().Single(r => r.Name == "list_monster_rail");
                Require(rail.IsVisibleInTree() && rail.Bar.MaxValue > rail.Bar.Page, "Long transformation lists scroll inside the fixed original area with the original scroll art");
                Require(formScroll.Size.Y % ClassicIdentityPanel.DisguiseRow == 0 && formScroll.Size.Y == 228, "Transformation list shows whole rows only");
                Require(formScroll.GetGlobalRect().End.X <= rail.GetGlobalRect().Position.X, "Transformation rows end before the original scroll bar");
                rows.GetChildren().OfType<Button>().ElementAt(1).EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                await Capture(window, "transformation-note");
                Require(((ScrollContainer)rows.GetParent()).Size.Y == 190, "Transformation notes reserve space without growing the window");
                Require(rail.Size.Y == 193 && rail.GetGlobalRect().End.Y <= Descendants(panel).OfType<Label>().Single(l => l.Name == "disguise_note").GetGlobalRect().Position.Y, "Transformation scroll art ends above the note");
                rail.Bar.Value = 7; await Frames();
                Require(rail.Bar.Value % ClassicIdentityPanel.DisguiseRow == 0, "Transformation list scrolls by whole rows");
                rail.Bar.Value = 0; await Frames();
                var groups = (VBoxContainer)DetailField(world, "DisguiseWindow.groupRows")!;
                groups.GetChildren().OfType<Button>().Last().EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                ok.EmitSignal(BaseButton.SignalName.Pressed);
                notice = world.GetChildren().OfType<Notice>().Single(); world.RemoveChild(notice); AddChild(notice); await Frames();
                Require(Descendants(notice).OfType<Label>().Any(l => l.Text.Contains("level")), "Transformation preserves the native level refusal before spending");
                notice.GetChildren().OfType<ClassicServiceNotice>().Single()._Input(new InputEventKey { Keycode = Key.Enter, Pressed = true }); await Frames();
                groups.GetChildren().OfType<Button>().First().EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                rows.GetChildren().OfType<Button>().First().EmitSignal(Control.SignalName.GuiInput,
                    new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true });
                notice = world.GetChildren().OfType<Notice>().Single(); world.RemoveChild(notice); AddChild(notice); await Frames();
                Require(Descendants(notice).OfType<Button>().Count() == 2, "Transformation double-click opens confirmation rather than immediately transforming");
                notice.GetChildren().OfType<ClassicServiceNotice>().Single()._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true }); await Frames();
                Require(window.Visible, "Transformation confirmation Escape preserves the current form and open list");
                Native.Set(Game.Session, "Self.TransformSkill", 600001);
                ok.EmitSignal(BaseButton.SignalName.Pressed);
                notice = world.GetChildren().OfType<Notice>().Single(); world.RemoveChild(notice); AddChild(notice); await Frames();
                Require(Descendants(notice).OfType<Label>().Any(l => l.Text.Contains("Transforming")), "Already transformed refusal remains native");
                notice.GetChildren().OfType<ClassicServiceNotice>().Single()._Input(new InputEventKey { Keycode = Key.Enter, Pressed = true }); await Frames();
                Native.Set(Game.Session, "Self.TransformSkill", 0);
            }
            panel._UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            Require(!window.Visible && !(bool)DetailField(world, "DisguiseWindow.shown")!, "Transformation Escape invokes native cancellation " + empty);
            window.Free(); world.Free();
        }
        // A member that still exists but changed type (the quest reward box became a flow container) is reported once,
        // as a missing member is, instead of silently reading as null.
        var reported = new List<string>(); Action<string> seen = reported.Add;
        KnightOnlineUiClassic.Native.Native.MemberMissing += seen;
        var probe = PreviewFixtures.Prepared(new WorldRoot());
        KnightOnlineUiClassic.Native.Native.Get<HBoxContainer>(probe, "Quest.rewardBox");
        KnightOnlineUiClassic.Native.Native.Get<HBoxContainer>(probe, "Quest.rewardBox");
        bool compatible = KnightOnlineUiClassic.Native.Native.Get<Container>(probe, "Quest.rewardBox") == null;
        KnightOnlineUiClassic.Native.Native.MemberMissing -= seen; probe.Free();
        Require(reported.Count(k => k == "LibreKO.World._questRewardBox") == 1 && compatible, "A native member read as another type is reported once; a compatible base type is not");
        string result = output + "/" + (nation == 1 ? "karus" : "human") + "-identity.json";
        System.IO.File.WriteAllText(result, JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Identity audit: " + checks.Count + " checks / " + screens.Count + " screens: " + result);
        offline.Free();
    }
}
