using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The account and clan premium as the client's chips show them (<c>World.Premium</c>, <c>World.ClanPremium</c>), and
/// the client's game clock (<c>_clockLabel</c>: the server's time of day and the weather), for HUD pieces that show
/// them elsewhere. The client keeps requesting and receiving every value.
/// </summary>
public static class NativePremium
{
    private const int PcRoomStatus = 2;

    public readonly record struct Account(bool Active, string Name, int Hours, IReadOnlyList<PremiumBonus> Bonuses);

    public readonly record struct Clan(bool InClan, bool Active, long EndsAt);

    public static Account Current(IGameConnection? net)
    {
        if (net == null || Game.Session.Premium.PremiumAccountStatus == 0 || Game.Session.Premium.PremiumHours <= 0) return new(false, "No Premium", 0, Array.Empty<PremiumBonus>());
        string name = Game.Session.Premium.PremiumAccountStatus == PcRoomStatus ? "PC Room" : PremiumName(Game.Session.Premium.PremiumType);
        return new(true, name, Game.Session.Premium.PremiumHours, Game.Session.Premium.PremiumBonuses);
    }

    /// <summary>
    /// The premium name from the client's text table (3277df5: <c>PremiumNames.Name</c> and the "Premium" word) in
    /// the Classic casing the approved plaques show: "Gold Premium", "DC Premium" (short abbreviations stay upper
    /// case, as the former client's names did), or "Premium" alone for a type the table lacks.
    /// </summary>
    private static string PremiumName(int type)
    {
        string premium = ClassicCase(ItemData.Text(PremiumNames.PremiumText, PremiumNames.PremiumFallback));
        return PremiumNames.Name(type) is { } name ? $"{ClassicCase(ItemData.Text(name.Text, name.Fallback))} {premium}" : premium;
    }

    /// <summary>"GOLD" → "Gold", "DC" and "EXP" unchanged (abbreviations of up to three letters), word by word.</summary>
    internal static string ClassicCase(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (word.Length <= 3 && word.All(char.IsUpper)) continue;
            words[i] = char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
        }
        return string.Join(" ", words);
    }

    /// <summary>The short time left the former chip caption carried: "2d 4h", "3d", "5h".</summary>
    public static string ShortDuration(int hours)
    {
        if (hours < 24) return $"{Math.Max(0, hours)}h";
        int days = hours / 24, rem = hours % 24;
        return rem > 0 ? $"{days}d {rem}h" : $"{days}d";
    }

    public static Clan ClanOf(WorldRoot? world, IGameConnection? net) =>
        new(world != null && Native.Get<bool>(world, "ClanPremium.inClan"), Game.Session.ClanPremium.ClanPremiumActive, Game.Session.ClanPremium.ClanPremiumEndsAt);

    /// <summary>
    /// The clan online bonus the client's chip shows (3277df5 <c>_clanBonusChip</c>): the online count the server
    /// sends (<see cref="Net.NoClanBonus"/> until it does), the O/X marks and the tooltip lines, all from the public
    /// <see cref="ClanBonusCard"/> rules and the text table.
    /// </summary>
    public readonly record struct ClanBonus(bool Shown, int Online, string Marks, string Tooltip);

    public static ClanBonus BonusOf(IGameConnection? net, int? online = null)
    {
        int count = online ?? Game.Session.Clan.ClanOnlineCount;
        bool shown = count != ClanSession.NoClanBonus && Game.Session.Clan.MyClan.InClan;
        return BonusLines(shown, count);
    }

    /// <summary>The marks and tooltip for a known online count (the group bar reads the count through its own source).</summary>
    public static ClanBonus BonusLines(bool shown, int online)
    {
        if (!shown) return new(false, online, "", "");
        string marks = ClanBonusCard.Marks(online, ItemData.Text(ClanBonusCard.OnText, ClanBonusCard.OnFallback), ItemData.Text(ClanBonusCard.OffText, ClanBonusCard.OffFallback));
        string tooltip = ClanBonusCard.Tooltip(online, ItemData.Text(ClanBonusCard.NeedText, ClanBonusCard.NeedFallback), ItemData.Text(ClanBonusCard.BonusText, ClanBonusCard.BonusFallback));
        // The client's bonus text indents its lines (" \n "); the Classic box shows them flush.
        return new(true, online, marks, string.Join("\n", tooltip.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)));
    }

    /// <summary>"2 days 4 hours", "5 hours", "45 minutes".</summary>
    public static string Duration(long seconds)
    {
        var left = TimeSpan.FromSeconds(Math.Max(0, seconds));
        static string Unit(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";
        if (left.TotalDays >= 1) return left.Hours > 0 ? Unit(left.Days, "day") + " " + Unit(left.Hours, "hour") : Unit(left.Days, "day");
        if (left.TotalHours >= 1) return Unit((int)left.TotalHours, "hour");
        return Unit(Math.Max(1, left.Minutes), "minute");
    }

    /// <summary>The name, the time left and the benefits the client's chip tooltip lists, one per line.</summary>
    public static string Details(Account account)
    {
        if (!account.Active) return "No Premium\nNo premium is active on this account.";
        var lines = new List<string> { account.Name, Duration(account.Hours * 3600L) + " left" };
        lines.AddRange(account.Bonuses.Select(b => b.Text).OfType<string>());
        return string.Join("\n", lines);
    }

    public static string ClanDetails(Clan clan)
    {
        if (!clan.Active) return "Clan: No Premium\nYour clan has no premium.";
        long left = clan.EndsAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (clan.EndsAt <= 0 || left <= 0) return "Clan Premium";
        return $"Clan Premium\n{Duration(left)} left\nEnds {DateTimeOffset.FromUnixTimeSeconds(clan.EndsAt).LocalDateTime:yyyy-MM-dd HH:mm}";
    }

    /// <summary>The client's clock text ("12:00  Clear"): the time of day the server sends and the weather.</summary>
    public static string? Clock(WorldRoot? world) =>
        world != null && Native.Get<RichTextLabel>(world, "ScenerySystem.ClockLabel") is { } label && GodotObject.IsInstanceValid(label) ? label.GetParsedText().Trim() : null;

    /// <summary>
    /// Hides the client's clan premium chip, clan bonus chip and clock while the group shows them. The account premium
    /// chip stays as an invisible spacer <paramref name="width"/> wide: the client lines its remaining top icons up
    /// left of it.
    /// </summary>
    public static void HideClientChips(WorldRoot world, float width)
    {
        foreach (var field in new[] { "ClanPremium.chip", "Premium.ClanBonusChip", "ScenerySystem.ClockLabel" })
            if (Native.Get<CanvasItem>(world, field) is { Visible: true } native) native.Visible = false;
        if (Native.Get<Control>(world, "Premium.Chip") is not { } chip || chip.Modulate.A == 0) return;
        chip.Modulate = Colors.Transparent;
        chip.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in Native.Descendants(chip).OfType<Control>()) child.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.CustomMinimumSize = new Vector2(width, chip.CustomMinimumSize.Y);
        Native.TryCall(world, "TopIcons.Place", out _);
    }
}
