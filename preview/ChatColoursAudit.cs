using Godot;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureChatColoursAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700); GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new LibreKO.Network.Net(); typeof(LibreKO.Network.Net).GetProperty("I")!.SetValue(null, offline);
        var settingsProperty = typeof(Config).GetProperty("SettingsSavePath")!; string originalPath = Config.SettingsSavePath, originalColours = Config.ChatColors;
        string isolatedPath = output + "/" + (nation == 1 ? "karus" : "human") + "-isolated-settings.cfg";
        settingsProperty.SetValue(null, isolatedPath);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool value, string text) { if (!value) throw new Exception("CHAT_COLOURS_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Click(Control control)
        {
            var point = control.GetGlobalRect().GetCenter(); var viewport = control.GetViewport();
            if (viewport is Window popup && popup != GetWindow()) { point += (Vector2)popup.Position; viewport = GetViewport(); }
            viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true); await Frames(1);
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point }, true);
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point }, true); await Frames();
        }
        async Task KeyInput(Viewport viewport, Key key)
        {
            viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }); await Frames(1);
            viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }); await Frames();
        }
        World? world = null; CanvasLayer? layer = null;
        try
        {
            var stored = new ChatColors(); stored[ChatColorSlot.Normal] = new Color("123456"); Config.SetChatColors(stored.Format());
            world = new World(); layer = (CanvasLayer)DetailCall(world, "BuildChatColoursClassicUiPreview")!; AddChild(layer);
            var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicChatColoursSkin.Apply(window.Body)!;
            var palette = (PopupPanel)DetailField(world, "_chatPalette")!;
            var chat = typeof(World).GetProperty("Chat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            ChatColors Draft() => (ChatColors)DetailField(world, "_chatColorsDraft")!;
            ChatColors Applied() => (ChatColors)chat.GetType().GetField("_colors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chat)!;
            Button Pick(int slot) => Descendants(panel).OfType<Button>().Single(b => b.Name == "chat_colour_pick_" + slot);
            var apply = Descendants(panel).OfType<Button>().Single(b => b.Name == "chat_colour_apply");
            var reset = Descendants(panel).OfType<Button>().Single(b => b.Name == "chat_colour_default");
            var swatches = Descendants(panel).OfType<ColorRect>().Where(c => c.Name.ToString().StartsWith("chat_colour_swatch_")).ToArray();
            async Task Capture(string state)
            {
                await Frames();
                Require(window.Size == panel.CustomMinimumSize && GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete fixed frame fits viewport");
                foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("chat_colours_expected_rect")))
                {
                    Require(control.GetRect() == control.GetMeta("chat_colours_expected_rect").AsRect2(), state + " actual bounds " + control.Name + " " + control.GetRect());
                    Require(control.GetParent() is Control parent && new Rect2(Vector2.Zero, parent.Size).Encloses(control.GetRect()), state + " content inside parent " + control.Name);
                    if (control is Label label) Require(label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X <= label.Size.X, state + " full label fits " + label.Text);
                }
                if (palette.Visible)
                {
                    Require(window.GetGlobalRect().Encloses(new Rect2((Vector2)palette.Position, (Vector2)palette.Size)), state + " palette stays inside editor frame");
                    Require(palette.GetChild(0).GetChildren().OfType<Button>().Count() == ChatColors.Palette.Length, state + " complete native palette preserved");
                }
                string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
            }
            await Frames(); window.Position = new Vector2(320, 130); await Frames(2);
            var grip = Descendants(panel).OfType<Control>().Single(c => c.Name == "party_drag"); var start = window.Position;
            grip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = start + new Vector2(30, 15) });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = start + new Vector2(63, 36) }); window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Require(window.Position == start + new Vector2(33, 21), "Header forwards native HudLayout dragging");
            Require(swatches.Length == 11 && Draft().Format() == stored.Format(), "All eleven saved colours, including a custom hex colour, load without reset");
            await Capture("stored-colours");
            await Click(Pick(0)); Require(palette.Visible, "Native colour button opens embedded palette"); await Capture("normal-palette");
            await KeyInput(palette, Key.Escape); Require(!palette.Visible && window.Visible, "Palette Escape closes only palette: palette=" + palette.Visible + " editor=" + window.Visible);
            for (int slot = 0; slot < ChatColors.SlotCount; slot++)
            {
                await Click(Pick(slot)); var colour = ChatColors.Palette[(slot + 3) % ChatColors.Palette.Length];
                var choice = palette.GetChild(0).GetChildren().OfType<Button>().Single(b => b.GetMeta("chat_palette_colour").AsColor() == colour);
                if (slot == 10) await Capture("last-slot-palette");
                await Click(choice);
                Require(!palette.Visible && Draft()[(ChatColorSlot)slot] == colour, "Actual palette click changes draft slot " + slot + " visible=" + palette.Visible + " actual=" + Draft()[(ChatColorSlot)slot] + " expected=" + colour + " button=" + choice.GetGlobalRect());
                Require(Applied().Format() == stored.Format() && Config.ChatColors == stored.Format(), "Draft slot " + slot + " does not apply or persist prematurely");
                Require(swatches.Single(s => s.Name == "chat_colour_swatch_" + slot).Color == colour, "Native swatch previews draft slot " + slot);
            }
            await Capture("edited-draft");
            apply.GrabFocus(); await KeyInput(GetViewport(), Key.Enter);
            Require(Applied().Format() == Draft().Format() && Config.ChatColors == Draft().Format() && window.Visible, "Focused Apply Enter applies without closing editor");
            var saved = new ConfigFile(); Require(saved.Load(isolatedPath) == Error.Ok && saved.GetValue("hud", "chat_colors").AsString() == Draft().Format(), "Native persistence writes all eleven colours to isolated settings file");
            await Capture("applied-colours"); string applied = Applied().Format();
            await Click(reset); Require(Draft().Format() == new ChatColors().Format() && Applied().Format() == applied, "Default resets draft without changing applied colours");
            await Capture("default-draft");
            await KeyInput(GetViewport(), Key.Escape); Require(!window.Visible && Applied().Format() == applied && !palette.Visible, "Editor Escape discards un-applied default draft");
            DetailCall(world, "OpenChatColors"); await Frames(); Require(Draft().Format() == applied, "Reopen starts from applied settings rather than abandoned draft");
            await Capture("reopened-colours");
            await Click(Pick(4)); Require(palette.Visible, "Palette can reopen after closing editor");
            var beforeFocus = palette.GuiGetFocusOwner(); await KeyInput(palette, Key.Right);
            var keyboardPick = palette.GuiGetFocusOwner() as Button;
            Require(keyboardPick != null && keyboardPick != beforeFocus && keyboardPick.HasMeta("chat_palette_colour"), "Arrow key moves native palette focus");
            var keyboardColour = keyboardPick!.GetMeta("chat_palette_colour").AsColor(); await KeyInput(palette, Key.Enter);
            Require(!palette.Visible && Draft()[ChatColorSlot.SendWhisper] == keyboardColour && Applied().Format() == applied, "Palette Enter selects focused colour without applying draft");
            await Capture("keyboard-draft");
            await Click(Pick(4)); Require(palette.Visible, "Palette reopens after keyboard selection");
            DetailCall(world, "CloseChatColors"); await Frames(); Require(!window.Visible && !palette.Visible, "Native close hides both editor and palette");
        }
        finally
        {
            layer?.Free(); world?.Free(); typeof(Config).GetProperty("ChatColors")!.SetValue(null, originalColours); settingsProperty.SetValue(null, originalPath); offline.Free();
        }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-chat-colours.json", JsonSerializer.Serialize(new { nation, checks, screens, settingsIsolated = isolatedPath }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic chat colours audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
