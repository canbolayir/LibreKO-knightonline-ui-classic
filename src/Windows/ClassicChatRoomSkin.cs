using Godot;
using LibreKO;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Native;
using Spec = KnightOnlineUiClassic.Layout.ClassicChatRoomLayout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The Classic chat room (the client's group-chat style room window of 3277df5). The client keeps its room:
/// posts, voice notes, the voice call, the roster and every callback stay native. The plugin frames it in the
/// plain Classic frame through the client's frame seam and dresses it with the whisper and messenger pieces:
/// black fields with the gold line, Classic list rows, the Messenger action buttons, item cells for the icon
/// actions, Arial on the Classic inks, the original whisper minimize button and, minimized, the compact whisper
/// bar with its attention blink. Since fa3cfc3 the log and the composer are the client's shared thread and composer
/// controls; their rows are restyled in place, and the reaction popups follow <see cref="ClassicReactionStyle"/>.
/// </summary>
public static class ClassicChatRoomSkin
{
    public const string WindowId = "chat_room";
    public const string PostMeta = "classic_room_post", RosterMeta = "classic_room_row", CellMeta = "classic_room_cell", CheckMeta = "classic_room_check",
        PillMeta = "classic_room_pill";

    public static Color TheirBubble => new("141310");
    public static Color MineBubble => ClassicDarkFrame.SelectedRow;
    public static Color MineEdge => ClassicDarkFrame.Line;
    public static Color TheirEdge => ClassicSocialStyle.RowRule;
    public static Color NoticeInk => new("ffe24a");
    public static Color Speaking => new("7fd98b");

    public static void Register(PluginUi ui) => ui.FrameWindow(WindowId, frame => new ClassicChatRoomFrame(frame));

    /// <summary>
    /// The room's popups (room settings, voice settings, a member's volume) on the Classic menu panel; their checks
    /// (Push to talk, Keep the room, Mute for me) carry the original chat check box instead of the client's own.
    /// </summary>
    public static void StylePopup(PopupPanel popup)
    {
        if (ClassicReactionStyle.Owns(popup)) { ClassicReactionStyle.Apply(popup); return; }
        if (popup.HasMeta("classic_popup")) return;
        popup.SetMeta("classic_popup", true);
        popup.AddThemeStyleboxOverride("panel", MenuPanel(10, 8, 10, 8));
        foreach (var node in ClassicSocialStyle.Descendants(popup))
        {
            if (node is HSlider slider) Slider(slider);
            else if (node is ProgressBar meter) Meter(meter);
        }
        ClassicSocialStyle.Apply(popup);
        foreach (var check in ClassicSocialStyle.Descendants(popup).OfType<Button>().Where(b => b is CheckBox or CheckButton)) Check(check);
    }

    /// <summary>The Classic menu panel: the black field with the gold line, at the given content margins.</summary>
    public static StyleBoxFlat MenuPanel(float left, float top, float right, float bottom)
    {
        var panel = ClassicDarkFrame.InputBox();
        panel.BgColor = new Color(0, 0, 0, .96f);
        panel.ContentMarginLeft = left; panel.ContentMarginTop = top;
        panel.ContentMarginRight = right; panel.ContentMarginBottom = bottom;
        return panel;
    }

    /// <summary>
    /// A popup check on the original chat check box of the nation (the client's own check icons are per-control
    /// overrides since fa3cfc3, so they are replaced here); the caption keeps the Classic ink and may wrap.
    /// </summary>
    public static void Check(Button check)
    {
        var art = Plugin.Kit.Layout("{nation}_chat_us").Find("btn_check_normal")!;
        foreach (string state in new[] { "unchecked", "checked", "unchecked_disabled", "checked_disabled" })
            check.AddThemeIconOverride(state, ClassicChatControls.Atlas(art.Images.First(i => i.Tag == (state.StartsWith("unchecked") ? 0 : 1))));
        check.AddThemeConstantOverride("h_separation", Spec.CheckGap);
        check.SetMeta(CheckMeta, true);
    }

    /// <summary>An icon action in the original item cell; the client keeps its icon and the icon's state tint.</summary>
    public static void Cell(Button button)
    {
        button.SetMeta(CellMeta, true);
        button.SetMeta(ClassicSocialStyle.Styled, true);
        var cell = ClassicSkin.PanelStyle();
        cell.SetContentMarginAll(0);
        StyleBoxTexture Tinted(Color tint) { var box = (StyleBoxTexture)cell.Duplicate(); box.ModulateColor = tint; return box; }
        button.AddThemeStyleboxOverride("normal", cell);
        button.AddThemeStyleboxOverride("hover", Tinted(new Color(1.25f, 1.2f, 1.05f)));
        button.AddThemeStyleboxOverride("pressed", Tinted(new Color(.75f, .75f, .75f)));
        button.AddThemeStyleboxOverride("hover_pressed", Tinted(new Color(.75f, .75f, .75f)));
        button.AddThemeStyleboxOverride("disabled", Tinted(new Color(.55f, .55f, .55f)));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeConstantOverride("icon_max_width", 14);
        button.CustomMinimumSize = new Vector2(Spec.Cell, Spec.Cell);
        button.FocusMode = Control.FocusModeEnum.None;
    }

    /// <summary>A volume or threshold slider: the dark-frame track with the gold line and a gold fill.</summary>
    public static void Slider(HSlider slider)
    {
        var track = new StyleBoxFlat { BgColor = new Color("171715"), BorderColor = new Color("bfa777") };
        track.SetBorderWidthAll(1);
        track.ContentMarginTop = track.ContentMarginBottom = 2;
        slider.AddThemeStyleboxOverride("slider", track);
        slider.AddThemeStyleboxOverride("grabber_area", new StyleBoxFlat { BgColor = ClassicDarkFrame.Line, ContentMarginTop = 2, ContentMarginBottom = 2 });
        slider.AddThemeStyleboxOverride("grabber_area_highlight", new StyleBoxFlat { BgColor = ClassicDarkFrame.Heading, ContentMarginTop = 2, ContentMarginBottom = 2 });
        slider.FocusMode = Control.FocusModeEnum.None;
        slider.AddThemeIconOverride("grabber", Knob(ClassicDarkFrame.Heading));
        slider.AddThemeIconOverride("grabber_highlight", Knob(ClassicDarkFrame.Value));
        slider.AddThemeIconOverride("grabber_disabled", Knob(ClassicDesign.Muted));
    }

    private static readonly Dictionary<Color, ImageTexture> _knobs = new();

    internal static void ClearKnobs()
    {
        foreach (var knob in _knobs.Values) knob.Dispose();
        _knobs.Clear();
    }

    /// <summary>A small gold slider knob with the dark outline of the Classic buttons, instead of the client's round one.</summary>
    private static ImageTexture Knob(Color fill)
    {
        if (_knobs.TryGetValue(fill, out var cached) && GodotObject.IsInstanceValid(cached)) return cached;
        using var image = Image.CreateEmpty(8, 14, false, Image.Format.Rgba8);
        image.Fill(new Color("2b2214"));
        image.FillRect(new Rect2I(1, 1, 6, 12), fill);
        image.FillRect(new Rect2I(1, 1, 6, 1), fill.Lightened(.35f));
        return _knobs[fill] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>A level meter: the dark track with the gold line; the fill keeps the client's tint.</summary>
    public static void Meter(ProgressBar meter)
    {
        var track = new StyleBoxFlat { BgColor = new Color("171715"), BorderColor = ClassicDarkFrame.Line };
        track.SetBorderWidthAll(1);
        meter.AddThemeStyleboxOverride("background", track);
        var tint = meter.GetThemeStylebox("fill") is StyleBoxFlat native ? native.BgColor : ClassicDarkFrame.Heading;
        meter.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = tint });
        meter.CustomMinimumSize = new Vector2(meter.CustomMinimumSize.X, 8);
    }

    /// <summary>
    /// One timeline row of the client's thread, restyled in place (the thread keeps its rows to refresh reactions and
    /// voice notes): a post keeps its side (own lines right) and its sender colour on a flat Classic field, its react
    /// button sits in the original item cell and its reaction pill is a Classic chip under the field; day markers and
    /// notices stay centred, notices in the Classic notice yellow.
    /// </summary>
    public static void StylePost(Node post)
    {
        if (post.HasMeta(PostMeta)) return;
        post.SetMeta(PostMeta, true);
        if (post is CenterContainer notice) { StyleNotice(notice); return; }
        if (post is not MarginContainer spaced || spaced.GetChildCount() == 0) return;
        if (spaced.GetChild(0) is CenterContainer day) { StyleDay(day); return; }
        if (spaced.GetChild(0) is not HBoxContainer line) return;
        bool mine = line.Alignment == BoxContainer.AlignmentMode.End;
        foreach (var child in line.GetChildren())
        {
            if (child is CenterContainer slot) { foreach (var react in slot.GetChildren().OfType<Button>()) ReactCell(react); continue; }
            if (child is not VBoxContainer stack) continue;
            foreach (var part in stack.GetChildren())
            {
                if (part is PanelContainer bubble) StyleBubble(bubble, mine);
                else if (part is MarginContainer holder)
                    foreach (var pill in ClassicSocialStyle.Descendants(holder).OfType<PanelContainer>()) Pill(pill);
            }
        }
    }

    private static void StyleBubble(PanelContainer panel, bool mine)
    {
        var bubble = new StyleBoxFlat { BgColor = mine ? MineBubble : TheirBubble, BorderColor = mine ? MineEdge : TheirEdge };
        bubble.SetBorderWidthAll(1);
        bubble.ContentMarginLeft = bubble.ContentMarginRight = 8;
        bubble.ContentMarginTop = 4;
        bubble.ContentMarginBottom = 3;
        panel.AddThemeStyleboxOverride("panel", bubble);
        panel.SetMeta("room_bubble", mine ? "mine" : "theirs");
        foreach (var label in ClassicSocialStyle.Descendants(panel).OfType<Label>())
        {
            // The sender's name is the clickable label (player menu); it keeps the client's colour per sender.
            if (label.MouseFilter == Control.MouseFilterEnum.Stop)
            {
                ClassicSocialStyle.Font(label, true, Spec.FontSize);
                label.Name = "room_post_name";
                continue;
            }
            ClassicSocialStyle.Font(label, false, Spec.SmallSize);
            label.AddThemeColorOverride("font_color", ClassicDesign.Muted);
        }
        foreach (var text in ClassicSocialStyle.Descendants(panel).OfType<RichTextLabel>())
        {
            text.AddThemeFontOverride("normal_font", Plugin.Kit.ChatStrong);
            text.AddThemeFontSizeOverride("normal_font_size", Spec.LineSize);
            text.Name = "room_post_text";
        }
        foreach (var wave in ClassicSocialStyle.Descendants(panel).OfType<VoiceWaveformView>())
        {
            wave.Played = mine ? ClassicDarkFrame.Value : ClassicDarkFrame.Heading;
            wave.Rest = new Color(ClassicDesign.Muted, .45f);
            wave.Dot = ClassicDarkFrame.Heading;
            wave.Name = "room_post_wave";
            wave.QueueRedraw();
        }
        foreach (var play in ClassicSocialStyle.Descendants(panel).OfType<Button>()) Cell(play);
    }

    private static void StyleDay(CenterContainer row)
    {
        foreach (var chip in row.GetChildren().OfType<PanelContainer>())
        {
            var box = new StyleBoxFlat { BgColor = new Color(0, 0, 0, .7f), BorderColor = ClassicDarkFrame.Line };
            box.SetBorderWidthAll(1);
            box.ContentMarginLeft = box.ContentMarginRight = 8;
            box.ContentMarginTop = box.ContentMarginBottom = 2;
            chip.AddThemeStyleboxOverride("panel", box);
            chip.SetMeta("room_chip", "day");
            foreach (var label in chip.GetChildren().OfType<Label>())
            {
                ClassicSocialStyle.Font(label, true, Spec.SmallSize);
                label.AddThemeColorOverride("font_color", ClassicDesign.Muted);
            }
        }
    }

    /// <summary>A notice: borderless, centred, the Classic notice yellow; a notice with web links keeps them clickable.</summary>
    private static void StyleNotice(CenterContainer row)
    {
        foreach (var chip in ClassicSocialStyle.Descendants(row).OfType<PanelContainer>())
        {
            var box = new StyleBoxFlat { BgColor = Colors.Transparent };
            box.ContentMarginLeft = box.ContentMarginRight = 8;
            box.ContentMarginTop = box.ContentMarginBottom = 2;
            chip.AddThemeStyleboxOverride("panel", box);
            chip.SetMeta("room_chip", "notice");
            foreach (var label in chip.GetChildren().OfType<Label>())
            {
                ClassicSocialStyle.Font(label, true, Spec.NoticeSize);
                label.AddThemeColorOverride("font_color", NoticeInk);
            }
            foreach (var linked in chip.GetChildren().OfType<RichTextLabel>())
            {
                linked.AddThemeFontOverride("normal_font", Plugin.Kit.Bold);
                linked.AddThemeFontSizeOverride("normal_font_size", Spec.NoticeSize);
                linked.Text = linked.Text.Replace("[color=#" + UiTheme.TextLo.ToHtml(false) + "]", "[color=#" + NoticeInk.ToHtml(false) + "]");
                // An unwrapped notice is sized for the client's font: it takes the width of the Classic one.
                if (linked.AutowrapMode == TextServer.AutowrapMode.Off)
                    linked.CustomMinimumSize = new Vector2(Mathf.Ceil(Plugin.Kit.Bold.GetStringSize(linked.GetParsedText(), HorizontalAlignment.Left, -1, Spec.NoticeSize).X) + 2, 0);
                linked.Name = "room_notice_link";
            }
        }
    }

    /// <summary>The post's react button (shown on hover and while its reaction bar is open) in the original item cell.</summary>
    public static void ReactCell(Button react)
    {
        Cell(react);
        // The client's react button is flat (no box at all); the Classic one draws its cell.
        react.Flat = false;
        react.AddThemeColorOverride("icon_normal_color", ClassicDarkFrame.Heading);
        react.AddThemeColorOverride("icon_hover_color", Colors.White);
        react.AddThemeColorOverride("icon_pressed_color", Colors.White);
        react.AddThemeConstantOverride("icon_max_width", Spec.ReactIcon);
        react.Name = "room_react";
    }

    /// <summary>
    /// The reaction pill under a post as a Classic chip: the black field with the gold line, the brighter selection
    /// gold while hovered (the client swaps its own hover style in on enter and exit, so the Classic one follows).
    /// </summary>
    public static void Pill(PanelContainer pill)
    {
        if (pill.HasMeta(PillMeta)) return;
        pill.SetMeta(PillMeta, true);
        pill.AddThemeStyleboxOverride("panel", PillBox(false));
        pill.MouseEntered += () => { if (Plugin.Active && GodotObject.IsInstanceValid(pill)) pill.AddThemeStyleboxOverride("panel", PillBox(true)); };
        pill.MouseExited += () => { if (Plugin.Active && GodotObject.IsInstanceValid(pill)) pill.AddThemeStyleboxOverride("panel", PillBox(false)); };
        // The count sits beside the glyph row; the glyphs keep the emoticon and emoji art.
        foreach (var count in ClassicSocialStyle.Descendants(pill).OfType<Label>().Where(l => l.GetParent()?.GetParent() == pill))
        {
            ClassicSocialStyle.Font(count, true, Spec.PillCountSize);
            count.AddThemeColorOverride("font_color", ClassicDesign.Muted);
            count.Name = "room_pill_count";
        }
        pill.Name = "room_pill";
    }

    public static StyleBoxFlat PillBox(bool hover)
    {
        var box = new StyleBoxFlat
        {
            BgColor = hover ? new Color("26251f") : new Color(0, 0, 0, .88f),
            BorderColor = hover ? ClassicSocialStyle.SelectedEdge : ClassicDarkFrame.Line,
        };
        box.SetBorderWidthAll(1);
        box.ContentMarginLeft = Spec.PillPadLeft; box.ContentMarginRight = Spec.PillPadRight;
        box.ContentMarginTop = box.ContentMarginBottom = Spec.PillPadY;
        return box;
    }
}

/// <summary>
/// Plain Classic frame of the chat room: gold title, the room's door and channel-list actions in item cells,
/// the original whisper minimize button and the round close on the title row, the room 16 px from the sides and
/// the bottom. Minimized it is the compact whisper bar (title, restore, close), which blinks for unread lines.
/// </summary>
public partial class ClassicChatRoomFrame : Container
{
    private readonly WindowFrame? _frame;
    private readonly Control _content = null!, _grip = null!;
    private readonly ClassicFrame _art = null!;
    private readonly Label _title = null!, _compactTitle = null!;
    private readonly Button _close = null!;
    private readonly TextureButton _minimize = null!;
    private readonly LayoutView _compact = null!;
    private readonly ClassicWhisperAttention _attention = null!;
    private readonly List<Button> _actions = new();
    private HudWindow? _window;
    private bool _bound, _watching;

    public ClassicChatRoomFrame() { }

    public ClassicChatRoomFrame(WindowFrame frame)
    {
        _frame = frame;
        Name = "classic_chat_room"; MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Nearest;
        _art = new ClassicFrame { Name = "room_frame", BackgroundAlpha = 1, BackgroundColor = Colors.Black };
        AddChild(_art);
        _grip = new Control { Name = "room_drag", MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Move };
        AddChild(_grip);
        frame.SetDragHandle(_grip);
        _title = new Label { Name = "room_title", Text = frame.Title, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        _title.ClipText = true; _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        ClassicSocialStyle.Font(_title, true, Spec.HeadingSize);
        _title.AddThemeColorOverride("font_color", ClassicDesign.FrameTitle);
        AddChild(_title);

        var open = Plugin.Kit.Layout("co_whisper_open_us");
        _minimize = ArtButton(open.Find("btn_hide")!, "room_minimize", "Minimize");
        _minimize.Pressed += () => _window?.SetMinimized(true);
        AddChild(_minimize);
        _close = new Button { Name = "room_close", TooltipText = "Close", FocusMode = FocusModeEnum.None };
        ClassicVendorSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!);
        _close.Text = "";
        _close.Pressed += () => _frame?.Close();
        AddChild(_close);

        _content = frame.Content;
        _content.GetParent()?.RemoveChild(_content);
        foreach (var side in new[] { "left", "top", "right", "bottom" }) _content.AddThemeConstantOverride("margin_" + side, 0);
        AddChild(_content);

        // Minimized: the original compact whisper bar with its own restore and close cells.
        var closed = Plugin.Kit.Layout("co_whisper_close_us");
        _compact = new LayoutView(Plugin.Kit, closed) { Name = "room_compact", Visible = false };
        AddChild(_compact);
        _compactTitle = _compact.Get<Label>("exit_id")!;
        _compactTitle.Name = "room_compact_title";
        _compactTitle.Text = frame.Title;
        _compactTitle.AddThemeFontOverride("font", Plugin.Kit.Bold);
        _compactTitle.AddThemeFontSizeOverride("font_size", UiKit.FontSize(closed.Find("exit_id")!));
        _compactTitle.VerticalAlignment = VerticalAlignment.Center;
        _compactTitle.ClipText = true; _compactTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _compactTitle.Position = new Vector2(6, 2); _compactTitle.Size = new Vector2(94, 20);
        _compactTitle.AddThemeColorOverride("font_outline_color", Colors.Black);
        _compactTitle.AddThemeConstantOverride("outline_size", 1);
        var bar = (TextureButton)_compact.Get("btn_bar")!;
        bar.Size = new Vector2(101, 15);
        AtlasTexture? Crop(Texture2D? texture) => texture is AtlasTexture atlas
            ? new AtlasTexture { Atlas = atlas.Atlas, Region = new Rect2(atlas.Region.Position, new Vector2(101, 15)), FilterClip = true } : null;
        bar.TextureNormal = Crop(bar.TextureNormal); bar.TextureHover = Crop(bar.TextureHover);
        bar.TexturePressed = Crop(bar.TexturePressed); bar.TextureDisabled = Crop(bar.TextureDisabled);
        bar.MouseDefaultCursorShape = CursorShape.Move;
        bar.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } press) return;
            _grip.EmitSignal(Control.SignalName.GuiInput, press);
            bar.AcceptEvent();
        };
        var restore = _compact.Get("btn_open")!;
        restore.TooltipText = "Restore"; restore.Position = new Vector2(104, 3);
        _compact.OnPressed("btn_open", () => _window?.SetMinimized(false));
        var compactClose = ArtButton(open.Find("btn_close")!, "room_compact_close", "Close");
        compactClose.Position = new Vector2(124, 3); compactClose.Size = new Vector2(19, 15);
        compactClose.Pressed += () => _frame?.Close();
        _compact.AddChild(compactClose);
        _attention = new ClassicWhisperAttention(bar, _compactTitle) { Name = "room_attention" };
        AddChild(_attention);

        frame.TitleChanged += title =>
        {
            if (!IsInstanceValid(_title)) return;
            _title.Text = title; _compactTitle.Text = title; _compactTitle.TooltipText = title;
        };
        foreach (var part in GetChildren().OfType<Control>()) part.SizeFlagsHorizontal = part.SizeFlagsVertical = SizeFlags.Fill;
    }

    public Control Content => _content;
    public bool Minimized => _window?.Minimized == true;
    public bool Unread => _attention.HasMeta("unread") && _attention.GetMeta("unread").AsBool();
    public static Vector2 CompactSize => Plugin.Kit.Layout("co_whisper_close_us").SizeVec;
    public IReadOnlyList<Button> HeaderActions => _actions;

    public static ClassicChatRoomFrame? Of(HudWindow window) => window.GetChildren().OfType<ClassicChatRoomFrame>().FirstOrDefault();

    private static TextureButton ArtButton(LayoutNode source, string name, string tooltip)
    {
        Texture2D? State(int tag) => source.Images.FirstOrDefault(i => i.Tag == tag) is { } image ? ClassicChatControls.Atlas(image) : null;
        return new TextureButton
        {
            Name = name, TooltipText = tooltip, FocusMode = FocusModeEnum.None, IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = State(0), TexturePressed = State(1), TextureHover = State(2), TextureDisabled = State(0),
        };
    }

    public override void _Notification(int what)
    {
        if (what == NotificationParented && _window == null && GetParent() is HudWindow window)
        {
            _window = window;
            window.MinimizedChanged += _ => Refresh();
            window.AttentionStyler = unread => { _attention.SetUnread(unread); _attention.SetMeta("unread", unread); };
            Refresh();
        }
        if (what != NotificationSortChildren || _content == null) return;
        Arrange();
    }

    public override Vector2 _GetMinimumSize()
    {
        if (_content == null) return Vector2.Zero;
        if (Minimized) return CompactSize;
        var content = _content.GetCombinedMinimumSize();
        return new Vector2(Mathf.Ceil(content.X + Spec.Inset * 2), Mathf.Ceil(content.Y + Spec.ContentTop + Spec.Inset));
    }

    private void Refresh()
    {
        bool minimized = Minimized;
        foreach (var part in new Control[] { _art, _grip, _title, _minimize, _close }) part.Visible = !minimized;
        _compact.Visible = minimized;
        if (!minimized) { _attention.SetUnread(false); _attention.SetMeta(HotNames.Unread, false); }
        UpdateMinimumSize();
        QueueSort();
        if (_window != null) Callable.From(() => { if (IsInstanceValid(_window)) _window.ResetSize(); }).CallDeferred();
    }

    private void Arrange()
    {
        var size = Size;
        if (Minimized)
        {
            FitChildInRect(_compact, new Rect2(Vector2.Zero, CompactSize));
            // The client hides its channel-list action itself when the room is minimized; the door goes with it.
            foreach (var action in _actions)
            {
                action.SelfModulate = Colors.Transparent; action.MouseFilter = MouseFilterEnum.Ignore;
                FitChildInRect(action, new Rect2(Vector2.Zero, Vector2.Zero));
            }
            return;
        }
        foreach (var action in _actions) { action.SelfModulate = Colors.White; action.MouseFilter = MouseFilterEnum.Stop; }
        FitChildInRect(_art, new Rect2(Vector2.Zero, size));
        Place(_grip, Spec.Drag(size.X));
        Place(_minimize, Spec.Minimize(size.X));
        Place(_close, Spec.Close(size.X));
        int index = 0;
        for (int i = _actions.Count - 1; i >= 0; i--)
            if (_actions[i].Visible) Place(_actions[i], Spec.HeaderAction(size.X, index++));
        Place(_title, Spec.Title(size.X, index));
        Place(_content, new Rect2(Spec.Inset, Spec.ContentTop, size.X - Spec.Inset * 2, size.Y - Spec.ContentTop - Spec.Inset));
    }

    private void Place(Control control, Rect2 rect)
    {
        FitChildInRect(control, rect);
        control.SetMeta("room_expected_rect", rect);
    }

    /// <summary>
    /// Takes the client's header actions (leave the room, channel list) onto the Classic title row and dresses the
    /// room. Called by the preparer once the client has built the room; the dressing runs again whenever the room
    /// opens, for the controls the client rebuilt meanwhile.
    /// </summary>
    public void Bind(HudWindow window)
    {
        if (_bound) return;
        _bound = true;
        _window ??= window;
        if (window.Header is { } header)
            foreach (var action in ClassicSocialStyle.Descendants(header).OfType<Button>()
                         .Where(b => b.Icon != null && b.TooltipText is not ("Close" or "Minimize" or "Restore")).ToArray())
            {
                action.GetParent()?.RemoveChild(action);
                AddChild(action);
                ClassicChatRoomSkin.Cell(action);
                action.AddThemeColorOverride("icon_normal_color", ClassicDarkFrame.Heading);
                action.AddThemeColorOverride("icon_hover_color", Colors.White);
                action.SizeFlagsHorizontal = action.SizeFlagsVertical = SizeFlags.Fill;
                action.VisibilityChanged += QueueSort;
                action.Name = "room_action_" + _actions.Count;
                _actions.Add(action);
            }
        NativeWindows.Sync(window, SyncRoster);
        window.VisibilityChanged += () => { if (window.Visible) Callable.From(Dress).CallDeferred(); };
        Callable.From(Dress).CallDeferred();
        Refresh();
    }

    private T? Find<T>(string name) where T : Node =>
        ClassicSocialStyle.Descendants(_content).OfType<T>().FirstOrDefault(n => n.Name == name);

    /// <summary>Restyles the room's fixed controls (idempotent) and starts dressing posts and roster rows as they arrive.</summary>
    public void Dress()
    {
        if (!Plugin.Active || !IsInstanceValid(this) || _window == null || !IsInstanceValid(_window)) return;
        // The client sizes the body for its 380 px log; the Classic log and the members column set the width.
        _window.Body.CustomMinimumSize = Vector2.Zero;
        if (Find<HBoxContainer>("room_main") is { } main) main.AddThemeConstantOverride("separation", (int)Spec.ColumnGap);
        if (Find<VBoxContainer>("room_members_column") is { } column)
        {
            column.CustomMinimumSize = new Vector2(Spec.ColumnWidth, 0);
            column.AddThemeConstantOverride("separation", (int)Spec.RowGap);
            if (column.GetChildren().OfType<PanelContainer>().FirstOrDefault() is { } field)
            {
                var box = ClassicDarkFrame.InputBox();
                box.SetContentMarginAll(1);
                field.AddThemeStyleboxOverride("panel", box);
                field.Name = "room_roster_field";
            }
            // The Classic bar is wider than the client's thin one: sort again so the rows end before it.
            foreach (var scroll in ClassicSocialStyle.Descendants(column).OfType<ScrollContainer>()) { ClassicSocialStyle.Scroll(scroll); Reserve(scroll); }
        }
        if (Find<VBoxContainer>("room_roster_rows") is { } rows)
        {
            rows.AddThemeConstantOverride("separation", 0);
            if (rows.GetParent() is VBoxContainer list) list.AddThemeConstantOverride("separation", 0);
            foreach (var row in rows.GetChildren()) StyleRosterRow(row);
            if (!_watching) rows.ChildEnteredTree += StyleRosterRow;
        }
        if (Find<Label>("room_roster_empty") is { } empty) { ClassicSocialStyle.Font(empty, false, Spec.NoticeSize); empty.AddThemeColorOverride("font_color", ClassicDesign.Muted); }
        if (Find<VBoxContainer>("room_leader_box") is { } leader)
        {
            leader.AddThemeConstantOverride("separation", (int)Spec.RowGap);
            foreach (var button in leader.GetChildren().OfType<Button>()) ClassicSocialStyle.Button(button);
        }
        if (Find<ScrollContainer>("room_log_scroll") is { } log)
        {
            log.CustomMinimumSize = new Vector2(Spec.LogWidth, Spec.LogHeight);
            var field = ClassicDarkFrame.InputBox();
            field.SetContentMarginAll(1);
            log.AddThemeStyleboxOverride("panel", field);
            ClassicSocialStyle.ScrollBar(log.GetVScrollBar());
            Reserve(log);
            if (log.GetParent() is VBoxContainer stack) stack.AddThemeConstantOverride("separation", (int)Spec.RowGap);
        }
        if (Find<Button>("room_older") is { } older) ClassicSocialStyle.Button(older);
        if (Find<VBoxContainer>("room_log") is { } posts)
        {
            foreach (var post in posts.GetChildren()) ClassicChatRoomSkin.StylePost(post);
            if (!_watching) posts.ChildEnteredTree += ClassicChatRoomSkin.StylePost;
        }
        foreach (var name in new[] { "room_members_toggle", "room_config", "room_deafen", "room_mic", "room_voice_gear", "room_voice_leave", "room_record", "room_send" })
            if (Find<Button>(name) is { } cell) ClassicChatRoomSkin.Cell(cell);
        if (Find<HBoxContainer>("room_voice_row") is { } voice) voice.AddThemeConstantOverride("separation", 6);
        if (Find<Button>("room_join") is { } join) ClassicSocialStyle.Button(join);
        if (Find<HSlider>("room_volume") is { } volume) { ClassicChatRoomSkin.Slider(volume); volume.CustomMinimumSize = new Vector2(Spec.VolumeWidth, 0); }
        if (Find<HBoxContainer>("room_composer_row") is { } composer) composer.AddThemeConstantOverride("separation", 6);
        if (Find<LineEdit>("room_input") is { } input && !input.HasMeta("classic_room_input"))
        {
            input.SetMeta("classic_room_input", true);
            ClassicSocialStyle.Input(input);
            void Roomy()
            {
                foreach (var state in new[] { "normal", "focus", "read_only" })
                    if (input.GetThemeStylebox(state) is StyleBoxFlat box) box.ContentMarginRight = Spec.EmoticonRoom;
            }
            Roomy();
            // The composer widens its field for the emoticon button each time it enters the tree; the Classic field keeps its room.
            input.TreeEntered += () => { if (Plugin.Active && IsInstanceValid(input)) Roomy(); };
            // The client's 20 px emoticon button nearly fills the Classic field; it takes the inline emoticon size.
            foreach (var face in input.GetChildren().OfType<TextureButton>())
            {
                face.Name = "room_emoticons";
                face.CustomMinimumSize = new Vector2(Spec.EmoticonSize, Spec.EmoticonSize);
                face.OffsetLeft = -Spec.EmoticonInset - Spec.EmoticonSize; face.OffsetRight = -Spec.EmoticonInset;
                face.OffsetTop = -Spec.EmoticonSize / 2; face.OffsetBottom = Spec.EmoticonSize / 2;
            }
        }
        if (Find<HBoxContainer>("room_record_strip") is { } strip)
        {
            foreach (var label in strip.GetChildren().OfType<Label>()) ClassicSocialStyle.Font(label, true, Spec.FontSize);
            if (Find<Label>("room_record_clock") is { } clock) clock.AddThemeColorOverride("font_color", ClassicDesign.Text);
            foreach (var cancel in strip.GetChildren().OfType<Button>()) ClassicChatRoomSkin.Cell(cancel);
        }
        if (Find<ProgressBar>("room_record_level") is { } level) ClassicChatRoomSkin.Meter(level);
        _watching = true;
        SyncRoster();
        UpdateMinimumSize();
        _window.ResetSize();
    }

    /// <summary>
    /// A scroll container keeps room for its bar by the bar's own minimum width, not its custom one: the Classic
    /// track gets that width as content margins, so rows and posts end before the bar instead of under it.
    /// </summary>
    private static void Reserve(ScrollContainer scroll)
    {
        var bar = scroll.GetVScrollBar();
        float half = Mathf.Ceil(bar.CustomMinimumSize.X / 2);
        foreach (var state in new[] { "scroll", "scroll_focus" })
            if (bar.GetThemeStylebox(state) is StyleBoxFlat track && track.ContentMarginLeft != half)
            {
                var wide = (StyleBoxFlat)track.Duplicate();
                wide.ContentMarginLeft = wide.ContentMarginRight = half;
                bar.AddThemeStyleboxOverride(state, wide);
            }
        scroll.QueueSort();
    }

    private static void StyleRosterRow(Node node)
    {
        if (node is not PanelContainer panel || panel.HasMeta(ClassicChatRoomSkin.RosterMeta)) return;
        if (panel.GetThemeStylebox("panel") is not StyleBoxFlat native) return;
        var box = ClassicSocialStyle.RowBox(false);
        panel.SetMeta(ClassicChatRoomSkin.RosterMeta, native);
        panel.SetMeta("classic_room_box", box);
        panel.AddThemeStyleboxOverride("panel", box);
        panel.CustomMinimumSize = new Vector2(0, Spec.RosterRow);
        foreach (var label in ClassicSocialStyle.Descendants(panel).OfType<Label>())
        {
            // The dot and the leader star are glyphs the client draws in its own font; only the name turns Arial.
            if (label.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.Expand)) { ClassicSocialStyle.Font(label, true, Spec.FontSize); label.Name = "room_roster_name"; }
            else if (label.Text == "★") label.AddThemeColorOverride("font_color", ClassicDarkFrame.Heading);
        }
        panel.Name = "room_roster_row";
    }

    /// <summary>
    /// The client recolours its own row style for the selection and for speaking; the Classic row mirrors that
    /// state: the gold selection line, the speaking green, otherwise the Messenger row rule.
    /// </summary>
    private void SyncRoster()
    {
        if (!_watching || Find<VBoxContainer>("room_roster_rows") is not { } rows) return;
        foreach (var panel in rows.GetChildren().OfType<PanelContainer>())
        {
            if (!panel.HasMeta(ClassicChatRoomSkin.RosterMeta) || panel.GetMeta(ClassicChatRoomSkin.RosterMeta).AsGodotObject() is not StyleBoxFlat native
                || panel.GetMeta(HotNames.ClassicRoomBox).AsGodotObject() is not StyleBoxFlat box) continue;
            bool selected = native.BgColor == UiTheme.RowHover;
            bool speaking = native.BorderColor == ClassicChatRoomSkin.Speaking;
            box.BgColor = selected ? new Color("26251f") : Colors.Transparent;
            box.BorderColor = speaking ? ClassicChatRoomSkin.Speaking : selected ? ClassicSocialStyle.SelectedEdge : ClassicSocialStyle.RowRule;
            int edge = selected || speaking ? 1 : 0;
            box.BorderWidthLeft = box.BorderWidthTop = box.BorderWidthRight = edge;
            box.BorderWidthBottom = 1;
            panel.SetMeta(HotNames.RoomSelected, selected);
            panel.SetMeta(HotNames.RoomSpeaking, speaking);
        }
    }
}

/// <summary>
/// The reaction popups the client shares between the chat room and the whispers (fa3cfc3), dressed in place on the
/// Classic menu panel: the quick bar (six picks and "more"), the details card (count, a chip per emoji that filters
/// the list, the members who reacted with their sender colour, "Click to remove" on the own row) and the emoticon
/// pickers. The client keeps building, sizing, positioning and wiring them; its geometry stays, so its placement
/// above the post is unchanged. The bar re-marks its current pick and the card rebuilds its chips and rows on every
/// fill, so those are dressed again as the client sets them.
/// </summary>
public static class ClassicReactionStyle
{
    public const string StyledMeta = "classic_reaction_popup", PickMeta = "reaction_pick", TabMeta = "picker_tab";
    private const string ClassicBox = "classic_reaction_box";

    /// <summary>The popups this style dresses. The reaction types exist since fa3cfc3; they are matched by name.</summary>
    public static bool Owns(Window popup) => popup is EmoticonPicker || popup.GetType().Name is "ReactionBar" or "ReactionDetails";

    public static void Apply(PopupPanel popup)
    {
        if (popup.HasMeta(StyledMeta)) return;
        popup.SetMeta(StyledMeta, true);
        switch (popup.GetType().Name)
        {
            case "ReactionBar": Bar(popup); break;
            case "ReactionDetails": Details(popup); break;
            default: Picker(popup); break;
        }
    }

    /// <summary>The quick bar: the picks on the menu panel, the Classic menu hover, the current reaction in the gold selection frame.</summary>
    private static void Bar(PopupPanel bar)
    {
        float edge = Spec.ReactionBarPadding;
        bar.AddThemeStyleboxOverride("panel", Panel(edge, edge, edge, edge));
        foreach (var pick in ClassicSocialStyle.Descendants(bar).OfType<Button>())
        {
            // The client's picks are flat, so neither its hover nor its mark of the current reaction ever shows;
            // the Classic picks draw both.
            pick.Flat = false;
            foreach (var state in new[] { "hover", "pressed", "hover_pressed" }) pick.AddThemeStyleboxOverride(state, Hover());
            pick.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            // "More" is the plus sign with its tooltip; the picks keep the emoji at the client's size.
            if (pick.TooltipText.Length > 0)
            {
                pick.AddThemeFontOverride("font", Plugin.Kit.Bold);
                pick.AddThemeColorOverride("font_color", ClassicDesign.Muted);
                foreach (var state in new[] { "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) pick.AddThemeColorOverride(state, ClassicDarkFrame.Heading);
                pick.Name = "reaction_more";
            }
            else { pick.Name = "reaction_pick"; pick.SetMeta(PickMeta, true); }
        }
        MarkCurrent(bar);
        bar.AboutToPopup += () => { if (Plugin.Active && GodotObject.IsInstanceValid(bar)) MarkCurrent(bar); };
    }

    /// <summary>The client marks the post's current reaction in each pick's normal style before it shows the bar.</summary>
    private static void MarkCurrent(PopupPanel bar)
    {
        foreach (var pick in ClassicSocialStyle.Descendants(bar).OfType<Button>().Where(b => b.HasMeta(PickMeta)))
        {
            var normal = pick.GetThemeStylebox("normal");
            if (normal != null && normal.HasMeta(ClassicBox)) continue;
            bool current = normal is StyleBoxFlat;
            pick.AddThemeStyleboxOverride("normal", current ? Chosen() : Classic(new StyleBoxEmpty()));
            pick.SetMeta("reaction_current", current);
        }
    }

    /// <summary>The details card: heading, the filter chips, the gold rule, the Classic list and its rows.</summary>
    private static void Details(PopupPanel card)
    {
        card.AddThemeStyleboxOverride("panel", Panel(Spec.ReactionCardPadX, Spec.ReactionCardTop, Spec.ReactionCardPadX, Spec.ReactionCardBottom));
        if (card.GetChildCount() == 0 || card.GetChild(0) is not VBoxContainer column) return;
        foreach (var part in column.GetChildren())
        {
            switch (part)
            {
                case Label title:
                    ClassicSocialStyle.Font(title, true, Spec.HeadingSize);
                    title.AddThemeColorOverride("font_color", ClassicDarkFrame.Heading);
                    title.Name = "reaction_title";
                    break;
                case HBoxContainer chips:
                    chips.Name = "reaction_chips";
                    foreach (var chip in chips.GetChildren()) Chip(chip);
                    chips.ChildEnteredTree += node => { if (Plugin.Active) Chip(node); };
                    break;
                case ColorRect rule:
                    rule.Color = ClassicSocialStyle.Rule;
                    rule.Name = "reaction_rule";
                    break;
                case ScrollContainer scroll:
                    ClassicSocialStyle.Scroll(scroll);
                    scroll.Name = "reaction_scroll";
                    if (scroll.GetChildCount() > 0 && scroll.GetChild(0) is VBoxContainer list)
                    {
                        list.Name = "reaction_list";
                        foreach (var row in list.GetChildren()) Row(row);
                        list.ChildEnteredTree += node => { if (Plugin.Active) Row(node); };
                    }
                    break;
            }
        }
    }

    /// <summary>A filter chip: the gold line on black, the chosen one in the gold selection frame; the add button alike.</summary>
    private static void Chip(Node node)
    {
        if (node is not Control control || control.HasMeta(StyledMeta)) return;
        control.SetMeta(StyledMeta, true);
        if (node is Button add)
        {
            add.AddThemeStyleboxOverride("normal", ChipBox(false));
            foreach (var state in new[] { "hover", "pressed", "hover_pressed" }) add.AddThemeStyleboxOverride(state, ChipBox(true));
            add.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            add.AddThemeColorOverride("icon_normal_color", ClassicDesign.Muted);
            foreach (var state in new[] { "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" }) add.AddThemeColorOverride(state, ClassicDarkFrame.Heading);
            add.Name = "reaction_add";
            return;
        }
        if (node is not PanelContainer chip) return;
        bool chosen = chip.GetThemeStylebox("panel") is StyleBoxFlat native && native.BgColor.A > .1f;
        chip.AddThemeStyleboxOverride("panel", ChipBox(chosen));
        chip.SetMeta("reaction_chosen", chosen);
        // The count is the line's last label; a Unicode emoji glyph (also a label) keeps the client's chip size and ink.
        if (chip.GetChildCount() > 0 && chip.GetChild(0) is HBoxContainer line && line.GetChildCount() > 1
            && line.GetChild(line.GetChildCount() - 1) is Label number)
        {
            ClassicSocialStyle.Font(number, true, Spec.FontSize);
            number.AddThemeColorOverride("font_color", chosen ? ClassicDarkFrame.Heading : ClassicDesign.Text);
            number.Name = "reaction_chip_count";
        }
        chip.Name = "reaction_chip";
    }

    /// <summary>
    /// A member row: the Messenger row rule, the member's initial in the original item cell in the sender colour,
    /// the name in Arial Bold; the own row takes the Classic list hover and says "Click to remove" in the muted ink.
    /// </summary>
    private static void Row(Node node)
    {
        if (node is not PanelContainer row || row.HasMeta(StyledMeta)) return;
        row.SetMeta(StyledMeta, true);
        bool mine = row.MouseFilter == Control.MouseFilterEnum.Stop;
        row.AddThemeStyleboxOverride("panel", RowBox(false));
        row.SetMeta("reaction_row", mine ? "mine" : "other");
        if (mine)
        {
            row.MouseEntered += () => { if (Plugin.Active && GodotObject.IsInstanceValid(row)) row.AddThemeStyleboxOverride("panel", RowBox(true)); };
            row.MouseExited += () => { if (Plugin.Active && GodotObject.IsInstanceValid(row)) row.AddThemeStyleboxOverride("panel", RowBox(false)); };
        }
        if (row.GetChildCount() == 0 || row.GetChild(0) is not HBoxContainer line) return;
        foreach (var part in line.GetChildren())
        {
            if (part is PanelContainer avatar)
            {
                var cell = ClassicSkin.PanelStyle();
                cell.SetContentMarginAll(0);
                avatar.AddThemeStyleboxOverride("panel", cell);
                avatar.Name = "reaction_avatar";
                foreach (var letter in avatar.GetChildren().OfType<Label>())
                {
                    var ink = letter.GetThemeColor("font_color");
                    ClassicSocialStyle.Font(letter, true, Spec.HeadingSize);
                    letter.AddThemeColorOverride("font_color", ink);
                    letter.AddThemeColorOverride("font_outline_color", Colors.Black);
                    letter.AddThemeConstantOverride("outline_size", 2);
                }
            }
            else if (part is VBoxContainer names)
            {
                var labels = names.GetChildren().OfType<Label>().ToArray();
                for (int i = 0; i < labels.Length; i++)
                {
                    ClassicSocialStyle.Font(labels[i], i == 0, i == 0 ? Spec.HeadingSize : Spec.SmallSize);
                    labels[i].AddThemeColorOverride("font_color", i == 0 ? ClassicDesign.Text : ClassicDesign.Muted);
                    labels[i].Name = i == 0 ? "reaction_name" : "reaction_hint";
                }
            }
        }
    }

    /// <summary>
    /// An emoticon picker (the composer's and the reaction "more" one): the menu panel, the Classic tabs, the
    /// emoji on the Classic menu hover; the emoticons keep their art.
    /// </summary>
    private static void Picker(PopupPanel picker)
    {
        picker.SetMeta("classic_popup", true);
        float edge = Spec.PickerPadding;
        picker.AddThemeStyleboxOverride("panel", Panel(edge, edge, edge, edge));
        foreach (var button in ClassicSocialStyle.Descendants(picker).OfType<Button>())
        {
            if (button.ToggleMode) { ClassicSocialStyle.Button(button); button.SetMeta(TabMeta, true); continue; }
            button.Flat = false;
            button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            foreach (var state in new[] { "hover", "pressed", "hover_pressed" }) button.AddThemeStyleboxOverride(state, Hover());
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        }
    }

    /// <summary>The Classic menu panel, opaque: these popups open over the posts.</summary>
    private static StyleBoxFlat Panel(float left, float top, float right, float bottom)
    {
        var panel = ClassicChatRoomSkin.MenuPanel(left, top, right, bottom);
        panel.BgColor = Colors.Black;
        return panel;
    }

    private static T Classic<T>(T box) where T : StyleBox { box.SetMeta(ClassicBox, true); return box; }

    /// <summary>The Classic menu hover (the drop-down's selected row).</summary>
    private static StyleBoxFlat Hover() => Classic(new StyleBoxFlat { BgColor = ClassicDarkFrame.SelectedRow });

    /// <summary>The gold selection frame of the Classic lists.</summary>
    private static StyleBoxFlat Chosen()
    {
        var box = Classic(new StyleBoxFlat { BgColor = new Color("26251f"), BorderColor = ClassicSocialStyle.SelectedEdge });
        box.SetBorderWidthAll(1);
        return box;
    }

    private static StyleBoxFlat ChipBox(bool chosen)
    {
        var box = Classic(new StyleBoxFlat
        {
            BgColor = chosen ? new Color("26251f") : new Color(0, 0, 0, .7f),
            BorderColor = chosen ? ClassicSocialStyle.SelectedEdge : ClassicDarkFrame.Line,
        });
        box.SetBorderWidthAll(1);
        box.ContentMarginLeft = box.ContentMarginRight = Spec.ReactionChipPadX;
        box.ContentMarginTop = box.ContentMarginBottom = Spec.ReactionChipPadY;
        return box;
    }

    private static StyleBoxFlat RowBox(bool hover)
    {
        var box = ClassicSocialStyle.RowBox(false);
        if (hover) box.BgColor = ClassicDarkFrame.SelectedRow;
        box.ContentMarginTop = box.ContentMarginBottom = 0;
        return Classic(box);
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName ClassicRoomBox = "classic_room_box";
    internal static readonly StringName RoomSelected = "room_selected";
    internal static readonly StringName RoomSpeaking = "room_speaking";
    internal static readonly StringName Unread = "unread";
}
