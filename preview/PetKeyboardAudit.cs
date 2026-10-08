using Godot;
using LibreKO;
using LibreKO.Network;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CapturePetKeyboardAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 740);
        GetViewport().GuiEmbedSubwindows = true;
        var original = Net.I; var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var world = new PetAuditWorld();
        var layer = (CanvasLayer)DetailCall(world, "BuildPetHatchClassicUiPreview", nation, false)!;
        world.AddChild(layer); AddChild(world);
        DetailCall(world, "EnablePetKeyboardUiPreview");
        T Field<T>(string name) => (T)typeof(World).GetField(name, flags)!.GetValue(world)!;
        var checks = new List<string>();
        void Require(bool ok, string text) { if (!ok) throw new Exception("PET_KEYBOARD_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task KeyPress(Key key, bool shift = false, bool echo = false, uint unicode = 0)
        {
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, ShiftPressed = shift, Echo = echo && pressed, Unicode = unicode }, true);
            await Frames();
        }
        bool ChatActive() => (bool)DetailCall(world, "PetKeyboardChatActive")!;
        var name = Field<LineEdit>("_petHatchName"); var panel = Field<HudWindow>("_petHatchPanel");
        bool NoticeOpen() => PreviewFixtures.PetHatchNotice(world) != null;
        var tabs = Field<ServiceTabs>("_petHatchTabs");
        var buttons = (List<Button>)typeof(ServiceTabs).GetField("_buttons", flags)!.GetValue(tabs)!;
        try
        {
            await Frames();
            Require(world.IsProcessingInput(), "Native ready-World _Input is active during keyboard checks");
            name.GrabFocus(); name.CaretColumn = name.Text.Length; await KeyPress(Key.X, unicode: 'x');
            Require(name.Text == "Kaulyx" && !ChatActive() && !NoticeOpen(), "Typing stays in the familiar name editor");
            await KeyPress(Key.Backspace); Require(name.Text == "Kauly", "Name deletion keeps native caret behavior");
            await KeyPress(Key.Enter);
            Require(NoticeOpen() && !ChatActive() && buttons.All(b => b.Disabled), "Focused Enter opens one confirmation and freezes reparented tabs");
            await KeyPress(Key.Enter, echo: true); Require(NoticeOpen(), "Repeated Enter cannot confirm a newly opened dialog");
            await KeyPress(Key.Escape);
            Require(!NoticeOpen() && panel.Visible && buttons.All(b => !b.Disabled) && name.Text == "Kauly", "Modal Escape preserves the trainer draft and restores tabs");
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus(); await KeyPress(Key.KpEnter);
            Require(NoticeOpen() && !ChatActive(), "Unfocused keypad Enter takes precedence over global chat opening");
            await KeyPress(Key.Enter);
            Require(!NoticeOpen() && !Field<bool>("_petHatchInFlight") && Field<Label>("_petHatchStatus").Text.StartsWith("Not connected"), "Confirmation Enter sends once and disconnected failure unlocks");
            typeof(World).GetField("_petHatchInFlight", flags)!.SetValue(world, true); DetailCall(world, "RefreshPetHatchUI");
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus(); await KeyPress(Key.Enter);
            Require(!NoticeOpen() && !ChatActive() && buttons.All(b => b.Disabled), "Pending input cannot reopen confirmation, chat or editable tabs");
            await KeyPress(Key.Escape); Require(!panel.Visible && Field<bool>("_petHatchInFlight"), "Pending Escape hides without cancelling the authoritative request");
            DetailCall(world, "OpenPetHatch", 99999);
            Require(buttons.All(b => b.Disabled) && Field<int>("_petHatchNpc") == 13016, "Pending reopen retains trainer identity and the tab lock");
            typeof(World).GetField("_petHatchInFlight", flags)!.SetValue(world, false); DetailCall(world, "RefreshPetHatchUI");
            name.GrabFocus(); await KeyPress(Key.Tab); await KeyPress(Key.Tab, shift: true);
            Require(!NoticeOpen() && !ChatActive(), "Tab and Shift-Tab never submit the operation or open chat");
            var other = new LineEdit { Position = new Vector2(10, 10), Size = new Vector2(120, 30) }; AddChild(other); other.GrabFocus();
            int submitted = 0; other.TextSubmitted += _ => submitted++;
            await KeyPress(Key.Enter);
            Require(submitted == 1 && !NoticeOpen(), "An unrelated text editor owns its Enter key"); other.Free();
            DetailCall(world, "SetPetKeyboardChat", true); await Frames();
            Require(ChatActive(), "Native chat can open alongside the trainer");
            await KeyPress(Key.Escape); Require(!ChatActive() && panel.Visible, "Chat Escape closes chat before the trainer");
            name.GrabFocus(); await KeyPress(Key.Escape);
            Require(!panel.Visible && !NoticeOpen(), "Focused name Escape closes the trainer through the native World input path");
        }
        finally
        {
            DetailCall(world, "SetPetKeyboardChat", false); world.Free(); net.Free(); typeof(Net).GetProperty("I")!.SetValue(null, original);
        }
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-keyboard.json", JsonSerializer.Serialize(new { checks }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("PET_KEYBOARD_AUDIT: " + checks.Count + " checks");
    }
}
