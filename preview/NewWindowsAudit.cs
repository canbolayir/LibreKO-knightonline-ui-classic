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
}
