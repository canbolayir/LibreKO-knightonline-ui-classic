using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Native;

/// <summary>
/// King election and nation treasury fixtures. The client's own <c>BuildKingUiPreview</c> builds the window
/// (it seeds an El Morad character); these wrappers restore the audited nation and run the King preparer as
/// the client's window flow would before the skin is applied.
/// </summary>
public static partial class PreviewFixtures
{
    public const int KingPreviewTribute = 12_500_000;
    public const int KingPreviewTreasury = 845_300_000;

    public static HudWindow BuildKingNationUiPreview(WorldRoot world, string view, int nation)
    {
        var window = (HudWindow)Call(world, "Preview.BuildKing", view)!;
        var me = Game.Session.Self.LastEnter; me.Nation = nation; Seed(world, me);
        if (view is "nation-tax" or "nation-tax-citizen")
            Call(world, "KingTreasury.OnTreasury", new KingTreasury(view == "nation-tax" ? NationTreasury.KingView : NationTreasury.CitizenView,
                view == "nation-tax" ? KingPreviewTribute : 0, KingPreviewTreasury));
        // The client's preview helper frees the window's drag behaviour; restore a non-persisting one so
        // the audit can exercise native dragging through the Classic grip.
        var bar = window.Header!.GetChildren().OfType<HBoxContainer>().First();
        Native.Set(window, "<Layout>k__BackingField", HudLayout.Attach(window, window.Id, bar, () => Vector2.Zero, persist: false));
        NativeKing.Prepare(window, world);
        return window;
    }

    /// <summary>A full ten-candidate list with long names and clans.</summary>
    public static void FillKingCandidates(WorldRoot world)
    {
        var names = new[] { "Rikka", "Zeus", "Morgana", "Hermes", "Valkyrie", "Sylph", "Ardream", "Krowaz", "Talon", "Longnamedcandidate" };
        var clans = new[] { "Valhalla", "Olympus", "Avalon", "", "Shieldmaidens", "Windwalkers", "Ardream Guard", "Krowaz Legion", "Talons", "Averylongclannamehere" };
        Call(world, "KingElection.OnCandidates", new KingCandidates(KingWire.Success,
            names.Select((name, i) => new KingCandidate(i + 1, name, clans[i])).ToList()));
    }
}
