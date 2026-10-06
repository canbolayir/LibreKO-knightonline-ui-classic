using Godot;
namespace KnightOnlineUiClassic.Layout;

/// <summary>Editable personal-trade composition referenced against the original nation artwork.</summary>
public static class ClassicExchangeLayout
{
    public static readonly Vector2 Size = new(365,627);
    public static readonly Rect2 Title = new(16,10,292,26), Drag = new(2,0,328,48);
    public static readonly Rect2 Mine = new(21,99,149,198), Theirs = new(206,99,149,198);
    public static readonly Rect2 MyName = new(22,52,146,38), TheirName = new(206,53,146,38);
    public static readonly Rect2 MyGold = new(56,307,108,20), TheirGold = new(240,307,108,20);
    public static readonly Rect2 Wallet = new(236,366,110,22), Status = new(14,366,178,22);
    public static readonly Rect2 MyCoin = new(22,304,22,24), TheirCoin = new(207,304,22,24), WalletCoin = new(204,365,22,24);
    public const int OfferPitch = 49, OfferSize = 50, InventorySize = 48;
    public static Rect2 BagCell(int index) => new(13+index%7*49,397+index/7*49,48,48);
    public static Rect2 OfferCell(int index) => new(index%3*49,index/3*49,50,50);
}
