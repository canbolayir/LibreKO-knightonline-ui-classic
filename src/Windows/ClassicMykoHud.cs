using Godot;
using KnightOnlineUiClassic.Layout;
using L = KnightOnlineUiClassic.Layout.ClassicMykoHudLayout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// Original HUD art read through <see cref="LocalAssets"/>: the nation's layouts, views on them and their texture
/// regions. Every piece checks <see cref="Layout"/> first and keeps the bundled Classic design when the art is absent.
/// </summary>
public static class ClassicMykoArt
{
    public static string NationTag => Plugin.Kit.Nation == 1 ? "ka" : "el";

    /// <summary>The layout for the player's nation, or null (missing, or a texture missing).</summary>
    public static LayoutNode? Layout(string name) => Plugin.Kit.Local.Layout(name.Replace("{nation}", NationTag));

    /// <summary>A view of the art; one-line texts taller than their UIF rect stay centred on it.</summary>
    public static LayoutView View(LayoutNode root)
    {
        var view = new LayoutView(Plugin.Kit, root, Plugin.Kit.Local.Texture);
        foreach (var (node, control) in view.Where(n => n.IsString).ToList())
            if (control is Label { AutowrapMode: TextServer.AutowrapMode.Off } label)
            {
                view.CentreOnRect(label, node);
                label.Position = label.Position.Round();
            }
        return view;
    }

    public static Texture2D? Texture(LayoutNode image) => image.Texture == null ? null : Plugin.Kit.Local.Texture(image.Texture);

    public static Rect2 Source(LayoutNode image) => new(image.SrcX, image.SrcY, image.SrcW, image.SrcH);

    /// <summary>
    /// The layout's own images that <paramref name="include"/> accepts, stretched to their rects and composed into one
    /// texture covering them from the layout's origin (for a frame drawn at another size); null when an image is missing.
    /// </summary>
    public static ImageTexture? Compose(LayoutNode root, Func<LayoutNode, bool> include)
    {
        var parts = root.Images.Where(include).ToList();
        if (parts.Count == 0) return null;
        int width = parts.Max(n => n.X + n.W), height = parts.Max(n => n.Y + n.H);
        using var frame = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        foreach (var node in parts)
        {
            if (node.Texture == null || Plugin.Kit.Local.Image(node.Texture) is not { } sheet) return null;
            using var part = sheet.GetRegion(new Rect2I(node.SrcX, node.SrcY, node.SrcW, node.SrcH));
            if (part.GetFormat() != Image.Format.Rgba8) part.Convert(Image.Format.Rgba8);
            if (node.FlipH) part.FlipX();
            if (node.FlipV) part.FlipY();
            part.Resize(node.W, node.H, Image.Interpolation.Nearest);
            frame.BlendRect(part, new Rect2I(0, 0, node.W, node.H), new Vector2I(node.X, node.Y));
        }
        return ImageTexture.CreateFromImage(frame);
    }

    /// <summary>The image of a button for a state, falling back to the normal art.</summary>
    public static LayoutNode? State(LayoutNode button, int state) =>
        button.Images.FirstOrDefault(i => i.Tag == state) ?? button.Images.FirstOrDefault(i => i.Tag == ButtonState.Normal);

    public static int StateOf(BaseButton button, bool hovered) =>
        button.Disabled ? ButtonState.Disabled : button.IsPressed() || button.ButtonPressed ? ButtonState.Pressed
        : hovered ? ButtonState.Hover : ButtonState.Normal;

    /// <summary>The scaled art of a button state as a texture style box (UIF art is stretched to the control rect).</summary>
    public static StyleBoxTexture? Box(LayoutNode button, int state)
    {
        if (State(button, state) is not { } image || Texture(image) is not { } texture) return null;
        return new StyleBoxTexture { Texture = texture, RegionRect = Source(image), DrawCenter = true };
    }
}

/// <summary>
/// Whether the 2.x status bar menu art (<c>re_*_hpmenu</c>) is installed. Its letter plates are no longer drawn: the row
/// uses the plate-less <see cref="ClassicHudIcons"/>; the art's presence still decides whether the clock is shown.
/// </summary>
public static class ClassicMykoPlates
{
    public static bool Available => ClassicMykoArt.Layout(L.HpMenu) != null;
}

/// <summary>
/// The hourglass after the icon row, where the 2.x menu art has its alarm clock (<c>btn_scheduler</c>). Like the original
/// it opens the event schedule: the client's Events window (Classic skin), whose request for the schedule goes to the
/// server as from E. It is shown when the 2.x menu art is installed (bundled, so by default).
/// </summary>
public partial class ClassicStatusClock : BaseButton
{
    public const string NodeName = "btn_scheduler", Title = "Event Schedule";
    public const string Icon = "time";
    private bool _hovered;

    public ClassicStatusClock() { }

    public ClassicStatusClock(Vector2 size, Action press)
    {
        Name = NodeName;
        Size = size; CustomMinimumSize = size;
        FocusMode = FocusModeEnum.None; MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TextureFilter = TextureFilterEnum.Nearest;
        TooltipText = Title;
        Pressed += press;
        MouseEntered += () => { _hovered = true; QueueRedraw(); };
        MouseExited += () => { _hovered = false; QueueRedraw(); };
        ButtonDown += QueueRedraw;
        ButtonUp += QueueRedraw;
    }

    /// <summary>The 2.x menu art's clock button; the hourglass is shown where it exists.</summary>
    public static LayoutNode? Art => ClassicMykoArt.Layout(L.HpMenu)?.Find("btn_scheduler");

    public int State => ClassicMykoArt.StateOf(this, _hovered);

    public override void _Draw() => ClassicHudIcons.DrawButton(this, Icon, ClassicHudIcons.StateOf(this, _hovered));
}
