using KnightOnlineUiClassic.Native;
using Xunit;

namespace KnightOnlineUiClassic.Tests;

public class AnvilPlacementRulesTests
{
    private const int Raptor = 156210008;
    private const int BlessedUpgradeScroll = 379021000;
    private const int HighScrollLast = 379035000;
    private const int HighUpgradeScroll = 379016000;
    private const int LowUpgradeScroll = 379221000;
    private const int AccessoryScroll = 379159000;
    private const int AccessoryTrina = 354000000;
    private const int RestorationScroll = 810322000;
    private const int TrinaPiece = 700002000;
    private const int KarivdisPiece = 379258000;
    private const int BlessingLogos = 890092000;
    private const int ReverseScroll = 379256000;
    private const int Accessory = 330620430;
    private const int HighClass = 3;
    private const int Weapon = 5;
    private const int Armour = 1;
    private const int Grade = 8;

    private static int[] Selection(params int[] materials) => new[] { Raptor }.Concat(materials).ToArray();

    [Fact]
    public void TheSnapshotHoldsEveryUpgradableOrigin() => Assert.Equal(95056, AnvilPlacementRules.OriginCount);

    [Fact]
    public void BlessedScrollAndTrinaUseTheServerSetting()
    {
        Assert.True(AnvilPlacementRules.Allows(Selection(BlessedUpgradeScroll, TrinaPiece), false));
        Assert.True(AnvilPlacementRules.Allows(Selection(BlessedUpgradeScroll, TrinaPiece), false, HighClass, Weapon, Grade, false));
    }

    [Fact]
    public void UnknownOriginAndMissingRecipeAreRejected()
    {
        Assert.False(AnvilPlacementRules.Allows(new[] { 123456789, BlessedUpgradeScroll }, false));
        Assert.False(AnvilPlacementRules.Allows(Selection(HighScrollLast), false));
    }

    [Fact]
    public void ScrollAloneCanBePlacedBeforeProtection() =>
        Assert.True(AnvilPlacementRules.Allows(Selection(BlessedUpgradeScroll), false, HighClass, Weapon, Grade, false));

    [Theory]
    [InlineData(Raptor)]
    [InlineData(AccessoryScroll)]
    [InlineData(AccessoryTrina)]
    [InlineData(RestorationScroll)]
    [InlineData(379021001)]
    [InlineData(379021999)]
    public void InvalidMaterialsAreRejected(int material) =>
        Assert.False(AnvilPlacementRules.Allows(Selection(material), false, HighClass, Weapon, Grade, false));

    [Fact]
    public void HighClassOriginRejectsLowClassScroll() =>
        Assert.False(AnvilPlacementRules.Allows(Selection(LowUpgradeScroll), false, HighClass, Weapon, Grade, false));

    [Fact]
    public void TwoScrollsAreRejected() =>
        Assert.False(AnvilPlacementRules.Allows(Selection(BlessedUpgradeScroll, HighUpgradeScroll), false, HighClass, Weapon, Grade, false));

    [Fact]
    public void TwoProtectionsAreRejected() =>
        Assert.False(AnvilPlacementRules.Allows(Selection(BlessedUpgradeScroll, TrinaPiece, BlessingLogos), false, HighClass, Weapon, Grade, false));

    [Fact]
    public void RebirthProtectionCannotMixWithNormalScroll() =>
        Assert.False(AnvilPlacementRules.Allows(Selection(BlessedUpgradeScroll, KarivdisPiece), false, HighClass, Weapon, Grade, false));

    [Fact]
    public void LogosHonorsTypeAndGradeLimit()
    {
        Assert.True(AnvilPlacementRules.Allows(Selection(BlessedUpgradeScroll, BlessingLogos), false, HighClass, Weapon, 9, false));
        Assert.False(AnvilPlacementRules.Allows(Selection(BlessedUpgradeScroll, BlessingLogos), false, HighClass, Weapon, 10, false));
        Assert.False(AnvilPlacementRules.Allows(Selection(BlessedUpgradeScroll, BlessingLogos), false, HighClass, Armour, 9, false));
        Assert.False(AnvilPlacementRules.Allows(Selection(ReverseScroll, BlessingLogos), false, HighClass, Weapon, Grade, false));
    }

    [Fact]
    public void AccessoryMaterialsStayOnTheAccessoryBench()
    {
        Assert.True(AnvilPlacementRules.Allows(new[] { Accessory, Accessory, Accessory, AccessoryScroll, AccessoryTrina }, true, 7, Weapon, 0, true));
        Assert.False(AnvilPlacementRules.Allows(new[] { Accessory, Accessory, Accessory, BlessedUpgradeScroll, TrinaPiece }, true, 7, Weapon, 0, true));
    }

    [Fact]
    public void MaterialRolesFollowTheBench()
    {
        Assert.True(AnvilPlacementRules.IsMaterial(BlessedUpgradeScroll, false));
        Assert.False(AnvilPlacementRules.IsMaterial(BlessedUpgradeScroll, true));
        Assert.True(AnvilPlacementRules.IsMaterial(AccessoryScroll, true));
        Assert.False(AnvilPlacementRules.IsMaterial(AccessoryScroll, false));
        Assert.False(AnvilPlacementRules.IsMaterial(Raptor, false));
    }
}
