using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Native;

/// <summary>
/// Vendor, trade and merchant entry points the fork kept in the client. The audits call them by their
/// old names; they now run the plugin's flow on top of the client.
/// </summary>
public static partial class PreviewFixtures
{
    public static void AskBuy(WorldRoot world, int itemId, int preferred) => NativeVendor.AskBuy(world, itemId, preferred);

    public static void AskSell(WorldRoot world, int abs) => NativeVendor.AskSell(world, abs);

    public static void OpenExchangeGold(WorldRoot world) => NativeTrade.OpenGold(world);

    public static int ExchangeOfferCount(WorldRoot world) => NativeTrade.OfferCount(world);

    public static void CloseExchangeFinal(WorldRoot world) => NativeTrade.CloseFinal(world);

    public static void OfferSlotAmount(WorldRoot world, int absSlot, int count)
    {
        var inventory = Game.Session.Self.Inventory;
        Native.Call(world, "Exchange.OfferSlotAmount", absSlot, inventory[absSlot].ItemId, count);
    }

    public static void ConfirmSellStall(WorldRoot world) => NativeMerchant.ConfirmSellStall(world);

    public static void SellToWanted(WorldRoot world, int gridIndex, int wantedSlot) => NativeMerchant.SellToWanted(world, gridIndex, wantedSlot);

    /// <summary>The client's buy-back facet and item tooltip on a layer the audit adds to its own tree.</summary>
    public static CanvasLayer BuildRepurchaseClassicUiPreview(WorldRoot world)
    {
        ItemData.EnsureLoaded(); Call(world, "ItemTooltip.Build"); Call(world, "Repurchase.Build");
        var layer = Field<CanvasLayer>(world, "Repurchase.Layer");
        Field<CanvasLayer>(world, "ItemTooltip.tipLayer").Reparent(layer, false);
        world.RemoveChild(layer);
        return layer;
    }

    /// <summary>A buy-back list reply (open or refresh) as the server sends it.</summary>
    public static RepurchaseReply RepurchaseList(RepurchaseSubOpcode op, IReadOnlyList<RepurchaseRow> rows)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_TRADE);
        p.WriteByte((byte)op); p.WriteByte((byte)RepurchaseResult.Succeeded); p.WriteByte((byte)rows.Count);
        foreach (var row in rows) { p.WriteByte(row.Row); p.WriteInt(row.ItemId); p.WriteInt(row.Price); p.WriteUInt(row.DeletesAt); }
        return RepurchaseReply.Read(p);
    }

    /// <summary>A buy-back purchase reply: success puts the item at a bag position, a refusal carries its error.</summary>
    public static RepurchaseReply RepurchaseBought(int itemId, RepurchaseError error = (RepurchaseError)0)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_TRADE);
        p.WriteByte((byte)RepurchaseSubOpcode.Buy); p.WriteByte(error == (RepurchaseError)0 ? (byte)RepurchaseResult.Succeeded : (byte)0); p.WriteByte(0);
        if (error == (RepurchaseError)0) { p.WriteByte(3); p.WriteInt(itemId); p.WriteByte(0); }
        else { p.WriteInt(itemId); p.WriteShort((short)error); }
        return RepurchaseReply.Read(p);
    }
}
