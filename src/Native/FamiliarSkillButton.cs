using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>A familiar skill on the familiar window's Skills page, with the shared cooldown shading.</summary>
public partial class FamiliarSkillButton : Button
{
    private readonly ColorRect _cooldown;
    private readonly ShaderMaterial _cooldownMaterial;
    private float _shown = -1;

    public int SkillId { get; internal set; }
    public float CooldownFraction { get; private set; }

    public FamiliarSkillButton()
    {
        ClipContents = true;
        _cooldownMaterial = Shaders.Material("cooldown");
        _cooldown = new ColorRect { Name = "pet_skill_cooldown", Material = _cooldownMaterial,
            MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_cooldown); _cooldown.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _cooldown.OffsetLeft = _cooldown.OffsetTop = 2; _cooldown.OffsetRight = _cooldown.OffsetBottom = -2;
    }

    /// <summary>Gives the button the client's action button style, as the native familiar controls use.</summary>
    internal void UseActionStyle()
    {
        var template = UiTheme.ActionButton("", "");
        AddThemeFontSizeOverride("font_size", template.GetThemeFontSize("font_size"));
        foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color" })
            AddThemeColorOverride(color, template.GetThemeColor(color));
        foreach (string style in new[] { "normal", "hover", "pressed", "focus" })
            AddThemeStyleboxOverride(style, template.GetThemeStylebox(style));
        CustomMinimumSize = template.CustomMinimumSize;
        template.Free();
        Audio.HookButton(this);
    }

    internal void SetCooldown(float fraction)
    {
        CooldownFraction = Mathf.Clamp(fraction, 0, 1);
        _cooldown.Visible = CooldownFraction > .001f;
        if (!_cooldown.Visible || Mathf.Abs(_shown - CooldownFraction) <= .002f) return;
        _cooldownMaterial.SetShaderParameter("remain", CooldownFraction); _shown = CooldownFraction;
    }
}
