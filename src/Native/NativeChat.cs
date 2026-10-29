using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The Classic chat log over the client's chat store: Classic channel formatting and colours, item links,
/// player menus, nearby players, service notices routed to the chat and Classic bubble colours.
/// </summary>
public static class NativeChat
{
    private const string NoticeColour = "ffe24a";
    private const string TimestampColour = "8d939b";
    private const int PublishedFallback = 200;
    private const ulong NearbyPollMs = 5000;

    private sealed record Entry(long Seq, int Category, byte Type, string Name, int Nation, bool Gm, string Text, string Bbcode, DateTime Time)
    {
        public string? Rendered;
        public string? RenderedInk;
    }

    private sealed class NearbyState
    {
        public ulong NextPoll;
        public int Zone = -1;
    }

    private static readonly Dictionary<long, Entry> _entries = new();
    private static readonly ConditionalWeakTable<WorldRoot, NearbyState> _nearby = new();
    private static Type? ChatSystemType => Native.ClientType("LibreKO.Features.Social.ChatSystem");

    private static object? ChatOf(WorldRoot world) => Native.Get<object>(world, "Chat");

    /// <summary>
    /// Text the client put into its own hidden chat input when it opened the chat with a prefill (the command
    /// list's "/PM ", "/RoyalOrder "...). With the Classic chat the client only raises the input request, so the
    /// Classic input takes the text once and the client's input is cleared.
    /// </summary>
    public static string TakeTyped()
    {
        if (NativeHudChat.World is not { } world || ChatOf(world) is not { } chat || Native.Get<LineEdit>(chat, "input") is not { } input) return "";
        string text = input.Text;
        if (text.Length > 0) input.Clear();
        return text;
    }

    /// <summary>Whether the plugin replaces the native chat panel (the Classic chat is active).</summary>
    public static bool ChatReplaced => Native.Call(PluginHost.Ui, "HudHidden", HudPart.Chat) is true;

    internal static void Attach(WorldRoot world)
    {
        if (!ChatReplaced || ChatOf(world) is not { } chat) return;
        Native.Set(chat, "StatusNotice", (Action<string>)(message => Info(chat, message)));
        if (Game.Connection is not { } net) return;
        Action<ChatLine> recolour = line => RecolourBubble(chat, line);
        NativeHooks.Listen("Net.ChatEvent", () => Game.Session.Chat.ChatEvent += recolour, () => Game.Session.Chat.ChatEvent -= recolour, world);
    }

    /// <summary>Forgets the rendered chat history cache.</summary>
    internal static void Reset() => _entries.Clear();

    /// <summary>Adds a service or status notice to the chat as a yellow system line.</summary>
    public static void Info(object chat, string message)
    {
        var entryType = Native.ClientType("LibreKO.Features.Social.ChatSystem+ChatEntry");
        if (entryType == null || Native.Call(entryType, "Raw", ChatCategory.System,
                $"[color=#{NoticeColour}]{ClassicChatFormat.Esc(message)}[/color]") is not { } entry) return;
        Native.Call(chat, "AddEntry", entry);
    }

    private static void RecolourBubble(object chat, ChatLine line)
    {
        if (line.Type == ChatType.Private || line.CharId < 0 && line.Name.Length == 0) return;
        if (Native.Get<System.Collections.IDictionary>(chat, "Bubbles.bubbles") is not { } bubbles || !bubbles.Contains(line.CharId)) return;
        if (bubbles[line.CharId] is not { } bubble || Native.Get<Label3D>(bubble, "Label") is not { } label) return;
        var colour = new Color("#" + ClassicChatFormat.Channel((byte)line.Type, line.Nation).Color);
        colour.A = label.Modulate.A;
        label.Modulate = colour;
    }

    /// <summary>
    /// The chat log in Classic form: entries of the categories in <paramref name="mask"/>, private messages
    /// excluded (they have their own windows), optional timestamps and five custom channel colours
    /// (General, Shout, Party, Clan, Alliance) as a comma-separated list. Chat room lines stay in the room
    /// window (ff78eef), so the log carries none.
    /// </summary>
    public static IReadOnlyList<string> ReadHistory(int mask, bool timestamps, string colors)
    {
        if (NativeHudChat.World is not { } world || ChatOf(world) is not { } chat) return Plugin.Kit.Game.Chat.History;
        return ReadHistory(chat, mask, timestamps, colors) ?? Plugin.Kit.Game.Chat.History;
    }

    /// <summary>The Classic history of one client chat store (the audits read a store they fill themselves).</summary>
    public static IReadOnlyList<string>? ReadHistory(object chat, int mask, bool timestamps, string colors)
    {
        if (Native.Get<System.Collections.IEnumerable>(chat, "store") is not { } store) return null;
        var overrides = colors.Split(',');
        var result = new List<string>();
        var present = new HashSet<long>();
        foreach (var raw in store)
        {
            var entry = Read(raw);
            present.Add(entry.Seq);
            if (entry.Category == (int)ChatCategory.Whisper || (entry.Category & mask) == 0) continue;
            string hex = ClassicChatFormat.Channel(entry.Type, entry.Nation).Color;
            int slot = ClassicChatFormat.ColourSlot(entry.Type);
            if (slot >= 0 && overrides.Length == 5 && Color.HtmlIsValid(overrides[slot])) hex = overrides[slot];
            string line = Render(entry, hex);
            if (timestamps) line = $"[color=#{TimestampColour}]{entry.Time:HH:mm}[/color] " + line;
            result.Add(line);
        }
        foreach (long seq in _entries.Keys.Where(seq => !present.Contains(seq)).ToArray()) _entries.Remove(seq);
        int max = ChatSystemType is { } type && Native.TryGet<int>(type, "PublishedMax", out int published) ? published : PublishedFallback;
        if (result.Count > max) result.RemoveRange(0, result.Count - max);
        return result;
    }

    private static Entry Read(object raw)
    {
        long seq = Native.Get<long>(raw, "Seq");
        if (_entries.TryGetValue(seq, out var cached)) return cached;
        var entry = new Entry(seq, (int)Native.Get<ChatCategory>(raw, "Category"), (byte)Native.Get<ChatType>(raw, "Type"),
            Native.Get<string>(raw, "Name") ?? "", Native.Get<int>(raw, "Nation"), Native.Get<bool>(raw, "Gm"),
            Native.Get<string>(raw, "Text") ?? "", Native.Get<string>(raw, "Bbcode") ?? "", Native.Get<DateTime>(raw, "Time"));
        _entries[seq] = entry;
        return entry;
    }

    private static string Render(Entry entry, string hex)
    {
        if (entry.Bbcode.Length > 0) return entry.Bbcode;
        if (entry.Rendered != null && entry.RenderedInk == hex) return entry.Rendered;
        string body = ChatSystemType is { } type && Native.Call(type, "RenderText", entry.Text, hex) is string text
            ? text : $"[color=#{hex}]{ClassicChatFormat.Esc(entry.Text)}[/color]";
        entry.Rendered = ClassicChatFormat.Line(entry.Type, entry.Name, entry.Nation, entry.Gm, entry.Text, hex, body, links: true);
        entry.RenderedInk = hex;
        return entry.Rendered;
    }

    /// <summary>Shift-click on an inventory item while typing in the Classic chat links it into the draft.</summary>
    public static (int Id, string Name)? LinkInventoryItem(int slot)
    {
        if (NativeHudChat.World is not { } world || ChatOf(world) is not { } chat || !Native.Get<bool>(chat, "PluginTyping")) return null;
        if (!Input.IsKeyPressed(Key.Shift) || Game.Session.Self.Inventory is not { } inventory) return null;
        if (slot < 0 || slot >= inventory.Length || inventory[slot].IsEmpty) return null;
        int id = inventory[slot].ItemId;
        if (id == ChatItemLink.RefusedItem) return null;
        return (id, ItemData.DisplayName(id));
    }

    public static void ShowLinkTooltip(int itemId)
    {
        if (NativeHudChat.World is { } world) Native.Call(world, "ItemTooltip.ShowItem", itemId);
    }

    public static void HideLinkTooltip()
    {
        if (NativeHudChat.World is { } world) Native.Call(world, "ItemTooltip.Hide");
    }

    public static void PlayerMenu(string name, Vector2 at)
    {
        if (NativeHudChat.World is { } world) Native.Call(world, "PlayerMenu.ShowByName", name, at);
    }

    /// <summary>
    /// Players near the character. Asks the server for the zone roster at most every five seconds, starts
    /// over in a new zone and merges the roster with the players in view, as the native nearby card does.
    /// </summary>
    public static IReadOnlyList<NearbyRow> NearbyPlayers()
    {
        if (NativeHudChat.World is not { } world || !Native.Get<bool>(world, "WorldReady") || Native.Get<Node3D>(world, "Avatar.Body") == null)
            return Array.Empty<NearbyRow>();
        var state = _nearby.GetOrCreateValue(world);
        int zone = Native.Get<int>(world, "ZoneId");
        if (state.Zone != zone)
        {
            state.Zone = zone;
            Native.Get<System.Collections.IList>(world, "ChatHost.NearbyRows")?.Clear();
            Native.Get<System.Collections.IList>(world, "ChatHost.NearbyRows")?.Clear();
            state.NextPoll = 0;
            Native.Set(world, "ChatHost.nearbyFirstPoll", true);
        }
        ulong now = Time.GetTicksMsec();
        if (now >= state.NextPoll && Game.Connection is { } net)
        {
            Game.Requests.NearbyPlayers.Request(Native.Get<bool>(world, "ChatHost.nearbyFirstPoll"));
            Native.Set(world, "ChatHost.nearbyFirstPoll", false);
            state.NextPoll = now + NearbyPollMs;
        }
        Native.TryCall(world, "ChatHost.RebuildNearby", out _);
        return Native.Get<List<NearbyRow>>(world, "ChatHost.NearbyRows")?.ToArray() ?? Array.Empty<NearbyRow>();
    }
}

/// <summary>
/// Classic private-message windows: the plugin's whisper skin (with the Classic line builder on clients
/// that keep a whisper log of their own, and the thread rows restyled in place on clients where whispers
/// share the room's thread and composer), the unread attention styler instead of the native fade blink,
/// and the retail compose behaviour (Enter focuses the last addressed conversation, sending leaves the
/// input, a click outside every conversation clears it).
/// </summary>
public static class NativeWhispers
{
    private const string Prefix = "whisper_";
    private const string StyledLine = "classic_whisper_line";

    private sealed class State
    {
        public string? ComposeTarget;
    }

    private static readonly ConditionalWeakTable<WorldRoot, State> _states = new();

    internal static bool IsWhisper(HudWindow window) =>
        window.Id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && Native.WorldOf(window) is { } world
        && Native.Get<CanvasLayer>(world, "Whispers.whisperLayer") is { } layer && window.GetParent() == layer;

    private static Type? ChatSystemType => Native.ClientType("LibreKO.Features.Social.ChatSystem");

    /// <summary>A whisper line as the client's chat renders it: item links and emoticons in BBCode.</summary>
    public static string Render(string text, string hex) =>
        ChatSystemType is { } type && Native.Call(type, "RenderText", text, hex) is string rendered ? rendered : $"[color=#{hex}]{ClassicChatFormat.Esc(text)}[/color]";

    /// <summary>A whisper line without markup: item links become their bracketed names.</summary>
    public static string Plain(string text) => ChatSystemType is { } type && Native.Call(type, "PlainText", text) is string plain ? plain : text;

    /// <summary>Shows the item tooltip of a clicked item link.</summary>
    public static void ShowLink(Variant meta)
    {
        if (ChatSystemType is { } type && Native.Call(type, "ItemOf", meta) is int itemId && itemId > 0) NativeChat.ShowLinkTooltip(itemId);
    }

    /// <summary>
    /// Styles the popups the client opens for its whisper windows (size and transparency, the emoticon and
    /// emoji picker) as they are created; they live on the whisper layer, outside the windows.
    /// </summary>
    public static void StylePopups(HudWindow window, Action<PopupPanel> style)
    {
        if (window.GetParent() is not CanvasLayer layer || layer.HasMeta("classic_whisper_popups")) return;
        layer.SetMeta("classic_whisper_popups", true);
        foreach (var popup in layer.GetChildren().OfType<PopupPanel>()) style(popup);
        layer.ChildEnteredTree += node => { if (node is PopupPanel popup) Callable.From(() => { if (Plugin.Active && GodotObject.IsInstanceValid(popup)) style(popup); }).CallDeferred(); };
    }

    /// <summary>The conversation that Enter focuses, if any.</summary>
    public static string? ComposeTarget(WorldRoot world) => _states.GetOrCreateValue(world).ComposeTarget;

    internal static void Attach(WorldRoot world)
    {
        if (!NativeChat.ChatReplaced || world.HasNode("native_whisper_input")) return;
        Callable.From(() => { if (GodotObject.IsInstanceValid(world)) world.AddChild(new NativeWhisperInput(world) { Name = "native_whisper_input" }); }).CallDeferred();
    }

    private static object? Conversation(WorldRoot world, string name) =>
        Native.Get<System.Collections.IDictionary>(world, "Whispers.Windows") is { } chats && chats.Contains(name) ? chats[name] : null;

    /// <summary>
    /// The conversation's composer on clients where whispers share the room's thread and composer (fa3cfc3);
    /// null on older clients, whose conversation holds its own log, input and Send button.
    /// </summary>
    private static Control? ComposerOf(object chat) => Native.Has(chat, "Composer") ? Native.Get<Control>(chat, "Composer") : null;

    /// <summary>The conversation's input. On fa3cfc3 <c>Input</c> is a property that reads the composer.</summary>
    private static LineEdit? InputOf(object chat) =>
        Native.Has(chat, "Composer") ? (ComposerOf(chat) is { } composer ? Native.Get<LineEdit>(composer, "Input") : null)
        : Native.Get<LineEdit>(chat, "Input");

    /// <summary>
    /// The native parts of a conversation that runs on the room thread and composer (fa3cfc3), found in the
    /// window body before the skin moves them: the thread with its log and "Load older messages" button, and the
    /// composer's field (the input), recording strip, record and send buttons. Null for the older conversation.
    /// </summary>
    public static WhisperParts? Parts(HudWindow window)
    {
        var thread = window.Body.GetChildren().OfType<ScrollContainer>().FirstOrDefault(n => n.GetType().Name == "ChatThreadView");
        var composer = window.Body.GetChildren().OfType<Container>().FirstOrDefault(n => n.GetType().Name == "ChatComposer");
        if (thread == null || composer == null) return null;
        var log = Native.Get<VBoxContainer>(thread, "log");
        var older = Native.Get<Button>(thread, "older");
        var input = Native.Get<LineEdit>(composer, "Input");
        var field = Native.Get<HBoxContainer>(composer, "field");
        var strip = Native.Get<HBoxContainer>(composer, "strip");
        var record = Native.Get<Button>(composer, "record");
        var send = Native.Get<Button>(composer, "send");
        if (log == null || older == null || input == null || field == null || strip == null || record == null || send == null) return null;
        return new WhisperParts(thread, log, older, composer, input, field, strip, record, send);
    }

    /// <summary>
    /// The posts behind the thread's rows (the thread keeps them for reaction and voice-note refreshes): row root
    /// to post. Notices and day markers have no entry.
    /// </summary>
    public static Dictionary<Node, LibreKO.Domain.RoomPost> Posts(ScrollContainer thread)
    {
        var posts = new Dictionary<Node, LibreKO.Domain.RoomPost>();
        if (Native.Get<System.Collections.IDictionary>(thread, "rows") is not { } rows) return posts;
        foreach (System.Collections.DictionaryEntry entry in rows)
            if (entry.Key is LibreKO.Domain.RoomPost post && entry.Value != null && Native.Get<Control>(entry.Value, "Root") is { } root)
                posts[root] = post;
        return posts;
    }

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (window.FindChild("classic_whisper", false, false) != null) return;
        string name = window.Id[Prefix.Length..];
        var state = _states.GetOrCreateValue(world);
        var chat = Conversation(world, name);
        var composer = chat != null ? ComposerOf(chat) : null;
        var input = chat != null ? InputOf(chat) : null;
        // The older conversation keeps its own log; on fa3cfc3 the skin restyles the thread's rows in place.
        var log = chat != null && composer == null ? Native.Get<VBoxContainer>(chat, "Log") : null;

        if (NativeHud.WhisperStyler is { } style)
        {
            style(window);
            if (log != null && NativeHud.WhisperLineBuilder is { } build) StyleLines(name, log, build);
        }

        if (input != null)
        {
            input.KeepEditingOnTextSubmit = false;
            input.FocusEntered += () => state.ComposeTarget = name;
            input.TextSubmitted += _ => Sent(state, name, input);
            // fa3cfc3: Send is the composer's icon button (tooltip "Send"); older clients have a "Send" caption.
            var send = composer != null ? Native.Get<Button>(composer, "send")
                : Native.Descendants(window).OfType<Button>().FirstOrDefault(b => b.Text == "Send");
            if (send != null) send.Pressed += () => Sent(state, name, input);
        }
        window.MinimizedChanged += minimized =>
        {
            if (minimized && state.ComposeTarget == name) state.ComposeTarget = null;
            else if (!minimized) state.ComposeTarget = name;
        };
        window.TreeExiting += () => { if (state.ComposeTarget == name) state.ComposeTarget = null; };
        if (chat != null)
        {
            bool attention = false;
            NativeWindows.Sync(window, () =>
            {
                var blink = Native.Get<Tween>(chat, "Blink");
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
        if (window.Visible && !window.Minimized && input?.HasFocus() == true) state.ComposeTarget = name;
    }

    /// <summary>
    /// After the client sent (or ignored an empty) message: keep the conversation addressed, clear the
    /// input and leave it, also after the client's own deferred refocus.
    /// </summary>
    private static void Sent(State state, string name, LineEdit input)
    {
        if (!GodotObject.IsInstanceValid(input)) return;
        state.ComposeTarget = name;
        input.Clear();
        input.ReleaseFocus();
        Callable.From(() => { if (GodotObject.IsInstanceValid(input) && input.HasFocus()) input.ReleaseFocus(); }).CallDeferred();
    }

    /// <summary>Rebuilds the conversation history with the Classic line builder and restyles every new line.</summary>
    private static void StyleLines(string name, VBoxContainer log, Func<string, bool, bool, string, Control> build)
    {
        foreach (var row in log.GetChildren())
        {
            log.RemoveChild(row);
            row.QueueFree();
        }
        // The facade's history keeps the same (mine, notice, text) shape on every client.
        var whispers = Plugin.Kit.Game.Whispers;
        foreach (var line in whispers.History(name))
            log.AddChild(Styled(build(name, line.Mine, line.Notice, line.Text)));
        NativeWindows.OnDescendantAdded(log, node =>
        {
            if (node.GetParent() != log || node.HasMeta(StyledLine)) return;
            var history = whispers.History(name);
            if (history.Count == 0) return;
            var line = history[^1];
            Callable.From(() =>
            {
                if (!GodotObject.IsInstanceValid(node) || node.GetParent() != log) return;
                int index = node.GetIndex();
                var styled = Styled(build(name, line.Mine, line.Notice, line.Text));
                log.AddChild(styled);
                log.MoveChild(styled, index);
                log.RemoveChild(node);
                node.QueueFree();
            }).CallDeferred();
        });
    }

    private static Control Styled(Control line)
    {
        line.SetMeta(StyledLine, true);
        return line;
    }

    /// <summary>
    /// Hit-tests a left click against the drawn conversation stack: the conversation on top under the
    /// cursor becomes the compose target; a click elsewhere, or on a minimized conversation, clears it
    /// and leaves any whisper input.
    /// </summary>
    public static void FocusAt(WorldRoot world, Vector2 at)
    {
        var state = _states.GetOrCreateValue(world);
        if (Native.Get<CanvasLayer>(world, "Whispers.whisperLayer") is not { } layer
            || Native.Get<System.Collections.IDictionary>(world, "Whispers.Windows") is not { } chats) return;
        for (int i = layer.GetChildCount() - 1; i >= 0; i--)
        {
            if (layer.GetChild(i) is not HudWindow window || !window.Visible || !window.GetGlobalRect().HasPoint(at)) continue;
            if (window.Minimized) break;
            foreach (var chat in chats.Values)
            {
                if (chat == null || Native.Get<HudWindow>(chat, "Window") != window) continue;
                state.ComposeTarget = Native.Get<string>(chat, "Name");
                return;
            }
        }
        state.ComposeTarget = null;
        foreach (var chat in chats.Values)
            if (chat != null && InputOf(chat) is { } input && GodotObject.IsInstanceValid(input) && input.HasFocus())
                input.ReleaseFocus();
    }

    /// <summary>Focuses the input of the compose target if that conversation is expanded.</summary>
    public static bool TryFocusInput(WorldRoot world)
    {
        var state = _states.GetOrCreateValue(world);
        if (state.ComposeTarget is not { } name || Conversation(world, name) is not { } chat) return false;
        if (Native.Get<HudWindow>(chat, "Window") is not { } window || !GodotObject.IsInstanceValid(window)
            || !window.Visible || window.Minimized || InputOf(chat) is not { } input || !input.IsVisibleInTree()) return false;
        input.GrabFocus();
        return true;
    }
}

/// <summary>The native parts of a conversation on the room thread and composer (see <see cref="NativeWhispers.Parts"/>).</summary>
public sealed record WhisperParts(
    ScrollContainer Thread, VBoxContainer Log, Button Older, Container Composer, LineEdit Input,
    HBoxContainer Field, HBoxContainer Strip, Button Record, Button Send);

/// <summary>Runs ahead of the world's own input handling (child nodes receive input first).</summary>
public partial class NativeWhisperInput : Node
{
    private readonly WorldRoot? _world;

    public NativeWhisperInput() { }

    public NativeWhisperInput(WorldRoot world) => _world = world;

    public override void _Input(InputEvent ev)
    {
        if (_world == null || !Native.Get<bool>(_world, "WorldReady") || Game.Connection is not { } net || net.Reconnect.Blocking) return;
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } press)
        {
            NativeWhispers.FocusAt(_world, press.Position);
            return;
        }
        if (ev is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode is not (Key.Enter or Key.KpEnter)) return;
        if (Native.Get<object>(_world, "Chat") is { } chat && Native.Get<bool>(chat, "IsActive")) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return;
        if (NativeWhispers.TryFocusInput(_world)) GetViewport().SetInputAsHandled();
    }
}

/// <summary>Names and marks the native chat colour editor for the Classic skin and places its palette.</summary>
public static class NativeChatColours
{
    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("classic_chat_colours_controls")) return;
        window.SetMeta("classic_chat_colours_controls", 1);
        var rows = window.Body.GetChildren().OfType<HBoxContainer>().ToArray();
        for (int i = 0; i < ChatColors.SlotCount && i < rows.Length; i++)
        {
            var row = rows[i];
            row.Name = "chat_colour_row_" + i;
            NativeWindows.Name(row.GetChildren().OfType<Label>().FirstOrDefault(), "chat_colour_label_" + i);
            var pick = row.GetChildren().OfType<Button>().FirstOrDefault();
            NativeWindows.Name(pick, "chat_colour_pick_" + i);
            NativeWindows.Name(pick?.GetChildren().OfType<ColorRect>().FirstOrDefault(), "chat_colour_swatch_" + i);
        }
        if (rows.Length > ChatColors.SlotCount)
        {
            var foot = rows[ChatColors.SlotCount].GetChildren().OfType<Button>().ToArray();
            if (foot.Length >= 2)
            {
                foot[0].Name = "chat_colour_default";
                foot[1].Name = "chat_colour_apply";
            }
        }
        if (Native.Get<PopupPanel>(world, "ChatHost.palette") is not { } palette) return;
        palette.Name = "chat_colour_palette";
        if (palette.GetChildCount() > 0)
        {
            var picks = palette.GetChild(0).GetChildren().OfType<Button>().ToArray();
            for (int i = 0; i < picks.Length && i < ChatColors.Palette.Length; i++) picks[i].SetMeta("chat_palette_colour", ChatColors.Palette[i]);
        }
        palette.AboutToPopup += () => PlacePalette(window, world, palette);
        window.VisibilityChanged += () => { if (window.Visible) palette.Hide(); };
    }

    /// <summary>
    /// In the Classic editor the palette opens right-aligned under its colour button, above it when it would
    /// leave the frame, and focuses the current colour for keyboard selection.
    /// </summary>
    private static void PlacePalette(HudWindow window, WorldRoot world, PopupPanel palette)
    {
        if (!window.HasMeta("classic_chat_colours")) return;
        int slot = Native.Get<int>(world, "ChatHost.paletteSlot");
        if (Native.Descendants(window).OfType<Button>().FirstOrDefault(b => b.Name == "chat_colour_pick_" + slot) is not { } anchor) return;
        var at = anchor.GetScreenPosition() + new Vector2(0, anchor.Size.Y + 2);
        at.X += anchor.Size.X - palette.Size.X;
        if (at.Y + palette.Size.Y > window.GetScreenPosition().Y + window.Size.Y - 16)
            at.Y = anchor.GetScreenPosition().Y - palette.Size.Y - 2;
        palette.Position = (Vector2I)at;
        var draft = Native.Get<ChatColors>(world, "ChatHost.colorsDraft");
        Callable.From(() =>
        {
            if (!palette.Visible || palette.GetChildCount() == 0) return;
            Button? focus = null;
            foreach (var pick in palette.GetChild(0).GetChildren().OfType<Button>())
            {
                focus ??= pick;
                if (draft != null && pick.HasMeta("chat_palette_colour") && pick.GetMeta("chat_palette_colour").AsColor() == draft[(ChatColorSlot)slot])
                {
                    focus = pick;
                    break;
                }
            }
            focus?.GrabFocus();
        }).CallDeferred();
    }
}
