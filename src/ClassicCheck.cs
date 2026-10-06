using Godot;
namespace KnightOnlineUiClassic;

// Explicit developer opt-in. Captures real windows without moving items or spending points.
public partial class ClassicCheck : Node
{
    public override void _Ready() { Plugin.Kit.Game.BecameAvailable+=Run; }
    public override void _ExitTree() { Plugin.Kit.Game.BecameAvailable-=Run; }
    private async void Run()
    {
        Plugin.Kit.Game.BecameAvailable-=Run;
        try
        {
            await ToSignal(GetTree().CreateTimer(12),SceneTreeTimer.SignalName.Timeout);
            await Capture("hud");
            if(OS.GetCmdlineUserArgs().Contains("hotbar-check"))
                foreach(var hotbar in Descendants(GetTree().Root).OfType<Windows.HotkeyBar>())
                    await hotbar.CaptureBarsForCheck(Capture);
            Plugin.Kit.Game.Windows.Open("inventory");
            await ToSignal(GetTree().CreateTimer(2),SceneTreeTimer.SignalName.Timeout);
            foreach(var inventory in Descendants(GetTree().Root).OfType<Windows.InventoryWindow>()) inventory.ShowExtrasForCheck();
            await Capture("inventory");
            Plugin.Kit.Game.Windows.Close("inventory");
            Plugin.Kit.Game.Windows.Open("skills");
            await ToSignal(GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);
            await Capture("skills");
            Plugin.Kit.Game.Windows.Close("skills");
            Plugin.Kit.Game.Windows.Open("genie");
            await ToSignal(GetTree().CreateTimer(2),SceneTreeTimer.SignalName.Timeout);
            await Capture("genie");
            Plugin.Kit.Game.Windows.Close("genie");
            GD.Print("[classic-check] PASS: HUD, inventory, skills, Genie rendered");
        }
        catch(Exception e) { GD.PushError("[classic-check] "+e); }
    }
    private async System.Threading.Tasks.Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(OS.GetExecutablePath().GetBaseDir(),"classic-"+name+".png"));
    }
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach(Node child in root.GetChildren()) { yield return child; foreach(var nested in Descendants(child)) yield return nested; }
    }
}
