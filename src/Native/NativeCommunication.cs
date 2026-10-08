using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterCommunication(PluginContext context)
    {
        foreach (var id in NativeCommunication.WindowIds) NativeWindows.Prepare(id, NativeCommunication.Prepare);
    }
}

/// <summary>
/// Prepares the native mail, messenger and chat-room windows for the Classic skins: names the native
/// controls, mirrors row state into the metas the skins read and keeps the Classic list behaviour
/// (immediate row replacement, bounded room log, Classic drop-zone materials).
/// </summary>
public static class NativeCommunication
{
    public static readonly string[] WindowIds = { "mail", "mailread", "mailcompose", "messenger", "chatrooms" };

    private const int RoomLogLines = 100;
    private const string MessengerEmptyText = "No one else online.";
    private const string RoomsEmptyText = "No rooms yet — create one.";

    public static void Prepare(HudWindow window, World world)
    {
        switch (window.Id)
        {
            case "mail": PrepareInbox(window, world); break;
            case "mailread": PrepareReader(window, world); break;
            case "mailcompose": PrepareCompose(window, world); break;
            case "messenger": PrepareMessenger(window, world); break;
            case "chatrooms": PrepareRooms(window, world); break;
        }
    }

    private static bool Prepared(HudWindow window, string gate) => window.HasMeta(gate);

    private static void PrepareInbox(HudWindow window, World world)
    {
        if (Prepared(window, "classic_mail_controls")) return;
        var list = Native.Get<VBoxContainer>(world, "_mailList");
        var scroll = Native.Get<ScrollContainer>(world, "_mailListScroll");
        var filter = Native.Get<CheckButton>(world, "_mailUnreadOnly");
        var unread = Native.Get<Label>(world, "_mailUnreadPill");
        var status = Native.Get<Label>(world, "_mailStatus");
        if (list == null || scroll == null || filter == null || unread == null || status == null) return;
        var actions = filter.GetParent().GetChildren().OfType<Button>().Where(b => b != filter).ToArray();
        if (actions.Length < 2) return;
        actions[0].Name = "mail_compose";
        actions[1].Name = "mail_refresh";
        filter.Name = "mail_unread_only";
        unread.Name = "mail_unread_count";
        scroll.Name = "mail_list_scroll";
        list.Name = "mail_list";
        status.Name = "mail_status";
        foreach (var row in list.GetChildren()) DescribeMailRow(world, list, row);
        NativeWindows.OnDescendantAdded(list, node => DescribeMailRow(world, list, node));
        window.SetMeta("classic_mail_controls", 1);
    }

    private static void DescribeMailRow(World world, VBoxContainer list, Node node)
    {
        if (node is not PanelContainer panel || panel.GetParent() != list) return;
        var mails = Native.Get<List<MailEntry>>(world, "_mails");
        var filter = Native.Get<CheckButton>(world, "_mailUnreadOnly");
        if (mails == null || filter == null) return;
        bool unreadOnly = filter.ButtonPressed;
        int index = list.GetChildren().OfType<PanelContainer>().Where(p => !p.IsQueuedForDeletion()).ToList().IndexOf(panel);
        var shown = mails.Where(m => !unreadOnly || !m.Read).ToList();
        if (index < 0 || index >= shown.Count) return;
        var mail = shown[index];
        bool store = mail.Kind == MailKind.Store;
        panel.SetMeta("mail_row", true);
        panel.SetMeta("mail_read", mail.Read);
        panel.SetMeta("mail_selected", mail.Id == Native.Get<int>(world, "_mailSelectedId"));
        panel.SetMeta("mail_store", store);
        panel.TooltipText = $"{mail.Subject}\nFrom {mail.Sender}\n{mail.SentAt.ToLocalTime():dd MMM yyyy HH:mm}";
        if (panel.GetChildCount() == 0 || panel.GetChild(0).GetChildCount() == 0 || panel.GetChild(0).GetChild(0) is not HBoxContainer row) return;
        var parts = row.GetChildren().OfType<Control>().ToArray();
        if (parts.Length < 3 || parts[1] is not VBoxContainer text || text.GetChildCount() < 2) return;
        parts[0].Name = "mail_row_marker";
        text.GetChild(0).Name = "mail_row_subject";
        text.GetChild(1).Name = "mail_row_sender";
        parts[^1].Name = "mail_row_date";
        if (parts.Length > 3 && parts[2] is TextureRect marker) marker.Name = "mail_row_attachment";
    }

    private static void PrepareReader(HudWindow window, World world)
    {
        if (Prepared(window, "classic_mail_controls")) return;
        var subject = Native.Get<Label>(world, "_mailReadSubject");
        var meta = Native.Get<Label>(world, "_mailReadMeta");
        var body = Native.Get<Label>(world, "_mailReadBody");
        var title = Native.Get<Label>(world, "_mailReadAttachmentTitle");
        var scroll = Native.Get<ScrollContainer>(world, "_mailReadAttachmentScroll");
        var attachments = Native.Get<VBoxContainer>(world, "_mailReadAttachments");
        var claim = Native.Get<Button>(world, "_mailClaimBtn");
        var delete = Native.Get<Button>(world, "_mailDeleteBtn");
        if (subject == null || meta == null || body == null || title == null || scroll == null || attachments == null || claim == null || delete == null) return;
        subject.Name = "mail_read_subject";
        meta.Name = "mail_read_meta";
        body.Name = "mail_read_body";
        title.Name = "mail_read_attachment_title";
        scroll.Name = "mail_read_attachment_scroll";
        attachments.Name = "mail_read_attachments";
        claim.Name = "mail_claim";
        delete.Name = "mail_delete";
        NativeWindows.OnDescendantAdded(attachments, node => DescribeAttachment(world, attachments, node));
        window.SetMeta("classic_mail_controls", 1);
    }

    private static void DescribeAttachment(World world, VBoxContainer attachments, Node node)
    {
        if (node.GetParent() != attachments) return;
        var mails = Native.Get<List<MailEntry>>(world, "_mails");
        int selected = Native.Get<int>(world, "_mailSelectedId");
        var mail = mails?.FirstOrDefault(m => m.Id == selected);
        int index = node.GetIndex();
        if (mail == null || index >= mail.Items.Count) return;
        bool claimed = mail.Attachments == MailAttachmentState.Claimed || mail.Items[index].Remaining == 0;
        var row = !claimed && node.GetChildCount() > 0 ? node.GetChild(0) : node;
        row.SetMeta("mail_attachment_claimed", claimed);
    }

    private static void PrepareCompose(HudWindow window, World world)
    {
        if (Prepared(window, "classic_mail_controls")) return;
        var named = new (string Field, string Name)[]
        {
            ("_mailTo", "mail_to"), ("_mailToSuggest", "mail_to_suggest"), ("_mailSubject", "mail_subject"),
            ("_mailBodyRemaining", "mail_body_remaining"), ("_mailBody", "mail_body"), ("_mailGold", "mail_gold"),
            ("_mailAttachTitle", "mail_attach_title"), ("_mailDropZone", "mail_drop_zone"), ("_mailAttachRows", "mail_attach_rows"),
            ("_mailSendBtn", "mail_send"), ("_mailComposeStatus", "mail_compose_status"),
        };
        var controls = named.Select(n => (Control: Native.Get<Control>(world, n.Field), n.Name)).ToArray();
        if (controls.Any(c => c.Control == null)) return;
        var send = (Button)controls.Single(c => c.Name == "mail_send").Control!;
        var cancel = send.GetParent().GetChildren().OfType<Button>().FirstOrDefault(b => b != send);
        if (cancel == null) return;
        foreach (var (control, name) in controls) control!.Name = name;
        cancel.Name = "mail_cancel";

        var drop = controls.Single(c => c.Name == "mail_drop_zone").Control!;
        var rows = controls.Single(c => c.Name == "mail_attach_rows").Control!;
        var hint = Native.Get<Label>(drop, "_hint");
        var picks = Native.Get<System.Collections.ICollection>(world, "_mailAttachments");
        void ClassicHint()
        {
            if (hint != null && picks != null && window.HasMeta("classic_mail")) hint.Visible = picks.Count == 0;
        }
        rows.ChildEnteredTree += _ => ClassicHint();
        NativeWindows.Sync(window, () =>
        {
            ClassicHint();
            if (drop.HasMeta("classic_mail_idle") && Native.Get<StyleBox>(drop, "_idle") != drop.GetMeta("classic_mail_idle").AsGodotObject())
            {
                Native.Set(drop, "_idle", drop.GetMeta("classic_mail_idle").AsGodotObject());
                Native.Set(drop, "_hot", drop.GetMeta("classic_mail_hot").AsGodotObject());
            }
        });
        window.SetMeta("classic_mail_controls", 1);
    }

    private static void PrepareMessenger(HudWindow window, World world)
    {
        if (Prepared(window, "classic_communication_controls")) return;
        var list = Native.Get<VBoxContainer>(world, "_msgrList");
        var to = Native.Get<LineEdit>(world, "_msgrToInput");
        var message = Native.Get<LineEdit>(world, "_msgrTextInput");
        if (list == null || to == null || message == null || list.GetParent() is not ScrollContainer scroll) return;
        var send = message.GetParent().GetChildren().OfType<Button>().FirstOrDefault();
        if (send == null) return;
        scroll.Name = "messenger_scroll";
        list.Name = "messenger_list";
        to.Name = "messenger_to";
        message.Name = "messenger_message";
        send.Name = "messenger_send";
        foreach (var row in list.GetChildren()) DescribeBuddy(list, row);
        NativeWindows.OnDescendantAdded(list, node => DescribeBuddy(list, node));
        PrependReplyHandler<List<MessengerBuddy>>("MessengerListEvent", "OnMessengerList", world, _ => DetachRows(list));
        window.SetMeta("classic_communication_controls", 1);
    }

    private static void DescribeBuddy(VBoxContainer list, Node node)
    {
        if (node.GetParent() != list) return;
        DetachQueued(list);
        if (node is not PanelContainer row || row.GetChildCount() == 0 || row.GetChild(0) is not HBoxContainer line) return;
        var parts = line.GetChildren().OfType<Control>().ToArray();
        if (parts.Length < 3 || parts[0] is not Label name || parts[1] is not Label state) return;
        row.SetMeta("communication_row", "buddy");
        row.SetMeta("communication_online", state.Text == "Online");
        name.Name = "communication_name";
        name.TooltipText = name.Text;
        state.Name = "communication_state";
        parts[2].Name = "communication_action";
    }

    private static void PrepareRooms(HudWindow window, World world)
    {
        if (Prepared(window, "classic_communication_controls")) return;
        var list = Native.Get<VBoxContainer>(world, "_chatRoomList");
        var name = Native.Get<LineEdit>(world, "_chatRoomNameInput");
        var status = Native.Get<Label>(world, "_chatRoomStatus");
        var logScroll = Native.Get<ScrollContainer>(world, "_chatRoomLogScroll");
        var log = Native.Get<VBoxContainer>(world, "_chatRoomLog");
        var message = Native.Get<LineEdit>(world, "_chatRoomSayInput");
        if (list == null || name == null || status == null || logScroll == null || log == null || message == null) return;
        if (list.GetParent() is not ScrollContainer listScroll) return;
        var create = name.GetParent().GetChildren().OfType<Button>().ToArray();
        var say = message.GetParent().GetChildren().OfType<Button>().ToArray();
        if (create.Length < 2 || say.Length < 2) return;
        listScroll.Name = "rooms_scroll";
        list.Name = "rooms_list";
        name.Name = "rooms_name";
        create[0].Name = "rooms_create";
        create[1].Name = "rooms_refresh";
        status.Name = "rooms_status";
        logScroll.Name = "rooms_log_scroll";
        log.Name = "rooms_log";
        message.Name = "rooms_message";
        say[0].Name = "rooms_send";
        say[1].Name = "rooms_leave";
        foreach (var row in list.GetChildren()) DescribeRoom(list, row);
        foreach (var line in log.GetChildren()) DescribeLine(log, line);
        NativeWindows.OnDescendantAdded(list, node => DescribeRoom(list, node));
        NativeWindows.OnDescendantAdded(log, node => DescribeLine(log, node));
        PrependReplyHandler<List<ChatRoomEntry>>("ChatRoomListEvent", "OnChatRoomList", world, _ => DetachRows(list));
        void RoomId() => window.SetMeta("communication_room_id", Native.Get<int>(world, "_chatRoomCurrentId"));
        RoomId();
        NativeWindows.Sync(window, RoomId);
        window.SetMeta("classic_communication_controls", 1);
    }

    private static void DescribeRoom(VBoxContainer list, Node node)
    {
        if (node.GetParent() != list) return;
        DetachQueued(list);
        if (node is not PanelContainer row || row.GetChildCount() == 0 || row.GetChild(0) is not HBoxContainer line) return;
        var parts = line.GetChildren().OfType<Control>().ToArray();
        if (parts.Length < 3 || parts[0] is not Label name) return;
        bool joined = parts[2] is Label;
        row.SetMeta("communication_row", "room");
        row.SetMeta("communication_selected", joined);
        name.Name = "communication_name";
        name.TooltipText = name.Text;
        parts[1].Name = "communication_count";
        parts[2].Name = joined ? "communication_state" : "communication_action";
    }

    private static void DescribeLine(VBoxContainer log, Node node)
    {
        if (node.GetParent() != log) return;
        DetachQueued(log);
        var live = log.GetChildren().Where(c => c != node).ToList();
        while (live.Count >= RoomLogLines)
        {
            log.RemoveChild(live[0]);
            live[0].QueueFree();
            live.RemoveAt(0);
        }
        if (node is not HBoxContainer line || line.GetChildCount() < 2 || line.GetChild(0) is not Label author || line.GetChild(1) is not Label body) return;
        author.Name = "communication_author";
        author.TooltipText = author.Text.EndsWith(':') ? author.Text[..^1] : author.Text;
        body.Name = "communication_body";
    }

    /// <summary>Removes rows the client queued for deletion so a refreshed list never counts or lays out stale rows.</summary>
    internal static void DetachQueued(Node parent)
    {
        foreach (var child in parent.GetChildren())
            if (child.IsQueuedForDeletion()) parent.RemoveChild(child);
    }

    /// <summary>Detaches every current row before the client rebuilds the list from a fresh reply.</summary>
    public static void DetachRows(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// Runs <paramref name="before"/> ahead of the client's own reply handler, so the replaced rows are gone
    /// when the client checks whether the refreshed list is empty.
    /// </summary>
    private static void PrependReplyHandler<T>(string evt, string handler, World world, Action<T> before)
    {
        var net = Net.I;
        if (net == null || !Native.TryGet<Action<T>>(net, evt, out var current)) return;
        var calls = current.GetInvocationList();
        if (!calls.Any(d => d.Target == world && d.Method.Name == handler)) return;
        if (_replyHooks.TryGetValue(evt, out var previous)) calls = calls.Where(d => d != previous).ToArray();
        _replyHooks[evt] = before;
        Native.Set(net, evt, Delegate.Combine(calls.Prepend(before).ToArray()));
    }

    private static readonly Dictionary<string, Delegate> _replyHooks = new();
}
