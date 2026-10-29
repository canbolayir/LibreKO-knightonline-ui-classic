using Godot;
using LibreKO;
using KnightOnlineUiClassic.Windows;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Prepares the client's chat room window for the Classic skin: names the native controls the skin and the audits
/// look for, dresses the room's popups (room settings, voice settings, a member's volume, the emoticon pickers, the
/// member menu) as the client creates them, and turns the client's minimized blink (a modulate tween on the window)
/// into the Classic attention styler, as the whisper windows do. Since fa3cfc3 the log and the composer are the
/// client's shared thread and composer controls (also used by the whispers); the reaction popups live on the
/// World's own layer and are dressed by <see cref="NativeChatReactions"/>.
/// </summary>
public static class NativeChatRoom
{
    public const string ControlsMeta = "classic_chat_room_controls";
    private const string PopupsMeta = "classic_chat_room_popups";

    /// <summary>The room's World members: the thread, the composer, the voice row and the members column.</summary>
    private static readonly (string Field, string Name)[] Named =
    {
        ("ChatRoom.RoomThread", "room_log_scroll"), ("ChatRoom.RoomComposer", "room_composer_row"),
        ("ChatRoom.roomVoiceRow", "room_voice_row"), ("ChatRoom.roomMembersToggle", "room_members_toggle"),
        ("ChatRoom.roomConfigButton", "room_config"), ("ChatRoom.roomJoin", "room_join"), ("ChatRoom.roomDeafen", "room_deafen"), ("ChatRoom.roomVolume", "room_volume"),
        ("ChatRoom.roomMic", "room_mic"), ("ChatRoom.roomVoiceGear", "room_voice_gear"), ("ChatRoom.roomVoiceLeave", "room_voice_leave"),
        ("ChatRoom.roomMembersColumn", "room_members_column"), ("ChatRoom.roomRosterRows", "room_roster_rows"), ("ChatRoom.roomRosterEmpty", "room_roster_empty"),
        ("ChatRoom.roomLeaderBox", "room_leader_box"), ("ChatRoom.roomLeaderHandOver", "room_leader_handover"), ("ChatRoom.roomLeaderKick", "room_leader_kick"),
    };

    /// <summary>The thread's post column and its "Load older messages" button.</summary>
    private static readonly (string Field, string Name)[] ThreadNamed = { ("log", "room_log"), ("older", "room_older") };

    /// <summary>The composer's field, input, voice-note and send buttons, and the recording strip.</summary>
    private static readonly (string Field, string Name)[] ComposerNamed =
    {
        ("field", "room_field"), ("Input", "room_input"), ("record", "room_record"), ("send", "room_send"),
        ("strip", "room_record_strip"), ("clock", "room_record_clock"), ("levelBar", "room_record_level"),
    };

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        NativeChatReactions.Attach(world);
        if (window.HasMeta(ControlsMeta)) return;
        var controls = Named.Select(n => (Control: Native.Get<Control>(world, n.Field), n.Name)).ToList();
        if (controls.Any(c => c.Control == null)) return;
        Control thread = controls[0].Control!, composer = controls[1].Control!;
        controls.AddRange(ThreadNamed.Select(n => (Native.Get<Control>(thread, n.Field), n.Name)));
        controls.AddRange(ComposerNamed.Select(n => (Native.Get<Control>(composer, n.Field), n.Name)));
        if (controls.Any(c => c.Control == null)) return;
        // The row of the members column and the log, voice and composer stack: the client's own flow, placed by the skin.
        if (thread.GetParent()?.GetParent() is not HBoxContainer main) return;
        foreach (var (control, name) in controls) control!.Name = name;
        main.Name = "room_main";
        NativeWindows.Name(Native.Get<Button>(world, "ChatRoom.roomChannelsButton"), "room_channels");
        if (Native.Get<CanvasLayer>(world, "ChatRoom.roomLayer") is { } layer) StylePopups(layer);
        StylePopups(window);
        // The composer keeps its emoticon picker as its own child.
        StylePopups(composer);
        Attention(window, world);
        window.SetMeta(ControlsMeta, 1);
        ClassicChatRoomFrame.Of(window)?.Bind(window);
    }

    /// <summary>
    /// Dresses the popups the client parks on the room layer, the composer or (in a preview) inside the window as
    /// they are created: the Classic menu panel for the panels, the Classic drop-down look for the member menu.
    /// </summary>
    private static void StylePopups(Node holder)
    {
        if (holder.HasMeta(PopupsMeta)) return;
        holder.SetMeta(PopupsMeta, true);
        foreach (var popup in holder.GetChildren().OfType<Popup>()) Style(popup);
        holder.ChildEnteredTree += node =>
        {
            if (node is not Popup popup) return;
            // The emoticon and reaction popups arrive complete and are shown right away: they are dressed before
            // the client sizes and places them. The room's own panels get their content after they are added.
            if (ClassicReactionStyle.Owns(popup)) { if (Plugin.Active) Style(popup); }
            else Callable.From(() => { if (Plugin.Active && GodotObject.IsInstanceValid(popup)) Style(popup); }).CallDeferred();
        };
    }

    private static void Style(Popup popup)
    {
        if (popup is PopupMenu menu) ClassicSocialStyle.Menu(menu);
        else if (ClassicReactionStyle.Owns(popup)) ClassicReactionStyle.Apply((PopupPanel)popup);
        else if (popup is PopupPanel panel) ClassicChatRoomSkin.StylePopup(panel);
    }

    /// <summary>
    /// The client blinks the minimized room by fading the whole window when a line from someone else arrives;
    /// the Classic compact bar carries the whisper attention blink instead, until the room is restored.
    /// </summary>
    private static void Attention(HudWindow window, WorldRoot world)
    {
        bool attention = false;
        NativeWindows.Sync(window, () =>
        {
            var blink = Native.Get<Tween>(world, "ChatRoom.roomBlink");
            if (blink != null && blink.IsValid())
            {
                blink.Kill();
                window.Modulate = Colors.White;
                if (window.AttentionStyler is { } styler) { attention = true; styler(true); }
            }
            else if (blink == null && attention)
            {
                attention = false;
                window.AttentionStyler?.Invoke(false);
            }
        });
    }
}
