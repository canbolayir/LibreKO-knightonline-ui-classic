using Godot;
using LibreKO;
using LibreKO.Network;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureCouncilRateAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool condition, string text) { if (!condition) throw new Exception("COUNCIL_RATE_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Capture(HudWindow window, string state)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            Require(window.Size == new Vector2(271, 105), state + " exact original window footprint");
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete dialog fits viewport");
            foreach (var control in Descendants(window).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("council_expected_rect")))
            {
                Require(control.GetRect() == control.GetMeta("council_expected_rect").AsRect2(), state + " native measured bounds " + control.Name + " = " + control.GetRect());
                Require(new Rect2(Vector2.Zero, window.Size).Encloses(control.GetRect()), state + " inside original border " + control.Name);
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file });
        }
        foreach (string mode in new[] { "nation", "siege", "fee" })
        {
            var world = new World(); var window = (HudWindow)DetailCall(world, "BuildCouncilRateUiPreview", mode)!;
            AddChild(window); var panel = ClassicCouncilRate.Apply(window.Body)!; await Frames();
            var controls = Descendants(panel).OfType<Control>().ToArray();
            var down = controls.OfType<Button>().Single(c => c.Name == "taxrate_down");
            var up = controls.OfType<Button>().Single(c => c.Name == "taxrate_up");
            var value = controls.OfType<Label>().Single(c => c.Name == "taxrate_value");
            await Capture(window, mode + "-ready");
            if (mode == "nation")
            {
                Vector2 hit = up.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = hit, GlobalPosition = hit }); await Frames(2);
                Require(up.GetDrawMode().ToString() == "Hover", "Original upper arrow hit rectangle receives actual mouse hover");
                await Capture(window, "nation-hover");
                GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.Left, Pressed = true });
                await Capture(window, "nation-pressed");
                Require(value.Text == "3 %", "Arrow press preserves the value until mouse release");
                GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames(2);
                Require(value.Text == "4 %", "Original arrow mouse release invokes one native increment");
                GetViewport().PushInput(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero }); await Frames(2);
            }
            window.Position = new Vector2(100, 80); Vector2 origin = window.Position, pointer = origin + new Vector2(12, 4);
            controls.Single(c => c.Name == "party_drag").EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = pointer });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = pointer + new Vector2(30, 15) });
            window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, GlobalPosition = pointer + new Vector2(30, 15) });
            Require(window.Position == origin + new Vector2(30, 15), mode + " top edge uses native dragging");
            if (mode != "fee")
            {
                for (int i = 0; i < 12; i++) down.EmitSignal(BaseButton.SignalName.Pressed);
                Require(value.Text == "0 %", mode + " native lower tax bound retained");
                for (int i = 0; i < 12; i++) up.EmitSignal(BaseButton.SignalName.Pressed);
                Require(value.Text == "5 %", mode + " native upper tax bound retained");
            }
            else
            {
                string original = value.Text;
                down.EmitSignal(BaseButton.SignalName.Pressed); up.EmitSignal(BaseButton.SignalName.Pressed);
                Require(value.Text == original, "Dungeon fee follows the current server-owned fixed fee bounds");
            }
            await Capture(window, mode + "-limit");
            if (mode == "nation")
            {
                panel._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Enter }); await Frames();
                Require((bool)DetailField(world, "_nationTaxRateBusy")!, "Nation rate Enter starts the actual pending request");
                Require(up.Disabled && down.Disabled && controls.OfType<Button>().Single(c => c.Name == "taxrate_accept").Disabled,
                    "Pending tariff freezes the submitted value and duplicate confirmations");
                Require(up.SelfModulate.R == .5f && down.SelfModulate.R == .5f, "Pending arrow artwork is visibly disabled");
                string submitted = value.Text; DetailCall(world, "StepNationTaxRate", -1);
                Require(value.Text == submitted, "Native tariff handler also rejects edits while pending");
                await Capture(window, "nation-pending");
            }
            else
            {
                panel._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Enter });
                Require(!window.Visible && !(bool)DetailField(world, "_siegeTaxRateShown")!, mode + " Enter invokes native submission and closes the rate dialog");
                var kindType = typeof(LibreKO.Domain.SiegeRateKind);
                DetailCall(world, "OpenSiegeTaxRate", Enum.Parse(kindType, mode == "fee" ? "DungeonFee" : "Moradon"));
            }
            panel._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            Require(!window.Visible, mode + " Escape invokes native cancellation");
            window.Free(); world.Free(); await Frames();
        }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-council.json",
            JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        offline.Free(); GD.Print("COUNCIL_RATE_AUDIT_OK " + checks.Count);
    }
}
