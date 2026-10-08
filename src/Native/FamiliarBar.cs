using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Applies <see cref="NativeHudPart.FamiliarBar"/> extenders to the client's familiar skill bar. The bar is
/// found when its layout controller is attached, which is the last step of building it, so extenders see the
/// complete native bar before the controller installs its overlays and default position. Extenders may set:
/// <list type="bullet">
/// <item><c>native_drag_handle_only</c>: move the bar with its own grip only, without the floating move grip;</item>
/// <item><c>hud_default_position</c>: a Callable returning the default position used while no position is saved;</item>
/// <item><c>slot_frame</c> on a skill cell: the panel style kept on that cell across native refreshes;</item>
/// <item><c>tooltip_builder</c> on a skill cell: a Callable(string) returning the tooltip content for that cell.</item>
/// </list>
/// </summary>
public static class FamiliarBar
{
    private static bool _watching;

    internal static void Watch()
    {
        if (_watching || Engine.GetMainLoop() is not SceneTree tree) return;
        _watching = true;
        tree.NodeAdded += node =>
        {
            if (node is not HudLayout layout || layout.GetParent() is not PanelContainer bar || bar.HasMeta("pet_bar_controls") || !IsFamiliarBar(bar)) return;
            bar.SetMeta("pet_bar_controls", 1);
            var cells = NameControls(bar);
            // The tree is still entering; extenders reparent controls, so they run once it has settled.
            Callable.From(() => Prepare(bar, layout, cells)).CallDeferred();
        };
    }

    private static bool IsFamiliarBar(PanelContainer bar) =>
        bar.GetChildCount() > 0 && bar.GetChild(0) is HBoxContainer row
        && row.GetChildren().Any(c => c.GetType().Name == "PetSkillCell");

    /// <summary>Gives the native bar controls the names the familiar bar extenders look for.</summary>
    private static PanelContainer[] NameControls(PanelContainer bar)
    {
        var row = (HBoxContainer)bar.GetChild(0);
        var cells = row.GetChildren().OfType<PanelContainer>().Where(c => c.GetType().Name == "PetSkillCell").ToArray();
        bar.Name = "pet_skill_bar"; row.Name = "pet_bar_native_row";
        NativeWindows.Name(row.GetChildren().OfType<Control>().FirstOrDefault(c => c.GetType().Name == "HotGrip"), "pet_bar_grip");
        NativeWindows.Name(row.GetChildren().OfType<Button>().LastOrDefault(), "pet_bar_page");
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].Name = i == 0 ? "pet_bar_attack" : "pet_bar_skill_" + (i - 1);
            NativeWindows.Name(cells[i].GetChildren().OfType<TextureRect>().FirstOrDefault(), "pet_bar_icon");
            NativeWindows.Name(cells[i].GetChildren().OfType<ColorRect>().FirstOrDefault(), "pet_bar_cooldown");
        }
        return cells;
    }

    private static void Prepare(PanelContainer bar, HudLayout layout, PanelContainer[] cells)
    {
        if (!GodotObject.IsInstanceValid(bar) || !GodotObject.IsInstanceValid(layout)) return;
        foreach (var extend in NativeHud.Extenders(NativeHudPart.FamiliarBar))
        {
            try { extend(bar); }
            catch (Exception e) { GD.PushError($"[plugin:knightonline-ui-classic] familiar bar extension: {e}"); }
        }
        if (bar.HasMeta("native_drag_handle_only")) Native.Set(layout, "_moveGripOverlay", false);
        if (bar.HasMeta("hud_default_position"))
        {
            var native = Native.Get<Func<Vector2>>(layout, "_defaultPosition");
            Native.Set(layout, "_defaultPosition", new Func<Vector2>(() =>
                bar.HasMeta("hud_default_position") ? bar.GetMeta("hud_default_position").AsCallable().Call().AsVector2()
                    : native?.Invoke() ?? bar.Position));
        }
        foreach (var cell in cells)
            if (cell.HasMeta("tooltip_builder"))
                cell.ChildEnteredTree += child => { if (child is PopupPanel popup) BuildTooltip(cell, popup); };
        if (cells.Any(c => c.HasMeta("slot_frame"))) bar.AddChild(new FamiliarBarFrames(cells) { Name = "pet_bar_frames" });
    }

    /// <summary>Replaces the content of the native tooltip popup that Godot opens for a skill cell.</summary>
    private static void BuildTooltip(Control cell, PopupPanel popup)
    {
        if (!cell.HasMeta("tooltip_builder") || popup.GetChildren().OfType<Label>().FirstOrDefault() is not { } label) return;
        if (cell.GetMeta("tooltip_builder").AsCallable().Call(cell.TooltipText).AsGodotObject() is not Control content) return;
        // The default label stays for the viewport to address, without text or size of its own.
        label.Visible = false; label.Text = "";
        popup.AddChild(content);
        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }
}

/// <summary>Keeps each familiar skill cell's extender frame after the client restyles the cell.</summary>
public partial class FamiliarBarFrames : Node
{
    private readonly PanelContainer[] _cells;

    public FamiliarBarFrames() { _cells = Array.Empty<PanelContainer>(); }

    public FamiliarBarFrames(PanelContainer[] cells)
    {
        _cells = cells;
        ProcessPriority = int.MaxValue;
    }

    public override void _Ready() => Apply();

    public override void _Process(double delta) => Apply();

    private void Apply()
    {
        foreach (var cell in _cells)
        {
            if (!GodotObject.IsInstanceValid(cell) || !cell.HasMeta("slot_frame") || cell.GetMeta("slot_frame").AsGodotObject() is not StyleBox frame) continue;
            if (cell.GetThemeStylebox("panel") != frame) cell.AddThemeStyleboxOverride("panel", frame);
        }
    }
}
