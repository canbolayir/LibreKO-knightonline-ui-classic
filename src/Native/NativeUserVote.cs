using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// User vote captions when the client's text table lacks the reason texts (43790-43793): the client then returns
/// "Reason 1".."Reason 4" for the reasons and an empty reason line for a received vote. The captions below are the
/// official English texts of those entries; the client's text is used whenever it has one.
/// </summary>
public static class NativeUserVote
{
    private static readonly string[] Captions =
    {
        "Inappropriate language",
        "Intentional disturbance",
        "Long-term inactivity",
        "Other (30 letters max)",
    };

    /// <summary>The reason captions: the client's texts, or the fallback captions for its "Reason n" placeholders.</summary>
    public static IReadOnlyList<string> Reasons(IGameUserVote vote)
    {
        var reasons = vote.Reasons.ToArray();
        for (int i = 0; i < reasons.Length && i < Captions.Length; i++)
            if (reasons[i].Length == 0 || reasons[i] == $"Reason {i + 1}") reasons[i] = Captions[i];
        return reasons;
    }

    /// <summary>
    /// The reason line of the received vote: the client's line, or the fallback caption of the listed reason it
    /// could not name (read from the client's shown question through its user vote bridge).
    /// </summary>
    public static string AskReason(IGameUserVote vote)
    {
        if (vote.AskReason.Length > 0) return vote.AskReason;
        // The plugin's facade forwards to the client's bridge, possibly through another facade proxy.
        object? bridge = vote;
        for (int depth = 0; depth < 4 && bridge != null && !Native.Has(bridge, "world"); depth++)
            bridge = Native.Has(bridge, "Source") ? Native.Get<object>(bridge, "Source") : null;
        if (bridge == null || Native.Get<LibreKO.Scene.WorldRoot>(bridge, "world") is not { } world
            || Native.Get<object>(world, "UserVoteWindow.askShown") is not UserVoteAsk ask) return "";
        return ask.Reason is >= 1 and < UserVote.ReasonOther ? Captions[ask.Reason - 1] : "";
    }
}
