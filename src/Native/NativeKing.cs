using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterKing(PluginContext context)
    {
        foreach (var id in NativeKing.WindowIds) NativeWindows.Prepare(id, NativeKing.Prepare);
    }
}

/// <summary>
/// King election and nation treasury windows: names the native controls the Classic King skin places,
/// mirrors the pending request flags into <c>king_pending</c> and the candidate clans into
/// <c>king_candidate_clan</c>. Callbacks, validation and server replies stay native.
/// </summary>
public static class NativeKing
{
    public static readonly string[] WindowIds = { "kingelection", "kingnominate", "kingplan", "kingvote", "kingballot", "nationtax", "nationintro" };
    public const int ElectionRows = 5;
    public const int CandidateRows = 10;

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_king")) return;
        bool named = NativeWindows.Name(Close(window), "king_close") && window.Id switch
        {
            "kingelection" => Election(window, world),
            "kingnominate" => Nominate(window, world),
            "kingplan" => Plan(window, world),
            "kingvote" => Vote(window, world),
            "kingballot" => Ballot(window, world),
            "nationtax" => Treasury(window, world),
            "nationintro" => Intro(window, world),
            _ => false,
        };
        if (named) window.SetMeta("classic_king_controls", 1);
    }

    /// <summary>The header X; its press runs the window's native close handler.</summary>
    public static Button? Close(HudWindow window) => window.Header?.GetChildren().OfType<HBoxContainer>().FirstOrDefault()?
        .GetChildren().OfType<Button>().FirstOrDefault(b => b.TooltipText == "Close" || b.Name == "king_close");

    private static Button[] Footer(HudWindow window) =>
        window.Body.GetChildren().OfType<HBoxContainer>().LastOrDefault()?.GetChildren().OfType<Button>().ToArray() ?? Array.Empty<Button>();

    private static bool NameAll(Button[] buttons, params string[] names)
    {
        if (buttons.Length != names.Length) return false;
        for (int i = 0; i < names.Length; i++) buttons[i].Name = names[i];
        return true;
    }

    private static void Pending(HudWindow window, WorldRoot world, params string[] flags)
    {
        window.SetMeta("king_pending", false);
        NativeWindows.Sync(window, () => window.SetMeta("king_pending", flags.Any(flag => Native.Get<bool>(world, flag))));
    }

    private static bool Election(HudWindow window, WorldRoot world)
    {
        var options = Native.Get<Button[]>(world, "KingElection.options");
        if (options == null || options.Length != ElectionRows) return false;
        for (int i = 0; i < options.Length; i++) options[i].Name = "king_option_" + i;
        bool named = NativeWindows.Name(Native.Get<Label>(world, "KingElection.speechUpper"), "king_speech_upper")
            & NativeWindows.Name(Native.Get<Label>(world, "KingElection.speechLower"), "king_speech_lower")
            & NativeWindows.Name(Native.Get<ScrollContainer>(world, "KingElection.senatorsBox"), "king_senators")
            & NativeWindows.Name(Native.Get<Label>(world, "KingElection.senatorsLabel"), "king_senators_text");
        Pending(window, world, "KingElection.busy", "KingElection.boxOpen");
        return named;
    }

    private static bool Nominate(HudWindow window, WorldRoot world)
    {
        var text = Native.Get<Label>(world, "KingElection.nominateText");
        var note = text?.GetParent()?.GetChildren().OfType<Label>().FirstOrDefault(l => l != text);
        var edit = Native.Get<LineEdit>(world, "KingElection.NomineeEdit");
        var caption = edit?.GetParent()?.GetChildren().OfType<Label>().FirstOrDefault();
        return NativeWindows.Name(text, "king_nominate_text") & NativeWindows.Name(note, "king_nominate_note")
            & NativeWindows.Name(caption, "king_nominate_caption") & NativeWindows.Name(edit, "king_nominee")
            & NameAll(Footer(window), "king_nominate_accept", "king_nominate_cancel");
    }

    private static bool Plan(HudWindow window, WorldRoot world)
    {
        bool named = NativeWindows.Name(Native.Get<TextEdit>(world, "KingElection.PlanEdit"), "king_plan_text")
            & NativeWindows.Name(Native.Get<Label>(world, "KingElection.planCount"), "king_plan_count")
            & NameAll(Footer(window), "king_plan_accept", "king_plan_cancel");
        Pending(window, world, "KingElection.planBusy");
        return named;
    }

    private static bool Vote(HudWindow window, WorldRoot world)
    {
        var rows = Native.Get<Array>(world, "KingElection.voteRows");
        if (rows == null || rows.Length != CandidateRows) return false;
        bool named = true;
        for (int i = 0; i < rows.Length; i++)
        {
            object row = rows.GetValue(i)!;
            named &= NativeWindows.Name(Native.Get<PanelContainer>(row, "Panel"), "king_vote_row_" + i)
                & NativeWindows.Name(Native.Get<Button>(row, "Vote"), "king_vote_" + i)
                & NativeWindows.Name(Native.Get<Label>(row, "Number"), "king_vote_number_" + i)
                & NativeWindows.Name(Native.Get<Label>(row, "Name"), "king_vote_name_" + i)
                & NativeWindows.Name(Native.Get<Button>(row, "Pledge"), "king_vote_pledge_" + i);
        }
        // The native list shows number and name only; the original Organization column reads the clan
        // the server already sends with each candidate.
        NativeWindows.Sync(window, () =>
        {
            for (int i = 0; i < rows.Length; i++)
            {
                object row = rows.GetValue(i)!;
                var panel = Native.Get<PanelContainer>(row, "Panel");
                if (panel == null) continue;
                panel.SetMeta("king_candidate_clan", Native.Get<object>(row, "Candidate") is KingCandidate candidate ? candidate.Clan : "");
            }
        });
        Pending(window, world, "KingElection.voteBusy");
        return named;
    }

    private static bool Ballot(HudWindow window, WorldRoot world) =>
        NativeWindows.Name(Native.Get<Label>(world, "KingElection.ballotText"), "king_ballot_text")
        & NameAll(Footer(window), "king_ballot_favour", "king_ballot_against", "king_ballot_cancel");

    private static bool Treasury(HudWindow window, WorldRoot world)
    {
        var options = Native.Get<VBoxContainer>(world, "KingTreasury.nationTaxOptions");
        var buttons = options?.GetChildren().OfType<Button>().ToArray() ?? Array.Empty<Button>();
        bool named = NativeWindows.Name(Native.Get<Label>(world, "KingTreasury.nationTaxNotice"), "nation_tax_heading")
            & NativeWindows.Name(Native.Get<Label>(world, "KingTreasury.nationTaxNotice2"), "nation_tax_notice")
            & NativeWindows.Name(options, "nation_tax_options")
            & NameAll(buttons, "nation_tax_treasury", "nation_tax_rate", "nation_tax_fund", "nation_tax_item", "nation_tax_intro");
        Pending(window, world, "KingTreasury.nationTaxBusy", "KingTreasury.boxOpen");
        return named;
    }

    private static bool Intro(HudWindow window, WorldRoot world) =>
        NativeWindows.Name(Native.Get<TextEdit>(world, "KingTreasury.nationIntroEdit"), "nation_intro_text")
        & NameAll(Footer(window), "nation_intro_revision", "nation_intro_accept");
}
