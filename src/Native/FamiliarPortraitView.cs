using Godot;

namespace KnightOnlineUiClassic.Native;

/// <summary>Exposes the summoned familiar's appearance to the portrait renderer without keeping a live model.</summary>
public partial class FamiliarPortraitView : Control
{
    public GameNpcPortrait? Appearance { get; private set; }
    public event Action? AppearanceChanged;

    public void SetAppearance(GameNpcPortrait? appearance)
    {
        if (Appearance?.AppearanceKey == appearance?.AppearanceKey) return;
        Appearance = appearance;
        AppearanceChanged?.Invoke();
    }
}
