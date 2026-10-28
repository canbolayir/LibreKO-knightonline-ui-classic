using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

/// <summary>Sample records the fixtures share: letters, a clan, item slots and the Special Auction lots.</summary>
public sealed partial class WorldPreview
{
    public const int PreviewHpPotion = 389015000;
    public const int PreviewMpPotion = 389020000;
    public const int PreviewUpgradedWeapon = 156210008;
    public const int AuctionPreviewDay = 2;
    public const int AuctionPreviewSeconds = 2 * 3600 + 14 * 60 + 9;
    public const int AgilityNecklace = 1320510810;
    public const int StrengthNecklace = 1320610820;
    public const int ShadowPiece = 700009000;
    public const int ElfMetalEarrings = 1310610106;

    /// <summary>The items of the Power-Up Store letter, as the store catalog lists them.</summary>
    private static readonly int[] StoreLetterItems = { 700002000, 379258000, 810166000, 700011000, 399282000, 800880000, 800003000, 800078000, 800442000, 810594000 };

    public static List<MailEntry> MailEntries() =>
    [
        new MailEntry
        {
            Id = 3, Sender = "LibreKO", Subject = "Collection Race: Moradon Rookie Roundup", Read = false,
            Attachments = MailAttachmentState.Pending, SentAt = DateTime.UtcNow.AddMinutes(-12),
            Items =
            [
                new MailAttachment { Kind = MailAttachmentKind.Gold, ItemId = QuestData.CoinItemId, Count = 300_000 },
                new MailAttachment { Kind = MailAttachmentKind.Experience, ItemId = QuestData.ExpItemId, Count = 150_000 },
                new MailAttachment { Kind = MailAttachmentKind.Item, ItemId = 379154000, Count = 1 },
            ],
        },
        new MailEntry
        {
            Id = 2, Sender = "Rikka", Subject = "Apples for the raid", Read = false,
            Attachments = MailAttachmentState.Pending, SentAt = DateTime.UtcNow.AddHours(-5),
            Items = [new MailAttachment { Kind = MailAttachmentKind.Item, ItemId = 810418000, Count = 20 }],
        },
        new MailEntry
        {
            Id = 1, Sender = "Zeus", Subject = "Welcome to the clan", Read = true,
            Attachments = MailAttachmentState.None, SentAt = DateTime.UtcNow.AddDays(-2),
        },
    ];

    /// <summary>A Power-Up Store letter with partly claimed attachments.</summary>
    public static MailEntry PusPreviewMail()
    {
        var mail = new MailEntry
        {
            Id = 9, Sender = "LibreKO", Subject = "A gift from Zeus", Read = false,
            Attachments = MailAttachmentState.Pending, Kind = MailKind.Store, SentAt = DateTime.UtcNow.AddMinutes(-3),
        };
        int index = 0;
        foreach (int id in StoreLetterItems.Where(id => ItemData.Get(id) != null))
            mail.Items.Add(new MailAttachment { Kind = MailAttachmentKind.Item, ItemId = id, Count = 2, Claimed = index++ < 2 ? 2 : 0 });
        if (mail.Items.Count > 2) mail.Items[2].Claimed = 1;
        return mail;
    }

    public static MyClanInfo PreviewClan(bool asChief, byte flag = ClanTypes.Accredited5) => new()
    {
        InClan = true, ClanId = 1, Name = "Olympus", Flag = flag, Fame = asChief ? ClanRanks.Chief : ClanRanks.Trainee,
        Grade = 2, Ranking = 3, PointFund = 5_000_000, Notice = "Siege practice tonight. Bring repair scrolls.",
        Online = 3, MaxMembers = ClanTypes.MaxMembers,
    };

    public static ItemSlot PreviewItem(int id, int count, int durability) => new()
    {
        ItemId = id, Count = (short)count, Durability = (short)durability,
    };

    /// <summary>Takes a built client control out of its layer and drops its drag controller, so a fixture can place it.</summary>
    public static Control DetachPreviewControl(Control control)
    {
        foreach (Node child in control.GetChildren())
        {
            if (child is not HudLayout layout) continue;
            control.RemoveChild(layout);
            layout.Free();
        }
        control.GetParent()?.RemoveChild(control);
        return control;
    }
}
