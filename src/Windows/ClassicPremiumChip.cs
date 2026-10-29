using Godot;
using LibreKO;
using KnightOnlineUiClassic.Native;
using Spec = KnightOnlineUiClassic.Layout.ClassicQuestHudLayout.Premium;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The account, clan premium and clan bonus chips as Classic information plaques, like the event title chips beside
/// them. 1.298 showed premium only as a yellow line on the login screen (el_login_intro_us "Premium"); in the world
/// the account plaque keeps the approved caption "Gold Premium: 2d 4h" (the client's text-table name in the Classic
/// casing with the time left; since 3277df5 the client's own chip shows the name alone and moves the time into its
/// tooltip) or "No Premium", the clan plaque the client's text ("Clan Premium · 3d"), the bonus plaque the client's
/// "Clan" and O/X marks: the plaque ink while a premium runs, the muted plaque ink without one. The client's status
/// dot gives way to the ink. The benefits tooltip is the original tooltip (co_tooltip_us): translucent black behind
/// white Arial Bold, with the name, the time left and the client's benefit lines.
/// </summary>
public static class ClassicPremiumChip
{
    public const string Skinned = "classic_premium_chip";

    public static void Register()
    {
        NativeQuestHud.Extend(NativeQuestHudPart.PremiumChip, node => Apply((PanelContainer)node, ClassicPremiumChipKind.Account));
        NativeQuestHud.Extend(NativeQuestHudPart.ClanPremiumChip, node => Apply((PanelContainer)node, ClassicPremiumChipKind.Clan));
        NativeQuestHud.Extend(NativeQuestHudPart.ClanBonusChip, node => Apply((PanelContainer)node, ClassicPremiumChipKind.ClanBonus));
    }

    public static void Apply(PanelContainer chip, ClassicPremiumChipKind kind = ClassicPremiumChipKind.Clan)
    {
        if (chip.HasMeta(Skinned)) return;
        chip.SetMeta(Skinned, true);
        chip.AddThemeStyleboxOverride("panel", new StyleBoxEmpty { ContentMarginLeft = Spec.Inset, ContentMarginRight = Spec.Inset, ContentMarginTop = Spec.PadY, ContentMarginBottom = Spec.PadY });
        chip.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        chip.Draw += () => ClassicPlaqueArt.Draw(chip, new Rect2(Vector2.Zero, chip.Size));
        chip.CustomMinimumSize = new Vector2(chip.CustomMinimumSize.X, Spec.Height);
        chip.Theme = Tooltip();
        // The bonus chip keeps a word gap between "Clan" and its marks; the premium chips have one caption.
        foreach (var row in chip.GetChildren().OfType<HBoxContainer>()) row.AddThemeConstantOverride("separation", kind == ClassicPremiumChipKind.ClanBonus ? Spec.BonusGap : 0);
        // The status dot's meaning moves into the caption ink.
        foreach (var dot in Native.Native.Descendants(chip).OfType<Panel>())
            if (dot.GetParent() is CenterContainer wrap) wrap.Visible = false;
        var labels = Native.Native.Descendants(chip).OfType<Label>().ToArray();
        foreach (var label in labels)
        {
            ClassicEventPanel.Bold(label, null, Spec.FontSize);
            label.AddThemeConstantOverride("outline_size", 0);
            label.AddThemeColorOverride("font_shadow_color", ClassicWorldOverlays.PlaqueShadow);
            label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            label.VerticalAlignment = VerticalAlignment.Center;
        }
        chip.AddChild(new ClassicPremiumInk(chip, labels, kind) { Name = "classic_premium_ink" });
        chip.QueueRedraw();
    }

    /// <summary>The original tooltip look for the chip's benefits tooltip (the tooltip popup inherits the chip's theme).</summary>
    internal static Theme Tooltip()
    {
        var theme = new Theme();
        var ground = new StyleBoxFlat { BgColor = Spec.TooltipGround };
        ground.ContentMarginLeft = ground.ContentMarginRight = Spec.TooltipPadX;
        ground.ContentMarginTop = ground.ContentMarginBottom = Spec.TooltipPadY;
        theme.SetStylebox("panel", "TooltipPanel", ground);
        theme.SetFont("font", "TooltipLabel", Plugin.Kit.Bold);
        theme.SetFontSize("font_size", "TooltipLabel", Spec.TooltipFont);
        theme.SetColor("font_color", "TooltipLabel", Colors.White);
        theme.SetColor("font_shadow_color", "TooltipLabel", Colors.Black);
        theme.SetConstant("shadow_offset_x", "TooltipLabel", 1);
        theme.SetConstant("shadow_offset_y", "TooltipLabel", 1);
        theme.SetConstant("outline_size", "TooltipLabel", 0);
        return theme;
    }
}

/// <summary>Which chip a plaque skins; the account chip also gets its Classic caption and tooltip.</summary>
public enum ClassicPremiumChipKind { Account, Clan, ClanBonus }

/// <summary>
/// Keeps the chip caption in the plaque ink: the client recolours it on every status change (gold while a
/// premium runs, grey without one); that state becomes the plaque ink or the muted plaque ink. The account chip's
/// caption and tooltip follow <see cref="NativePremium"/> (the client's premium state read through the network
/// state): the client writes its own name-only caption on every status change and this rewrites it.
/// </summary>
public partial class ClassicPremiumInk : Node
{
    private readonly PanelContainer? _chip;
    private readonly Label[] _labels;
    private readonly ClassicPremiumChipKind _kind;
    public ClassicPremiumInk() { _labels = Array.Empty<Label>(); }
    public ClassicPremiumInk(PanelContainer chip, Label[] labels, ClassicPremiumChipKind kind)
    {
        _chip = chip; _labels = labels; _kind = kind;
        ProcessPriority = int.MaxValue - 1;
        Sync();
        // The chip's ink only shows with the chip (the Classic HUD hides the client's chips).
        if (chip != null) Layout.ShownOnly.Gate(this, chip, Sync);
    }
    public override void _Process(double delta) => Sync();

    /// <summary>Whether the client shows a running premium (its gold caption).</summary>
    public static bool Active(Color native) => native.IsEqualApprox(UiTheme.GoldBright) || native.IsEqualApprox(UiTheme.Gold);

    /// <summary>The account plaque caption: the Classic name with the time left, or "No Premium".</summary>
    public static string Caption(NativePremium.Account account) =>
        account.Active ? $"{account.Name}: {NativePremium.ShortDuration(account.Hours)}" : account.Name;

    private void Sync()
    {
        NativePremium.Account? account = _kind == ClassicPremiumChipKind.Account ? NativePremium.Current(Game.Connection) : null;
        if (account is { } state && _chip != null && GodotObject.IsInstanceValid(_chip))
        {
            string tooltip = state.Active ? NativePremium.Details(state) : "";
            if (_chip.TooltipText != tooltip) _chip.TooltipText = tooltip;
        }
        string? caption = account is { } named ? Caption(named) : null;
        foreach (var label in _labels)
        {
            if (!GodotObject.IsInstanceValid(label)) continue;
            if (caption != null && label.Text != caption) label.Text = caption;
            var shown = label.GetThemeColor(HotNames.FontColor);
            if (shown == ClassicWorldOverlays.PlaqueInk || shown == Layout.ClassicQuestHudLayout.PlaqueMuted) continue;
            // The bonus chip's "Clan" title is the client's dim text; on the plaque both labels keep the plaque ink.
            bool active = _kind == ClassicPremiumChipKind.ClanBonus || (account?.Active ?? Active(shown));
            label.SetMeta(HotNames.PremiumActive, active);
            label.AddThemeColorOverride(HotNames.FontColor, active ? ClassicWorldOverlays.PlaqueInk : Layout.ClassicQuestHudLayout.PlaqueMuted);
        }
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName FontColor = "font_color";
    internal static readonly StringName PremiumActive = "premium_active";
}
