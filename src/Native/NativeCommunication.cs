using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Windows;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterCommunication(PluginContext context)
    {
        foreach (var id in NativeCommunication.WindowIds) NativeWindows.Prepare(id, NativeCommunication.Prepare);
        NativeWindows.Prepare(ClassicChatRoomSkin.WindowId, NativeChatRoom.Prepare);
        NativeSocial.Register();
    }
}

/// <summary>
/// Prepares the native mail and messenger windows for the Classic skins: names the native controls,
/// mirrors row state into the metas the skins read and keeps the Classic list behaviour (immediate row
/// replacement, Classic drop-zone materials). The nation chat rooms use the Classic social frame.
/// </summary>
public static class NativeCommunication
{
    public static readonly string[] WindowIds = { "mail", "mailread", "mailcompose", "messenger" };

    private const string MessengerEmptyText = "No one else online.";

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        switch (window.Id)
        {
            case "mail": PrepareInbox(window, world); break;
            case "mailread": PrepareReader(window, world); break;
            case "mailcompose": PrepareCompose(window, world); break;
            case "messenger": PrepareMessenger(window, world); break;
        }
    }

    private static bool Prepared(HudWindow window, string gate) => window.HasMeta(gate);

    private static void PrepareInbox(HudWindow window, WorldRoot world)
    {
        if (Prepared(window, "classic_mail_controls")) return;
        var list = Native.Get<VBoxContainer>(world, "Mail.list");
        var scroll = Native.Get<ScrollContainer>(world, "Mail.listScroll");
        var filter = Native.Get<CheckButton>(world, "Mail.unreadOnlyToggle");
        var unread = Native.Get<Label>(world, "Mail.unreadPill");
        var status = Native.Get<Label>(world, "Mail.status");
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

    private static void DescribeMailRow(WorldRoot world, VBoxContainer list, Node node)
    {
        if (node is not PanelContainer panel || panel.GetParent() != list) return;
        var mails = Native.Get<List<MailEntry>>(world, "Mail.mails");
        var filter = Native.Get<CheckButton>(world, "Mail.unreadOnlyToggle");
        if (mails == null || filter == null) return;
        bool unreadOnly = filter.ButtonPressed;
        int index = list.GetChildren().OfType<PanelContainer>().Where(p => !p.IsQueuedForDeletion()).ToList().IndexOf(panel);
        var shown = mails.Where(m => !unreadOnly || !m.Read).ToList();
        if (index < 0 || index >= shown.Count) return;
        var mail = shown[index];
        bool store = mail.Kind == MailKind.Store;
        panel.SetMeta("mail_row", true);
        panel.SetMeta("mail_read", mail.Read);
        panel.SetMeta("mail_selected", mail.Id == Native.Get<int>(world, "Mail.selectedId"));
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

    private static void PrepareReader(HudWindow window, WorldRoot world)
    {
        if (Prepared(window, "classic_mail_controls")) return;
        var subject = Native.Get<Label>(world, "Mail.readSubject");
        var meta = Native.Get<Label>(world, "Mail.readMeta");
        var body = Native.Get<Label>(world, "Mail.readBody");
        var title = Native.Get<Label>(world, "Mail.readAttachmentTitle");
        var scroll = Native.Get<ScrollContainer>(world, "Mail.readAttachmentScroll");
        var attachments = Native.Get<VBoxContainer>(world, "Mail.readAttachments");
        var claim = Native.Get<Button>(world, "Mail.claimBtn");
        var delete = Native.Get<Button>(world, "Mail.deleteBtn");
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

    private static void DescribeAttachment(WorldRoot world, VBoxContainer attachments, Node node)
    {
        if (node.GetParent() != attachments) return;
        var mails = Native.Get<List<MailEntry>>(world, "Mail.mails");
        int selected = Native.Get<int>(world, "Mail.selectedId");
        var mail = mails?.FirstOrDefault(m => m.Id == selected);
        int index = node.GetIndex();
        if (mail == null || index >= mail.Items.Count) return;
        bool claimed = mail.Attachments == MailAttachmentState.Claimed || mail.Items[index].Remaining == 0;
        var row = !claimed && node.GetChildCount() > 0 ? node.GetChild(0) : node;
        row.SetMeta("mail_attachment_claimed", claimed);
    }

    private static void PrepareCompose(HudWindow window, WorldRoot world)
    {
        if (Prepared(window, "classic_mail_controls")) return;
        var named = new (string Field, string Name)[]
        {
            ("MailCompose.Recipient", "mail_to"), ("MailCompose.recipientSuggestions", "mail_to_suggest"), ("MailCompose.SubjectInput", "mail_subject"),
            ("MailCompose.bodyRemaining", "mail_body_remaining"), ("MailCompose.BodyInput", "mail_body"), ("MailCompose.Gold", "mail_gold"),
            ("MailCompose.attachTitle", "mail_attach_title"), ("MailCompose.AttachmentDrop", "mail_drop_zone"), ("MailCompose.attachRows", "mail_attach_rows"),
            ("MailCompose.sendBtn", "mail_send"), ("MailCompose.composeStatus", "mail_compose_status"),
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
        var hint = Native.Get<Label>(drop, "hint");
        var picks = Native.Get<System.Collections.ICollection>(world, "MailCompose.Attachments");
        void ClassicHint()
        {
            if (hint != null && picks != null && window.HasMeta("classic_mail")) hint.Visible = picks.Count == 0;
        }
        rows.ChildEnteredTree += _ => ClassicHint();
        NativeWindows.Sync(window, () =>
        {
            ClassicHint();
            if (drop.HasMeta("classic_mail_idle") && Native.Get<StyleBox>(drop, "idle") != drop.GetMeta("classic_mail_idle").AsGodotObject())
            {
                Native.Set(drop, "idle", drop.GetMeta("classic_mail_idle").AsGodotObject());
                Native.Set(drop, "hot", drop.GetMeta("classic_mail_hot").AsGodotObject());
            }
        });
        window.SetMeta("classic_mail_controls", 1);
    }

    private static void PrepareMessenger(HudWindow window, WorldRoot world)
    {
        if (Prepared(window, "classic_communication_controls")) return;
        var list = Native.Get<VBoxContainer>(world, "Messenger.msgrList");
        var to = Native.Get<LineEdit>(world, "Messenger.msgrToInput");
        var message = Native.Get<LineEdit>(world, "Messenger.msgrTextInput");
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
        PrependReplyHandler<List<MessengerBuddy>>(Game.Session.Messenger, "MessengerListEvent", Native.Get<object>(world, "Messenger"), "OnList", world, _ => DetachRows(list));
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
    private static void PrependReplyHandler<T>(object source, string evt, object? target, string handler, WorldRoot world, Action<T> before)
    {
        if (target == null || !Native.TryGet<Action<T>>(source, evt, out var current)) return;
        Prepend(source, evt, target, handler, world, current, before);
    }

    private static void Prepend(object net, string evt, object target, string handler, WorldRoot world, Delegate current, Delegate before)
    {
        var calls = current.GetInvocationList();
        if (!calls.Any(d => d.Target == target && d.Method.Name == handler)) return;
        if (_replyHooks.TryGetValue(evt, out var previous)) previous.Release();
        if (Native.Get<Delegate>(net, evt) is { } now) calls = now.GetInvocationList();
        if (!Native.Set(net, evt, Delegate.Combine(calls.Prepend(before).ToArray()))) return;
        _replyHooks[evt] = NativeHooks.Hold("Net." + evt + " (prepended)", () =>
        {
            Native.Set(net, evt, Delegate.Remove(Native.Get<Delegate>(net, evt), before));
            _replyHooks.Remove(evt);
        }, world);
    }

    /// <summary>The handler each network reply event runs ahead of the client's own.</summary>
    private static readonly Dictionary<string, NativeHooks.Hook> _replyHooks = new();

    internal static void Reset() => _replyHooks.Clear();
}
