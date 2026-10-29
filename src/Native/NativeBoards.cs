using Godot;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterBoards(PluginContext context)
    {
        foreach (string id in NativeBoards.WindowIds) NativeWindows.Prepare(id, NativeBoards.Prepare);
    }
}

/// <summary>
/// Event boards (bounty board, sheriff reports, fishing hall of fame, nation force): names the native forms,
/// lists, paging, gauge and actions the Classic board skin places on the party board artwork. Rows stay
/// native; the client rebuilds them on every server reply.
/// </summary>
public static class NativeBoards
{
    public static readonly string[] WindowIds = { "bounty", "report", "fishinghall", "forces" };
    public const string Prepared = "classic_board_controls";

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_board")) return;
        bool named = window.Id switch
        {
            "bounty" => PrepareBounty(window, world),
            "report" => PrepareReport(window, world),
            "fishinghall" => PrepareFishing(window, world),
            "forces" => PrepareForces(window, world),
            _ => false,
        };
        named &= NativeWindows.Name(Native.Get<Label>(window, "titleLbl"), "board_title")
            & NativeWindows.Name(Native.Get<Button>(window, "closeButton"), "board_close");
        if (named) window.SetMeta(Prepared, 1);
    }

    /// <summary>Names the native row list and its scroll container.</summary>
    private static bool PrepareRows(HudWindow window, WorldRoot world, string field)
    {
        var rows = Native.Get<VBoxContainer>(world, field);
        if (rows?.GetParent() is not ScrollContainer scroll || !window.Body.IsAncestorOf(rows)) return false;
        rows.Name = "board_rows"; scroll.Name = "board_scroll";
        return true;
    }

    /// <summary>Post form (target, reward, Post) above the open bounties.</summary>
    private static bool PrepareBounty(HudWindow window, WorldRoot world)
    {
        var target = Native.Get<LineEdit>(world, "Bounty.targetEdit");
        var reward = Native.Get<LineEdit>(world, "Bounty.rewardEdit");
        if (target?.GetParent() is not HBoxContainer form || reward?.GetParent() != form || !PrepareRows(window, world, "Bounty.listBox")) return false;
        var captions = form.GetChildren().OfType<Label>().ToArray();
        var post = form.GetChildren().OfType<Button>().ToArray();
        var titles = window.Body.GetChildren().OfType<Label>().ToArray();
        if (captions.Length != 2 || post.Length != 1 || titles.Length != 2) return false;
        target.Name = "bounty_target"; reward.Name = "bounty_reward"; post[0].Name = "bounty_post";
        captions[0].Name = "bounty_target_caption"; captions[1].Name = "bounty_reward_caption";
        titles[0].Name = "bounty_post_title"; titles[1].Name = "bounty_list_title";
        return true;
    }

    /// <summary>Sheriff gate, filing form, paged open reports with their votes, and the status line.</summary>
    private static bool PrepareReport(HudWindow window, WorldRoot world)
    {
        var target = Native.Get<LineEdit>(world, "Report.TargetEdit");
        var reason = Native.Get<LineEdit>(world, "Report.ReasonEdit");
        var prev = Native.Get<Button>(world, "Report.prevBtn");
        var refresh = prev?.GetParent()?.GetChildren().OfType<Button>().LastOrDefault();
        var listTitle = prev?.GetParent()?.GetChildren().OfType<Label>().FirstOrDefault();
        var fileTitle = window.Body.GetChildren().OfType<Label>().FirstOrDefault(l => l != Native.Get<Label>(world, "Report.gateLbl") && l != Native.Get<Label>(world, "Report.status"));
        bool named = NativeWindows.Name(Native.Get<Label>(world, "Report.gateLbl"), "report_gate")
            & NativeWindows.Name(Native.Get<Label>(world, "Report.status"), "report_status")
            & NativeWindows.Name(Native.Get<Label>(world, "Report.pageLbl"), "report_page")
            & NativeWindows.Name(target, "report_target") & NativeWindows.Name(reason, "report_reason")
            & NativeWindows.Name(target?.GetParent()?.GetChildren().OfType<Label>().FirstOrDefault(), "report_target_caption")
            & NativeWindows.Name(reason?.GetParent()?.GetChildren().OfType<Label>().FirstOrDefault(), "report_reason_caption")
            & NativeWindows.Name(Native.Get<Button>(world, "Report.fileBtn"), "report_file")
            & NativeWindows.Name(prev, "report_prev")
            & NativeWindows.Name(Native.Get<Button>(world, "Report.nextBtn"), "report_next")
            & NativeWindows.Name(refresh != prev ? refresh : null, "report_refresh")
            & NativeWindows.Name(listTitle, "report_list_title") & NativeWindows.Name(fileTitle, "report_file_title");
        var rows = Native.Get<VBoxContainer>(world, "Report.voteList");
        if (rows?.GetParent() is not ScrollContainer scroll || !window.Body.IsAncestorOf(rows)) return false;
        rows.Name = "board_rows"; scroll.Name = "board_scroll";
        NativeWindows.Name(window.Body.GetChildren().OfType<HSeparator>().FirstOrDefault(), "report_separator");
        return named && Native.Has(world, "Report.eligible");
    }

    private static bool PrepareFishing(HudWindow window, WorldRoot world)
    {
        var titles = window.Body.GetChildren().OfType<Label>().ToArray();
        if (titles.Length != 1 || !PrepareRows(window, world, "FishingHall.fishHallList")) return false;
        titles[0].Name = "fishing_title";
        return true;
    }

    /// <summary>Enlistment state, rank, force points, anger gauge and the Join/Leave actions.</summary>
    private static bool PrepareForces(HudWindow window, WorldRoot world)
    {
        var rank = Native.Get<Label>(world, "Forces.rankLbl");
        var anger = Native.Get<Control>(world, "Forces.angerBar");
        var labels = window.Body.GetChildren().OfType<Label>().ToArray();
        int gauge = anger?.GetIndex() ?? -1;
        var angerCaption = labels.LastOrDefault(l => l.GetIndex() < gauge);
        return NativeWindows.Name(labels.FirstOrDefault(), "forces_title")
            & NativeWindows.Name(Native.Get<Label>(world, "Forces.stateLbl"), "forces_state")
            & NativeWindows.Name(Native.Get<Label>(world, "Forces.pointsLbl"), "forces_points")
            & NativeWindows.Name(rank, "forces_rank")
            & NativeWindows.Name(rank?.GetParent()?.GetChildren().OfType<Label>().FirstOrDefault(l => l != rank), "forces_rank_caption")
            & NativeWindows.Name(angerCaption, "forces_anger_caption")
            & NativeWindows.Name(anger, "forces_anger")
            & NativeWindows.Name(Native.Get<Button>(world, "Forces.joinBtn"), "forces_join")
            & NativeWindows.Name(Native.Get<Button>(world, "Forces.leaveBtn"), "forces_leave")
            && anger?.GetParent() == window.Body;
    }
}
