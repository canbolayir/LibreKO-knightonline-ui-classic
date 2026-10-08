using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;

public partial class Preview
{
    private async Task AuditPetBarPlacement(World world, CanvasLayer layer, Control bar,
        Action<bool, string> require, Func<string, Task> capture)
    {
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        var main = new HotkeyBar { Name = "pet_audit_main_hotbar" }; layer.AddChild(main); await Frames();
        var behavior = Descendants(bar).OfType<HudLayout>().Single();
        void ApplyDefault() => typeof(HudLayout).GetMethod("ApplySavedLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(behavior, null);
        async Task Rotate()
        {
            var button = Descendants(main).OfType<BaseButton>().Single(c => c.Name == "rotate");
            var at = button.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true); await Frames(2);
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, Pressed = pressed, ButtonIndex = MouseButton.Left }, true);
            await Frames();
        }
        if (main.Size.X > main.Size.Y) await Rotate();
        main.Position = new Vector2(8, 110); ApplyDefault(); await Frames();
        var room = GetViewportRect().Size;
        require(main.Size.X < main.Size.Y && GetViewportRect().Encloses(main.GetGlobalRect()), "Familiar placement uses the actual visible vertical main hotbar");
        require(bar.Position == new Vector2(Mathf.Round((room.X - ClassicPetBarLayout.Size.X) / 2), room.Y - Taskbar.BarHeight - ClassicPetBarLayout.Size.Y - 12)
            && !bar.GetGlobalRect().Intersects(main.GetGlobalRect()), "Familiar bar stays above the taskbar without overlapping the vertical main hotbar");
        await capture("vertical-main-bar");
        await Rotate();
        require(main.Size.X > main.Size.Y, "Actual main hotbar rotate button changes to horizontal layout");
        main.Position = new Vector2(88, room.Y - Taskbar.BarHeight - main.Size.Y - 2); ApplyDefault(); await Frames();
        require(bar.Position.X == main.GlobalPosition.X && bar.GetGlobalRect().End.Y + 6 == main.GlobalPosition.Y
            && !bar.GetGlobalRect().Intersects(main.GetGlobalRect()), "Familiar bar aligns above the actual horizontal main hotbar with a 6 px gap");
        await capture("horizontal-main-bar");
        var saved = bar.Position; DetailCall(world, "HidePetBar"); DetailCall(world, "OnPetBarSummoned", LibreKO.Network.Net.I.Pet!); await Frames();
        require(bar.Position == saved, "Resummoning preserves the familiar HUD position beside the main hotbar");
        main.Position = new Vector2(88, 8); ApplyDefault(); await Frames();
        require(bar.GetGlobalRect().Position.Y == main.GetGlobalRect().End.Y + 6 && !bar.GetGlobalRect().Intersects(main.GetGlobalRect()),
            "Familiar default goes below a horizontal main hotbar at the viewport top");
        await capture("top-main-bar");
        var extraPages = (List<int>)typeof(HotkeyBar).GetField("_extraPages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        extraPages.Clear(); for (int i = 1; i < 8; i++) extraPages.Add(i);
        typeof(HotkeyBar).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null); await Frames();
        main.Position = new Vector2(88, room.Y - Taskbar.BarHeight - main.Size.Y - 2); ApplyDefault(); await Frames();
        require(Descendants(main).OfType<Control>().Count(c => c.Name.ToString().StartsWith("extra_bar_")) == 7
            && bar.GetGlobalRect().End.Y + 6 == main.GetGlobalRect().Position.Y && GetViewportRect().Encloses(main.GetGlobalRect()),
            "Familiar default clears the complete eight-bar horizontal stack");
        await capture("eight-main-bars");
        main.Free();
        bar.Position = new Vector2(room.X - bar.Size.X, room.Y - bar.Size.Y);
        GetWindow().Size = new Vector2I(640, 600); await Frames(8);
        require(GetViewportRect().Encloses(bar.GetGlobalRect()) && bar.Position == new Vector2(640 - bar.Size.X, 600 - bar.Size.Y),
            "Native HudLayout clamps the familiar bar after viewport shrink without changing its dimensions");
        await capture("smaller-viewport");
        GetWindow().Size = new Vector2I(800, 680); await Frames(8);
    }
}
