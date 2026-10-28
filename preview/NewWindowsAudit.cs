using System.Reflection;
using System.Text.Json;
using Godot;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic;
using LibreKO.Plugins;

/// <summary>
/// "new-windows-audit": the windows LibreKO e5f0e420 added outside the Classic page set (the item rental market,
/// Party Drops and the quickslot sets). Each client feature is built on its own, without a running world, and its
/// window is shown with the Classic frame the plugin extends it with. Both nations; the frame, the window bounds
/// and the restyled buttons and labels are checked on the rendered controls.
/// </summary>
public partial class Preview
{
    private async Task CaptureNewWindowsAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/new-windows-audit");
        System.IO.Directory.CreateDirectory(output);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        ItemData.EnsureLoaded();
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("NEW_WINDOWS_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int n = 8) { for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        object Slice(object owner, string name) => owner.GetType().GetProperty(name)!.GetValue(owner)!;
        object Feature(string type, params object?[] args) =>
            Activator.CreateInstance(Native.ClientType("LibreKO.Features." + type)!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null)!;

        var world = new WorldRoot();
        var windows = new (string Id, Func<object> Build, string Open, object?[] OpenArgs)[]
        {
            ("item_rental", () => Feature("Windows.ItemRentalFeature", world, Slice(Game.Session, "ItemRental"), Game.Session.Self, Slice(Game.Requests, "ItemRental")), "OnOpen", new object?[] { 0 }),
            ("party_drops", () => Feature("Hud.PartyLootFeature", world, Slice(Game.Session, "Loot"), Slice(Game.Session, "Party"), Game.Session.Self, System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Native.ClientType("LibreKO.Features.Hud.PartyFeature")!)), "Toggle", Array.Empty<object?>()),
            ("quickslot_saves", () => Feature("Windows.QuickslotSavesFeature", world, null, Game.Session.Self), "Toggle", Array.Empty<object?>()),
        };
        foreach (var (id, build, open, openArgs) in windows)
        {
            var feature = build();
            Native.Call(feature, "Build");
            var window = Native.Get<HudWindow>(feature, "Window")!;
            // The preview's PluginUi is not the host's, so the Classic extender the plugin registers for this id is applied here.
            Require(Plugin.UnlistedWindowIds.Contains(id) || PluginUi.KnownWindowIds.Contains(id), id + ": the plugin extends this window id");
            ClassicSkin.ExtendWindow(window.Body);
            var layer = (CanvasLayer)window.GetParent();
            layer.GetParent().RemoveChild(layer); AddChild(layer);
            await Frames();
            Native.Call(feature, open, openArgs);
            window.Visible = true;
            await Frames();
            window.Position = ((GetViewportRect().Size - window.Size) / 2).Round();
            await Frames(4);
            Require(window.Id == id, id + ": the client's window id");
            Require(window.Visible && GetViewportRect().Encloses(window.GetGlobalRect()), id + ": the whole window fits the screen " + window.GetGlobalRect());
            Require(Descendants(window).OfType<ClassicFrame>().Count() == 1, id + ": one Classic frame behind the window");
            var buttons = Descendants(window.Body).OfType<Button>().Where(b => b.IsVisibleInTree()).ToArray();
            Require(buttons.All(b => b.GetThemeStylebox("normal") is StyleBoxTexture), id + ": every shown button wears the Classic button art");
            foreach (var label in Descendants(window.Body).OfType<Label>().Where(l => l.IsVisibleInTree() && l.Text.Length > 0))
                Require(label.GetThemeFont("font") == Plugin.Kit.Regular, id + ": Classic type on '" + label.Text + "'");
            foreach (var control in Descendants(window.Body).OfType<Control>().Where(c => c.IsVisibleInTree()))
                Require(window.GetGlobalRect().Grow(1).Encloses(control.GetGlobalRect()), id + ": " + control.Name + " stays inside the window " + control.GetGlobalRect());
            string file = prefix + "-" + id + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { id, file, rect = window.GetGlobalRect().ToString(), buttons = buttons.Select(b => new { name = b.Name.ToString(), text = b.Text, rect = b.GetGlobalRect().ToString() }).ToArray() });
            GD.Print("NEW_WINDOWS_AUDIT_CAPTURE " + prefix + "-" + id);
            Native.Call(feature, "Dispose");
            layer.QueueFree();
            await Frames(2);
        }
        world.Free();
        System.IO.File.WriteAllText(output + "/" + prefix + "-new-windows.json", JsonSerializer.Serialize(new { checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("NEW_WINDOWS_AUDIT_OK " + output);
    }

    /// <summary>
    /// "manes-hud-audit": the Manes' Survival sign-up board and boss bar of a prepared World, opened through the
    /// client's own entry points and skinned by the Classic event overlays (board frame, score plaque, red meter).
    /// </summary>
    private async Task CaptureManesHudAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/manes-hud-audit");
        System.IO.Directory.CreateDirectory(output);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 600);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 600), MouseFilter = MouseFilterEnum.Ignore });
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("MANES_HUD_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int n = 8) { for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        var world = PreviewFixtures.Prepared(new WorldRoot());
        var manes = Native.Get<object>(world, "ManesSurvivalWindow")!;
        Native.Call(manes, "OpenBoard", 540, 37);
        Native.Call(manes, "StartBar", 900);
        Native.Call(manes, "OnBoss", new LibreKO.Network.ManesBoss("Scorpion King", 1_200_000, 744_000, 600));
        var layer = Native.Get<CanvasLayer>(manes, "layer")!;
        layer.GetParent().RemoveChild(layer); AddChild(layer);
        KnightOnlineUiClassic.Native.NativeOverlays.PrepareLater(world);
        await Frames();
        var board = Native.Get<PanelContainer>(manes, "SignUpBoard")!;
        var bar = Native.Get<PanelContainer>(manes, "BossBar")!;
        Require(board.Name == "manes_board", "the sign-up board is prepared as an event board (" + board.Name + ")");
        Require(Descendants(board).OfType<ClassicFrame>().Any(), "the sign-up board wears the Classic event board frame (" + string.Join(",", board.GetChildren().Select(c => c.Name + ":" + c.GetType().Name)) + ")");
        Require(bar.Name == "manes_boss_bar", "the boss bar is prepared as a score plaque");
        var meter = Descendants(bar).OfType<ProgressBar>().Single();
        Require(meter.GetThemeStylebox("fill") is StyleBoxTexture, "the boss health meter uses the original progress art");
        foreach (var label in Descendants(bar).OfType<Label>().Where(l => l.IsVisibleInTree() && l.Text.Length > 0 && l.GetParent() != meter.GetParent()))
            Require(label.GetThemeFontSize("font_size") == 13, "plaque type on '" + label.Text + "'");
        var hpText = meter.GetParent().GetChildren().OfType<Label>().Single();
        Require(hpText.GetThemeColor("font_color") == Colors.White && hpText.GetThemeConstant("outline_size") > 0, "the boss health reads white with an outline over the red meter");
        foreach (var control in new Control[] { board, bar })
            Require(GetViewportRect().Encloses(control.GetGlobalRect()), control.Name + " fits the screen " + control.GetGlobalRect());
        string file = prefix + "-manes-hud.png";
        GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
        System.IO.File.WriteAllText(output + "/" + prefix + "-manes-hud.json", JsonSerializer.Serialize(new { checks, file,
            board = board.GetGlobalRect().ToString(), bar = bar.GetGlobalRect().ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("MANES_HUD_AUDIT_OK " + output);
    }
}