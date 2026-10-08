using LibreKO;
using LibreKO.Network;

/// <summary>
/// Character, quest, NPC and party fixtures. A server reply reaches both the client and the plugin's
/// native layer at runtime; these entry points do the same when an audit drives the client directly.
/// </summary>
public static partial class PreviewFixtures
{
    public static void ShowQuestView(World world, QuestView view)
    {
        Call(world, "ShowQuestView", view);
        NativeNpc.QuestViewShown(world, view);
    }

    public static void OnQuestView(World world, QuestView view)
    {
        Call(world, "OnQuestView", view);
        if (!view.Notification && view.Open && view.Page != QuestPageKind.Conversation) NativeNpc.QuestViewShown(world, view);
    }

    public static void BuildInZoneLeaveUi(World world)
    {
        Call(world, "BuildInZoneLeaveUi");
        NativeEvents.Prepare(world);
    }

    public static void RefreshInZoneLeaveUi(World world)
    {
        Call(world, "RefreshInZoneLeaveUi");
        NativeEvents.Refresh(world);
    }

    public static void OnBbsList(World world, int page, int total, List<PartyBbsEntry> entries)
    {
        Call(world, "OnBbsList", page, total, entries);
        NativeParty.Listed(world, entries);
    }
}

/// <summary>Reads the talking NPC's portrait descriptor for one audit world.</summary>
public sealed class NpcPortraitProbe(World world)
{
    public GameNpcPortrait? NpcPortrait => NativeNpc.DialogPortrait(world);
}
