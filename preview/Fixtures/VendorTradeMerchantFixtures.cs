using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic.Native;

/// <summary>
/// Vendor, trade and merchant entry points the fork kept in the client. The audits call them by their
/// old names; they now run the plugin's flow on top of the client.
/// </summary>
public static partial class PreviewFixtures
{
    public static void AskBuy(World world, int itemId, int preferred) => NativeVendor.AskBuy(world, itemId, preferred);

    public static void AskSell(World world, int abs) => NativeVendor.AskSell(world, abs);

    public static void OpenExchangeGold(World world) => NativeTrade.OpenGold(world);

    public static int ExchangeOfferCount(World world) => NativeTrade.OfferCount(world);

    public static void CloseExchangeFinal(World world) => NativeTrade.CloseFinal(world);

    /// <summary>The fork's two-argument offer; the client now also checks the item the amount was chosen for.</summary>
    public static void OfferSlotAmount(World world, int absSlot, int count)
    {
        var inventory = Native.Get<Inventory>(world, "Inv")!;
        if (!Native.TryCall(world, "OfferSlotAmount", out _, absSlot, inventory[absSlot].ItemId, count))
            Native.Call(world, "OfferSlotAmount", absSlot, count);
    }

    public static void ConfirmSellStall(World world) => NativeMerchant.ConfirmSellStall(world);

    public static void SellToWanted(World world, int gridIndex, int wantedSlot) => NativeMerchant.SellToWanted(world, gridIndex, wantedSlot);
}
