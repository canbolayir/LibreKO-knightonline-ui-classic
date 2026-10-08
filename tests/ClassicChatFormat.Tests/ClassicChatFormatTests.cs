using Xunit;

namespace KnightOnlineUiClassic.Tests;

public sealed class ClassicChatFormatTests
{
    [Theory]
    [InlineData(3, "party", "5fd95f")]
    [InlineData(5, "shout", "ff9a3c")]
    [InlineData(6, "clan", "46d3c0")]
    [InlineData(15, "alliance", "6fb7ff")]
    public void RetainsOriginalChannelColors(byte type, string tag, string color)
    {
        Assert.Equal((tag, color), ClassicChatFormat.Channel(type));
    }

    [Theory]
    [InlineData(1, false, "e06666")]
    [InlineData(2, false, "6fa8ff")]
    [InlineData(1, true, "ffd24a")]
    public void RetainsNationAndGmNameColors(int nation, bool gm, string color)
    {
        Assert.Contains($"[color=#{color}]Canbo[/color]", ClassicChatFormat.Line(1, "Canbo", nation, gm, "Hello"));
    }

    [Fact]
    public void TreatsPlayerMarkupAsText()
    {
        string line = ClassicChatFormat.Line(1, "Canbo", 1, false, "[b]Hello[/b]");
        Assert.DoesNotContain("[b]", line);
        Assert.Contains("Hello", line);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(5, 1)]
    [InlineData(3, 2)]
    [InlineData(6, 3)]
    [InlineData(15, 4)]
    [InlineData(2, -1)]
    public void MapsCustomColourSlots(byte type, int slot)
    {
        Assert.Equal(slot, ClassicChatFormat.ColourSlot(type));
    }

    [Fact]
    public void LinksPlayerNamesOnlyWhenRequested()
    {
        Assert.Contains("[url=p:Canbo]", ClassicChatFormat.Line(1, "Canbo", 1, false, "Hi", links: true));
        Assert.DoesNotContain("[url=", ClassicChatFormat.Line(1, "Canbo", 1, false, "Hi"));
        Assert.DoesNotContain("[url=", ClassicChatFormat.Line(1, "[GM]Canbo", 1, false, "Hi", links: true));
    }

    [Fact]
    public void TagsNamelessClanLinesOnly()
    {
        Assert.StartsWith("[color=#46d3c0][lb]clan[rb] [/color]", ClassicChatFormat.Line(6, "", 0, false, "Notice"));
        Assert.Equal("[color=#ff9a3c]Notice[/color]", ClassicChatFormat.Line(5, "", 0, false, "Notice"));
    }
}
