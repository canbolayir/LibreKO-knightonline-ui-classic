using Godot;

namespace KnightOnlineUiClassic.Layout;

public static class IconDragPreview
{
    public static Vector2 GrabPoint(Control owner,TextureRect icon,Vector2 pressedPosition) =>
        icon.GetGlobalTransform().AffineInverse() * (owner.GetGlobalTransform() * pressedPosition);

    public static Control Create(TextureRect icon,Vector2 grabPoint)
    {
        // Godot places the preview root at the cursor. Offset the child so the
        // original grab point stays under it, preserving the icon's visual scale.
        var basis=icon.GetGlobalTransformWithCanvas();
        var root=new Control { MouseFilter=Control.MouseFilterEnum.Ignore };
        root.AddChild(new TextureRect {
            ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode=icon.StretchMode,
            Texture=icon.Texture,
            Size=icon.Size,
            Position=-basis.BasisXform(grabPoint),
            Rotation=basis.Rotation,
            Scale=basis.Scale,
            MouseFilter=Control.MouseFilterEnum.Ignore,
        });
        return root;
    }
}
