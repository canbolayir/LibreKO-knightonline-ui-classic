using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Windows;

/// <summary>Uses the shared bounded NPC cache; the familiar window keeps only a circular 2D image.</summary>
public partial class ClassicFamiliarPortrait : TextureRect
{
    private readonly FamiliarPortraitView _view;
    private int _revision;
    public ClassicFamiliarPortrait(FamiliarPortraitView view)
    {
        _view = view; Name = "familiar_portrait_texture";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        ExpandMode = ExpandModeEnum.IgnoreSize; StretchMode = StretchModeEnum.Scale;
        Material = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; void fragment() { if (length(UV - vec2(0.5)) > 0.5) discard; COLOR = texture(TEXTURE, UV); }" } };
        _view.AppearanceChanged += Refresh; TreeExiting += () => { _revision++; _view.AppearanceChanged -= Refresh; };
        Ready += Refresh;
    }
    private async void Refresh()
    {
        int revision = ++_revision; var appearance = _view.Appearance; Texture = null;
        if (appearance == null || !IsInsideTree()) return;
        var texture = await NpcPortraitCache.Get(this, appearance);
        if (!GodotObject.IsInstanceValid(this) || revision != _revision || !IsInsideTree()) return;
        Texture = texture;
    }
}
