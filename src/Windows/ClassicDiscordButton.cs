using Godot;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// Dresses the client's Discord button (the bottom-right corner icon) like the status bar's Classic icon row: the
/// client's Discord mark drawn in the nation's icon gradient (<see cref="ClassicHudIcons"/>, cell "discord"), with
/// the row's hover, pressed and disabled states, and no disc or caption. The client keeps the button, its place and
/// its action; the disc, glyph and caption are only made transparent and come back when the plugin shuts down.
/// </summary>
public partial class ClassicDiscordGlyph : Control
{
    public const string NodeName = "classic_discord_glyph", Icon = "discord";
    private readonly BaseButton? _button;
    private bool _hovered;

    public ClassicDiscordGlyph() { }

    private ClassicDiscordGlyph(BaseButton button)
    {
        _button = button;
        Name = NodeName;
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        SetAnchorsPreset(LayoutPreset.FullRect);
        button.MouseEntered += Enter;
        button.MouseExited += Exit;
        button.ButtonDown += QueueRedraw;
        button.ButtonUp += QueueRedraw;
    }

    private void Enter() { _hovered = true; QueueRedraw(); }
    private void Exit() { _hovered = false; QueueRedraw(); }

    public override void _Draw()
    {
        if (_button == null) return;
        // The 28-pixel cell, centred in the client's button.
        var at = ((Size - new Vector2(ClassicHudIcons.Cell, ClassicHudIcons.Cell)) / 2).Floor();
        DrawSetTransform(at);
        ClassicHudIcons.DrawButton(this, Icon, ClassicHudIcons.StateOf(_button, _hovered));
    }

    /// <summary>Dresses <paramref name="button"/> once; the dress is undone when the plugin shuts down.</summary>
    public static void Dress(Button button)
    {
        if (button.GetNodeOrNull(NodeName) != null) return;
        var hidden = new List<(CanvasItem Item, Color Modulate)>();
        foreach (var child in button.GetChildren().OfType<CanvasItem>())
        {
            hidden.Add((child, child.Modulate));
            child.Modulate = Colors.Transparent;
        }
        var glyph = new ClassicDiscordGlyph(button);
        button.AddChild(glyph);
        NativeHooks.Hold("Discord button (Classic glyph)", () =>
        {
            foreach (var (item, modulate) in hidden) if (GodotObject.IsInstanceValid(item)) item.Modulate = modulate;
            if (GodotObject.IsInstanceValid(button))
            {
                button.MouseEntered -= glyph.Enter; button.MouseExited -= glyph.Exit;
                button.ButtonDown -= glyph.QueueRedraw; button.ButtonUp -= glyph.QueueRedraw;
            }
            if (GodotObject.IsInstanceValid(glyph)) glyph.QueueFree();
        }, button);
    }
}
