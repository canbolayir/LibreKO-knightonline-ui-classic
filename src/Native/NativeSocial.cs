using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Prepares the native social windows the Classic social frame shows (nation chat, clan and join-request
/// boards, leader transfer, commander): names the controls the skin and the audits look for
/// and replaces symbol-only captions with the English words of the original client.
/// </summary>
public static class NativeSocial
{
    internal static void Register()
    {
        NativeWindows.Prepare("nation_chat", PrepareNationChat);
        NativeWindows.Prepare("commander", PrepareCommander);
        NativeCommandList.Register();
    }

    /// <summary>Runs the social preparer of a window built outside the client's window flow (audits).</summary>
    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (window.Id == "nation_chat") PrepareNationChat(window, world);
        else if (window.Id == "commander") PrepareCommander(window, world);
    }

    private static void PrepareNationChat(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("classic_social_controls")) return;
        window.SetMeta("classic_social_controls", 1);
        if (Native.Get<Button>(world, "NationChannels.ncRefresh") is { } refresh)
        {
            refresh.Name = "nation_refresh";
            refresh.Text = "Refresh";
        }
        NativeWindows.Name(Native.Get<Button>(world, "NationChannels.ncPublicTab"), "nation_public_tab");
        NativeWindows.Name(Native.Get<Button>(world, "NationChannels.ncPrivateTab"), "nation_private_tab");
        NativeWindows.Name(Native.Get<VBoxContainer>(world, "NationChannels.ncPublicRows"), "nation_public_rows");
        NativeWindows.Name(Native.Get<VBoxContainer>(world, "NationChannels.ncPrivateRows"), "nation_private_rows");
        // 3277df5: "Open" shows the chat room of the joined channel; it sits in the Classic button row.
        NativeWindows.Name(Native.Get<Button>(world, "NationChannels.ncPubOpen"), "nation_public_open");
        // fa3cfc3: the Private tab is a search row (room name, Search, Create room) and a room is entered with a
        // double-click; the members and search windows are gone (members and owner actions live in the room window).
        NativeWindows.Name(Native.Get<LineEdit>(world, "NationChannels.ncSearchEdit"), "nation_search_edit");
    }

    private static void PrepareCommander(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("classic_social_controls")) return;
        window.SetMeta("classic_social_controls", 1);
        // The direction toggle is a symbol in the client; the Classic button carries its word.
        if (window.Body.GetChildren().OfType<Button>().FirstOrDefault() is { } flip)
        {
            flip.Name = "commander_flip";
            flip.Text = "Rotate";
        }
    }
}
