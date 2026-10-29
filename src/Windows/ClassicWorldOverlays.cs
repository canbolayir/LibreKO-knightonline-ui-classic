using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO;
using Spec = KnightOnlineUiClassic.Layout.ClassicOverlayLayout;
using NativeApi = KnightOnlineUiClassic.Native.Native;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// Classic skins of the native world overlays. Nothing here has its own window art in 1.298 except
/// the loot grid, so every overlay is composed from original pieces: prompts from the common message
/// box (the same art and type as <see cref="MessageBox"/>), one-line plates and chips from the nation
/// information plaque and its chat-tab buttons, countdown bars from the original progress art, and
/// broadcast banners as outlined Arial Bold text like the original centre notices. The client's own
/// controls are restyled or moved in place; their callbacks, timers and state stay native.
/// </summary>
public static class ClassicWorldOverlays
{
    private const string Skinned = "classic_overlay";
    public const string ExpectedRect = "overlay_expected_rect";

    public static void Register()
    {
        NativeOverlays.Extend(NativeOverlay.Registration, node => Once(node, () => Registration((Control)node)));
        NativeOverlays.Extend(NativeOverlay.Countdown, node => Once(node, () => Countdown((PanelContainer)node)));
        NativeOverlays.Extend(NativeOverlay.Leave, node => Once(node, () => Leave((PanelContainer)node)));
        NativeOverlays.Extend(NativeOverlay.Death, node => Once(node, () => Death((PanelContainer)node)));
        NativeOverlays.Extend(NativeOverlay.Prompt, node => Once(node, () => Prompt((AcceptDialog)node)));
        NativeOverlays.Extend(NativeOverlay.Announcement, node => Once(node, () => Announcement((Control)node)));
        NativeOverlays.Extend(NativeOverlay.Chip, node => Once(node, () => Chip((PanelContainer)node)));
        NativeOverlays.Extend(NativeOverlay.Score, node => Once(node, () => Score((PanelContainer)node)));
        NativeOverlays.Extend(NativeOverlay.Loot, node => Once(node, () => Loot((PanelContainer)node)));
        NativeOverlays.Extend(NativeOverlay.Board, node => Once(node, () => Board((PanelContainer)node)));
        NativeOverlays.Extend(NativeOverlay.GenieQuick, node => Once(node, () => { Board((PanelContainer)node); ClassicGenieQuick.Apply((PanelContainer)node); }));
        NativeOverlays.Extend(NativeOverlay.Toast, node => Once(node, () => Toast((PanelContainer)node)));
        NativeOverlays.Extend(NativeOverlay.Titles, node => Once(node, () => Titles((Container)node)));
        NativeOverlays.Extend(NativeOverlay.LabelPlate, node => Once(node, () => LabelPlate((Label)node)));
        NativeOverlays.Extend(NativeOverlay.Action, node => Once(node, () => ClassicEventFamily.Action((Button)node)));
        NativeOverlays.Extend(NativeOverlay.TowerButton, node => Once(node, () => TowerButton((Button)node)));
        NativeOverlays.Extend(NativeOverlay.Paper, node => Once(node, () => Paper((Control)node)));
        NativeOverlays.Extend(NativeOverlay.Wanted, node => Once(node, () => Wanted((Button)node)));
        NativeOverlays.Extend(NativeOverlay.MapPlate, node => Once(node, () => MapPlate((Label)node)));
    }

    private static void Once(Node node, Action skin)
    {
        if (node.HasMeta(Skinned)) return;
        node.SetMeta(Skinned, true);
        skin();
    }

    // ---- Palette (original art strings) -----------------------------------------------------------

    /// <summary>Message box title and message ink (<c>Text_title</c>, <c>Text_Message</c>).</summary>
    public static Color MessageTitle => new("ffff00");
    public static Color MessageText => Colors.White;
    /// <summary>Information plaque caption: El Morad light on dark slate, Karus black on light stone.</summary>
    public static Color PlaqueInk => ClassicDesign.Karus ? Colors.Black : new Color("ffff80");
    public static Color PlaqueShadow => ClassicDesign.Karus ? new Color("c7c5be") : Colors.Black;
    /// <summary>Chat tab caption ink.</summary>
    public static Color TabInk => ClassicDesign.Karus ? Colors.Black : new Color("f8ead1");
    /// <summary>Urgent countdown ink on the dark message box and on each nation plaque.</summary>
    public static Color UrgentOnMessage => new("ff6a5a");
    public static Color UrgentOnPlaque => ClassicDesign.Karus ? new Color("7a160c") : new Color("ff6a5a");

    // ---- Shared pieces ------------------------------------------------------------------------------

    private static T? Find<T>(Node root, string name) where T : Node => root.FindChild(name, true, false) as T;

    internal static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren())
            foreach (var node in Descendants(child)) yield return node;
    }

    internal static void Text(Label label, int size, Color ink, bool bold = true, Color? shadow = null)
    {
        label.AddThemeFontOverride("font", bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeConstantOverride("outline_size", 0);
        label.AddThemeColorOverride("font_color", ink);
        label.AddThemeColorOverride("font_shadow_color", shadow ?? new Color(0, 0, 0, .75f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeConstantOverride("shadow_outline_size", 0);
    }

    /// <summary>Sets a label's ink only when it changed, so per-frame syncs cause no theme updates.</summary>
    private static void Ink(Label label, Color ink)
    {
        if (label.GetThemeColor("font_color") != ink) label.AddThemeColorOverride("font_color", ink);
    }

    private static void Place(Control control, Control parent, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, parent, rect);
        control.SetMeta(ExpectedRect, rect);
    }

    /// <summary>The message box buttons: original art per state, the native text on a shadowed caption.</summary>
    internal static void MessageButton(Button button, bool cancel = false)
    {
        var art = Plugin.Kit.Layout("{nation}_messagebox_us").Find(cancel ? "btn_cancel" : "btn_ok")!;
        ArtButton(button, art, 12, false, Colors.White, new Color(0, 0, 0, .75f), new Color("8a8a8a"));
    }

    /// <summary>The chat tab art used as an action on a plaque.</summary>
    internal static void TabButton(Button button)
    {
        var art = Plugin.Kit.Layout("{nation}_chat_us").Find("btn_normal")!;
        ArtButton(button, art, 12, false, TabInk, ClassicDesign.Karus ? Colors.Transparent : new Color(0, 0, 0, .75f),
            ClassicDesign.Karus ? new Color("57544f") : new Color("8a8a8a"));
    }

    private static void ArtButton(Button button, LayoutNode art, int size, bool bold, Color ink, Color shadow, Color disabled)
    {
        ClassicChatControls.SkinButton(button, art);
        var normal = (StyleBoxTexture)button.GetThemeStylebox("normal").Duplicate();
        normal.ModulateColor = new Color(.6f, .6f, .6f);
        button.AddThemeStyleboxOverride("disabled", normal);
        button.FocusMode = Control.FocusModeEnum.None;
        button.Icon = null;
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "font_disabled_color" })
            button.AddThemeColorOverride(state, Colors.Transparent);
        button.AddThemeConstantOverride("outline_size", 0);
        if (button.GetNodeOrNull<ClassicButtonCaption>("caption") == null)
            button.AddChild(new ClassicButtonCaption(button, size, bold, ink, shadow, disabled) { Name = "caption" });
    }

    private static StyleBoxTexture Bar(Rect2 source)
    {
        var box = new StyleBoxTexture { Texture = Plugin.Kit.Texture("ui_message_us.png"), RegionRect = source };
        box.SetTextureMargin(Side.Left, 2); box.SetTextureMargin(Side.Right, 2);
        box.SetContentMarginAll(0);
        return box;
    }

    /// <summary>Original progress art: the groove and the orange (calm) or red (urgent) fill.</summary>
    internal static void ProgressBar(ProgressBar bar, bool urgent)
    {
        // The whole groove, with its light rims, so an almost empty bar still shows its track.
        bar.AddThemeStyleboxOverride("background", Bar(new Rect2(321, 445, 179, 16)));
        bar.AddThemeStyleboxOverride("fill", Bar(urgent ? new Rect2(323, 462, 175, 12) : new Rect2(323, 389, 175, 12)));
        bar.SetMeta(NativeOverlays.Urgent, urgent);
    }

    private static void SyncBar(ProgressBar bar, bool urgent)
    {
        if (bar.GetMeta(NativeOverlays.Urgent, false).AsBool() != urgent) ProgressBar(bar, urgent);
    }

    /// <summary>Runs <paramref name="sync"/> every frame, before drawing, while <paramref name="owner"/> is visible.</summary>
    private static void Keep(Control owner, Action sync)
    {
        owner.AddChild(new NativeEventWatcher(() => { if (owner.IsVisibleInTree()) sync(); }) { Name = "classic_overlay_sync", ProcessPriority = int.MaxValue - 1 });
        sync();
    }

    /// <summary>Replaces a native plate's flat panel with the information plaque.</summary>
    private static void Plaque(PanelContainer panel, float left, float top, float right, float bottom)
    {
        var box = new StyleBoxEmpty { ContentMarginLeft = left, ContentMarginTop = top, ContentMarginRight = right, ContentMarginBottom = bottom };
        panel.AddThemeStyleboxOverride("panel", box);
        panel.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        panel.Draw += () => ClassicPlaqueArt.Draw(panel, new Rect2(Vector2.Zero, panel.Size));
        panel.QueueRedraw();
    }

    // ---- Message box overlays -----------------------------------------------------------------------

    private static void Registration(Control host)
    {
        if (host.GetChildren().OfType<CenterContainer>().FirstOrDefault() is not { } native) return;
        if (Find<Label>(host, "registration_title") is not { } title || Find<Label>(host, "registration_timer") is not { } timer
            || Find<ProgressBar>(host, "registration_bar") is not { } bar || Find<Label>(host, "registration_status") is not { } status
            || Find<Button>(host, "registration_join") is not { } join || Find<Button>(host, "registration_close") is not { } close) return;
        native.Visible = false;
        var panel = new ClassicMessagePanel(Spec.Registration.Size, centre: true) { Name = "classic_registration" };
        host.AddChild(panel);
        Place(title, panel, Spec.Registration.Title); Text(title, 13, MessageTitle);
        title.HorizontalAlignment = HorizontalAlignment.Center; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Place(timer, panel, Spec.Registration.Timer); Text(timer, 15, MessageText);
        timer.HorizontalAlignment = HorizontalAlignment.Center;
        Place(bar, panel, Spec.Registration.Bar); ProgressBar(bar, false);
        Place(status, panel, Spec.Registration.Status); Text(status, 13, ClassicDesign.Text);
        status.ClipText = false; status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.HorizontalAlignment = HorizontalAlignment.Center; status.VerticalAlignment = VerticalAlignment.Center;
        Place(join, panel, Spec.Registration.Join); MessageButton(join);
        Place(close, panel, Spec.Registration.Close); MessageButton(close, cancel: true);
        var count = Find<Label>(host, "registration_count");
        if (count != null)
        {
            Place(count, panel, Spec.Registration.Count); Text(count, 12, ClassicDesign.Muted);
            count.HorizontalAlignment = HorizontalAlignment.Center;
        }
        (bool Count, bool Alone)? shape = null;
        Keep(panel, () =>
        {
            // The box grows by the sign-up count line; Close takes the single place when the event has no Join.
            var now = (Count: count is { Visible: true }, Alone: !join.Visible);
            if (shape != now)
            {
                shape = now;
                var size = Spec.Registration.SizeFor(now.Count);
                panel.Size = panel.CustomMinimumSize = size; panel.GetNode<Control>("message_art").Size = size;
                foreach (var (control, rect) in new (Control, Rect2)[] { (join, Spec.Registration.JoinAt(now.Count)), (close, Spec.Registration.CloseAt(now.Count, now.Alone)) })
                { control.Position = rect.Position; control.Size = rect.Size; control.SetMeta(ExpectedRect, rect); }
            }
            bool urgent = host.GetMeta(NativeOverlays.Urgent, false).AsBool();
            Ink(timer, urgent ? UrgentOnMessage : MessageText);
            SyncBar(bar, urgent);
            // The client upper-cases the event name with the system culture, which turns i into a dotted capital on a
            // Turkish system; the title keeps the English capitals.
            if (title.Text.Contains('\u0130')) title.Text = title.Text.Replace('\u0130', 'I');
            // Registered here: the ready ink; registered for another open sign-up (Join hidden): the muted hint.
            Ink(status, host.GetMeta(NativeOverlays.Registered, false).AsBool() ? ClassicEventPanel.Accepted
                : host.GetMeta(NativeOverlays.RegisteredElsewhere, false).AsBool() ? ClassicDesign.Muted : ClassicDesign.Text);
        });
    }

    private static void Death(PanelContainer native)
    {
        if (native.GetChildren().OfType<MarginContainer>().FirstOrDefault() is not { } content) return;
        var labels = Descendants(content).OfType<Label>().ToArray();
        // The resurrection choices are flat rows in their own column; OK is the last button.
        var rows = Descendants(content).OfType<VBoxContainer>().FirstOrDefault(v => v.GetChildren().OfType<Button>().Any() && !v.GetChildren().OfType<Label>().Any());
        if (labels.Length < 2 || Descendants(content).OfType<Button>().LastOrDefault(b => rows == null || !rows.IsAncestorOf(b)) is not { } ok) return;
        native.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var panel = new ClassicMessagePanel(Spec.Death.Size(0), centre: false) { Name = "classic_death" };
        native.AddChild(panel);
        content.Visible = false;
        var (loss, hint) = (labels[0], labels[1]);
        Place(loss, panel, Spec.Death.Loss); Text(loss, 13, MessageTitle);
        loss.AutowrapMode = TextServer.AutowrapMode.Off; loss.HorizontalAlignment = HorizontalAlignment.Center; loss.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Place(hint, panel, Spec.Death.Hint(0)); Text(hint, 15, MessageText);
        hint.ClipText = false; hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Center;
        Place(ok, panel, Spec.Death.Ok(0)); MessageButton(ok);
        if (rows != null)
        {
            Place(rows, panel, Spec.Death.Rows(0, 0));
            rows.Name = "death_choices";
            rows.AddThemeConstantOverride("separation", (int)Spec.Death.RowGap);
            var hover = new StyleBoxFlat { BgColor = new Color(1, 1, 1, .08f) };
            foreach (var row in rows.GetChildren().OfType<Button>())
            {
                // The client keeps its yellow (chosen) and white ink; the row is plain message-box text.
                foreach (var state in new[] { "normal", "pressed", "focus", "disabled" }) row.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
                row.AddThemeStyleboxOverride("hover", hover); row.AddThemeStyleboxOverride("hover_pressed", hover);
                row.AddThemeFontOverride("font", Plugin.Kit.Regular); row.AddThemeFontSizeOverride("font_size", 13);
                row.AddThemeConstantOverride("outline_size", 0);
                row.CustomMinimumSize = new Vector2(Spec.Message.Text.Size.X, Spec.Death.RowHeight);
                row.ClipText = true; row.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            }
        }
        // The box fits the hint's wrapped lines (the pending hint is shorter) and the choices, and stays centred.
        (float Hint, int Rows) fitted = (-1, -1);
        void Fit()
        {
            float height = Wrapped(hint, Spec.Message.Text.Size.X);
            int shown = rows is { Visible: true } ? rows.GetChildren().OfType<Button>().Count(b => b.Visible) : 0;
            if (rows != null) foreach (var row in rows.GetChildren().OfType<Button>()) if (row.TooltipText != row.Text) row.TooltipText = row.Text;
            if ((height, shown) == fitted && native.Size == panel.Size) return;
            fitted = (height, shown);
            var size = Spec.Death.Size(height, shown);
            panel.Size = panel.CustomMinimumSize = size; panel.GetNode<Control>("message_art").Size = size;
            native.CustomMinimumSize = size;
            var places = new List<(Control, Rect2)> { (hint, Spec.Death.Hint(height)), (ok, Spec.Death.Ok(height, shown)) };
            if (rows != null) places.Add((rows, Spec.Death.Rows(height, shown)));
            foreach (var (control, rect) in places)
            { control.Position = rect.Position; control.Size = rect.Size; control.SetMeta(ExpectedRect, rect); }
            // The native panel centres on its anchors; the box sits on whole pixels.
            native.OffsetLeft = -Mathf.Ceil(size.X / 2); native.OffsetRight = native.OffsetLeft + size.X;
            native.OffsetTop = -Mathf.Ceil(size.Y / 2); native.OffsetBottom = native.OffsetTop + size.Y;
        }
        Fit();
        Keep(native, () => { Fit(); var at = native.Position.Round(); if (at != native.Position) native.Position = at; });
    }

    /// <summary>The height a word-wrapped label needs at <paramref name="width"/>.</summary>
    internal static float Wrapped(Label label, float width)
    {
        var font = label.GetThemeFont("font"); int size = label.GetThemeFontSize("font_size");
        // The label's own word-smart breaking: mandatory breaks, word bounds, and adaptive splits of long words.
        const TextServer.LineBreakFlag smart = TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive;
        float line = font.GetHeight(size);
        int lines = Math.Max(1, Mathf.RoundToInt(font.GetMultilineStringSize(label.Text, HorizontalAlignment.Left, width, size, -1, smart).Y / line));
        return Mathf.Ceil(lines * line + (lines - 1) * label.GetThemeConstant("line_spacing"));
    }

    private static void Prompt(AcceptDialog dialog)
    {
        dialog.Borderless = true; dialog.Transparent = true; dialog.TransparentBg = true;
        var box = new StyleBoxEmpty();
        box.SetContentMargin(Side.Top, Spec.Message.TextTop);
        box.SetContentMargin(Side.Left, Spec.Message.Text.Position.X);
        box.SetContentMargin(Side.Right, Spec.Message.Width - Spec.Message.Text.End.X);
        box.SetContentMargin(Side.Bottom, Spec.Message.ButtonBottom);
        dialog.AddThemeStyleboxOverride("panel", box);
        dialog.AddThemeConstantOverride("buttons_min_width", (int)Spec.Message.Button.X);
        dialog.AddThemeConstantOverride("buttons_min_height", (int)Spec.Message.Button.Y);
        // The dialog's button separation is the vertical gap under the message, so the box fits its wrapped lines.
        dialog.AddThemeConstantOverride("buttons_separation", (int)Spec.Message.ActionGap);
        dialog.DialogAutowrap = true;
        dialog.MinSize = new Vector2I((int)Spec.Message.Width, 1);
        dialog.MaxSize = new Vector2I((int)Spec.Message.Width, 4096);
        dialog.Size = dialog.MinSize;
        var label = dialog.GetLabel();
        Text(label, 15, MessageText);
        label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
        MessageButton(dialog.GetOkButton());
        // The dialog focuses its confirm button on popup, which keeps Enter and Tab working as before.
        dialog.GetOkButton().FocusMode = Control.FocusModeEnum.All;
        if (dialog is ConfirmationDialog confirmation)
        {
            var cancel = confirmation.GetCancelButton();
            MessageButton(cancel, cancel: true);
            cancel.FocusMode = Control.FocusModeEnum.All;
            // The original two buttons sit 19 pixels apart, centred. The dialog keeps a spacer between them (and
            // re-shows it with the buttons), so it becomes a fixed 1-pixel gap between two 9-pixel separations.
            if (cancel.GetParent() is HBoxContainer row)
            {
                int gap = (int)Spec.Message.ButtonGap;
                row.AddThemeConstantOverride("separation", gap / 2);
                var items = row.GetChildren(true).OfType<Control>().ToList();
                int from = Math.Min(items.IndexOf(cancel), items.IndexOf(dialog.GetOkButton())), to = Math.Max(items.IndexOf(cancel), items.IndexOf(dialog.GetOkButton()));
                for (int i = from + 1; i < to; i++)
                    if (items[i] is not Button) { items[i].SizeFlagsHorizontal = Control.SizeFlags.Fill; items[i].CustomMinimumSize = new Vector2(gap - gap / 2 * 2, 0); }
            }
        }
        // Decoration stays outside AcceptDialog's direct child layout.
        var decoration = new Node { Name = "classic_prompt" };
        dialog.AddChild(decoration);
        var art = new ClassicMessageArt { Name = "message_art", ZIndex = -1 };
        decoration.AddChild(art);
        var title = new Label { Name = "prompt_title", Position = Spec.Message.Title.Position, Size = Spec.Message.Title.Size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipText = true,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        Text(title, 13, MessageTitle);
        title.SetMeta(ExpectedRect, Spec.Message.Title);
        decoration.AddChild(title);
        art.AddChild(new NativeEventWatcher(() =>
        {
            if (!dialog.Visible) return;
            // The dialog grows to its contents but keeps a stale larger size after the native text changes;
            // fit it to the wrapped message and keep it centred where the native popup placed it.
            int fitted = (int)Spec.Message.HeightFor(Wrapped(label, Spec.Message.Text.Size.X));
            if (dialog.Size.Y != fitted)
            {
                int before = dialog.Size.Y;
                dialog.Size = new Vector2I(dialog.Size.X, fitted);
                dialog.Position += new Vector2I(0, (before - dialog.Size.Y) / 2);
            }
            art.Position = Vector2.Zero;
            art.Size = new Vector2(Spec.Message.Width, dialog.Size.Y);
            title.Text = dialog.Title;
        }) { Name = "classic_prompt_sync" });
    }

    // ---- Plates, chips and strips -------------------------------------------------------------------

    private static void Countdown(PanelContainer plate)
    {
        if (plate.GetChildren().OfType<MarginContainer>().FirstOrDefault() is not { } content
            || Find<Label>(plate, "countdown_title") is not { } title || Find<Label>(plate, "countdown_timer") is not { } timer
            || Find<ProgressBar>(plate, "countdown_bar") is not { } bar) return;
        Plaque(plate, 0, 0, 0, 0);
        plate.CustomMinimumSize = Spec.Countdown.Size;
        var surface = new Control { Name = "classic_countdown", MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddChild(surface);
        content.Visible = false;
        Place(title, surface, Spec.Countdown.Title); Text(title, 13, PlaqueInk, shadow: PlaqueShadow);
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; title.MouseFilter = Control.MouseFilterEnum.Ignore;
        Place(timer, surface, Spec.Countdown.Timer); Text(timer, 13, PlaqueInk, shadow: PlaqueShadow);
        timer.HorizontalAlignment = HorizontalAlignment.Right; timer.MouseFilter = Control.MouseFilterEnum.Ignore;
        Place(bar, surface, Spec.Countdown.Bar); ProgressBar(bar, false); bar.MouseFilter = Control.MouseFilterEnum.Ignore;
        plate.ResetSize();
        Keep(plate, () =>
        {
            bool urgent = plate.GetMeta(NativeOverlays.Urgent, false).AsBool();
            Ink(timer, urgent ? UrgentOnPlaque : PlaqueInk);
            SyncBar(bar, urgent);
        });
    }

    private static void Leave(PanelContainer plate)
    {
        if (plate.GetChildren().OfType<MarginContainer>().FirstOrDefault() is not { } content
            || Find<Label>(plate, "leave_title") is not { } title || Find<Button>(plate, "leave_action") is not { } action) return;
        Plaque(plate, 0, 0, 0, 0);
        plate.CustomMinimumSize = Spec.Leave.Size;
        var surface = new Control { Name = "classic_leave", MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddChild(surface);
        // The grip glyph goes with the native row; the whole plaque stays the drag handle (move cursor, tooltip).
        content.Visible = false;
        Place(title, surface, Spec.Leave.Title); Text(title, 13, PlaqueInk, shadow: PlaqueShadow);
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; title.MouseFilter = Control.MouseFilterEnum.Ignore;
        Place(action, surface, Spec.Leave.Action); TabButton(action);
        plate.ResetSize();
    }

    // ---- Event boards, toasts, titles and actions ---------------------------------------------------------

    /// <summary>An event box: the plain Classic frame of the event windows around the client's own rows.</summary>
    private static void Board(PanelContainer panel)
    {
        // The frame fills the box; the client's column moves into an even padding inside it.
        panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var content = panel.GetChildren().OfType<Control>().ToArray();
        var frame = new ClassicFrame { Name = "classic_frame", BackgroundColor = Colors.Black, BackgroundAlpha = Spec.Board.Alpha };
        panel.AddChild(frame);
        panel.MoveChild(frame, 0);
        var padding = new MarginContainer { Name = "classic_padding", MouseFilter = Control.MouseFilterEnum.Pass };
        padding.AddThemeConstantOverride("margin_left", (int)Spec.Board.Padding); padding.AddThemeConstantOverride("margin_right", (int)Spec.Board.Padding);
        padding.AddThemeConstantOverride("margin_top", (int)Spec.Board.PaddingTop); padding.AddThemeConstantOverride("margin_bottom", (int)Spec.Board.PaddingTop);
        panel.AddChild(padding);
        foreach (var child in content) child.Reparent(padding, false);
        ClassicEventFamily.Restyle(panel);
        NativeWindows.OnDescendantAdded(panel, node => Callable.From(() => { if (GodotObject.IsInstanceValid(node)) ClassicEventFamily.Restyle(node); }).CallDeferred());
    }

    /// <summary>A centre toast: the client's lines as outlined Arial Bold centre notices, without the modern plate and rules.</summary>
    private static void Toast(PanelContainer panel)
    {
        var old = panel.GetThemeStylebox("panel");
        var box = new StyleBoxEmpty();
        box.ContentMarginLeft = old.ContentMarginLeft; box.ContentMarginRight = old.ContentMarginRight;
        box.ContentMarginTop = old.ContentMarginTop; box.ContentMarginBottom = old.ContentMarginBottom;
        panel.AddThemeStyleboxOverride("panel", box);
        foreach (var rule in Descendants(panel).OfType<TextureRect>()) rule.Visible = false;
        Announcement(panel);
    }

    /// <summary>The event title chips: each an information plaque with the plaque ink; rebuilt chips follow.</summary>
    private static void Titles(Container column)
    {
        // With the local notice art the chips become the original notice bars (see ClassicNoticeBars).
        if (ClassicNoticeBars.Apply(column)) return;
        void Chip(Node node)
        {
            if (node is not PanelContainer chip || chip.HasMeta(Skinned)) return;
            chip.SetMeta(Skinned, true);
            // The expand glyph comes from a fallback font a pixel taller than Arial; the padding keeps the 22-pixel plaque.
            Plaque(chip, Spec.Plaque.Inset, 2, Spec.Plaque.Inset, 2);
            chip.CustomMinimumSize = new Vector2(chip.CustomMinimumSize.X, Spec.Plaque.Height);
            foreach (var row in Descendants(chip).OfType<HBoxContainer>()) row.CustomMinimumSize = new Vector2(row.CustomMinimumSize.X, 0);
            foreach (var label in Descendants(chip).OfType<Label>())
            {
                Text(label, 12, PlaqueInk, shadow: PlaqueShadow); label.VerticalAlignment = VerticalAlignment.Center;
                label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            }
            foreach (var toggle in Descendants(chip).OfType<Button>())
            {
                // The expand toggle is a glyph on the plaque: no padding may raise the plaque above its 22 pixels.
                foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" }) toggle.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
                toggle.CustomMinimumSize = Vector2.Zero; toggle.AddThemeFontSizeOverride("font_size", 12);
                foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) toggle.AddThemeColorOverride(state, PlaqueInk);
                toggle.AddThemeFontOverride("font", Plugin.Kit.Bold);
            }
        }
        foreach (var child in column.GetChildren()) Chip(child);
        NativeWindows.OnDescendantAdded(column, node => Callable.From(() => { if (GodotObject.IsInstanceValid(node)) Chip(node); }).CallDeferred());
    }

    /// <summary>A one-line label plate: the information plaque behind the label, plaque ink over any native tint.</summary>
    private static void LabelPlate(Label label)
    {
        var box = new StyleBoxEmpty { ContentMarginLeft = Spec.Plaque.Inset, ContentMarginRight = Spec.Plaque.Inset, ContentMarginTop = 3, ContentMarginBottom = 3 };
        label.AddThemeStyleboxOverride("normal", box);
        // The plaque draws behind the label's text.
        var back = new ClassicPlaqueBack(label) { Name = "plaque_back", ShowBehindParent = true };
        label.AddChild(back);
        label.CustomMinimumSize = new Vector2(label.CustomMinimumSize.X, Spec.Plaque.Height);
        Text(label, 13, PlaqueInk, shadow: PlaqueShadow);
        Keep(label, () => Ink(label, PlaqueInk));
    }

    /// <summary>
    /// The siege tower Board/Use button: the client hangs it under its own target bar, which the Classic target frame
    /// replaces; it moves under the Classic target frame (centred, the service button art) and keeps its callback.
    /// </summary>
    private static void TowerButton(Button button)
    {
        ClassicEventFamily.Action(button);
        button.AddChild(new NativeEventWatcher(() =>
        {
            if (button.GetParent() is TargetFrame) return;
            if (NativeApi.Descendants(button.GetTree().Root).OfType<TargetFrame>().FirstOrDefault() is not { } frame) return;
            bool shown = button.Visible;
            button.Reparent(frame, false);
            button.Visible = shown;
            var size = button.GetCombinedMinimumSize();
            button.Position = new Vector2(Mathf.Round((frame.Size.X - size.X) / 2), frame.Size.Y + Spec.Board.TowerGap);
            button.Size = size;
        }) { Name = "classic_tower_host" });
    }

    /// <summary>The ranker paper keeps its original paper art; its buttons, text and edit field become Classic.</summary>
    private static void Paper(Control paper)
    {
        foreach (var button in Descendants(paper).OfType<Button>()) ClassicEventFamily.Action(button);
        foreach (var label in Descendants(paper).OfType<Label>())
        {
            label.AddThemeFontOverride("font", Plugin.Kit.Bold);
            label.AddThemeConstantOverride("outline_size", 2);
        }
        foreach (var edit in Descendants(paper).OfType<TextEdit>())
        {
            var box = ClassicDesign.InputBox();
            foreach (var state in new[] { "normal", "focus", "read_only" }) edit.AddThemeStyleboxOverride(state, box);
            edit.AddThemeFontOverride("font", Plugin.Kit.Bold);
            edit.AddThemeColorOverride("font_color", ClassicDesign.Text);
        }
    }

    /// <summary>
    /// The Wanted list buttons live on the client's mini map, which the Classic HUD hides. In the Ronark Land ladder
    /// zones (where the Wanted event runs) they show on an information plaque at the top right, with chat tab art.
    /// </summary>
    private static void Wanted(Button button)
    {
        if (NativeApi.WorldOf(button) is not { } world || NativeApi.Get<CanvasLayer>(world, "VanguardWindow.Layer") is not { } layer) return;
        if (layer.GetNodeOrNull<ClassicWantedPlaque>("classic_wanted") is not { } plaque)
        {
            plaque = new ClassicWantedPlaque(world) { Name = "classic_wanted" };
            layer.AddChild(plaque);
        }
        plaque.Add(button);
    }

    /// <summary>
    /// A mini-map plate (the Nereid monument strip): the client shows it on its mini map, which the Classic HUD hides,
    /// so it becomes an information plaque at the top right, under the Wanted plaque, shown whenever the client shows it.
    /// </summary>
    private static void MapPlate(Label label)
    {
        if (NativeApi.WorldOf(label) is not { } world) return;
        LabelPlate(label);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var layer = world.GetNodeOrNull<CanvasLayer>("classic_map_plates");
        if (layer == null) { layer = new CanvasLayer { Name = "classic_map_plates", Layer = Spec.MapPlates.Layer }; world.AddChild(layer); }
        bool shown = label.Visible;
        label.Reparent(layer, false);
        label.Visible = shown;
        label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        Keep(label, () =>
        {
            if (!label.IsInsideTree()) return;
            var at = new Vector2(label.GetViewportRect().Size.X - label.Size.X - Spec.Wanted.Margin, Spec.MapPlates.Top);
            if (label.Position != at) label.Position = at;
        });
    }

    private static void Chip(PanelContainer chip)
    {
        Plaque(chip, Spec.Plaque.Inset, 3, Spec.Plaque.Inset, 3);
        chip.CustomMinimumSize = new Vector2(0, Spec.Plaque.Height);
        // The chip hangs from its top offset; drop the flat panel's taller padding.
        chip.OffsetBottom = chip.OffsetTop + Spec.Plaque.Height;
        var labels = Descendants(chip).OfType<Label>().ToArray();
        // The chip keeps its width from zone to zone; the name stays centred on the plaque.
        foreach (var label in labels)
        {
            Text(label, 13, PlaqueInk, shadow: PlaqueShadow);
            label.VerticalAlignment = VerticalAlignment.Center; label.HorizontalAlignment = HorizontalAlignment.Center;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        // An action on a chip (Stop gathering) takes the chat tab art of plaque actions.
        foreach (var action in Descendants(chip).OfType<Button>())
        {
            TabButton(action);
            action.CustomMinimumSize = Spec.Plaque.Action; action.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            action.AddThemeFontSizeOverride("font_size", 8);
        }
        // The zone name carries the zone type; the native per-type tint has no place on the plaque.
        Keep(chip, () => { foreach (var label in labels) Ink(label, PlaqueInk); });
    }

    private static void Score(PanelContainer strip)
    {
        Plaque(strip, 12, 5, 12, 5);
        var labels = Descendants(strip).OfType<Label>().ToArray();
        foreach (var label in labels) Text(label, 13, PlaqueInk, shadow: PlaqueShadow);
        // The nation colours move to the original red and blue bars under each name; on the plaque every
        // caption keeps the plaque ink, which the client's per-update status tints would otherwise replace.
        foreach (var part in Descendants(strip).OfType<ColorRect>().ToArray())
        {
            bool karus = part.Color.R > part.Color.B;
            var source = karus ? new Rect2(323, 462, 175, 12) : new Rect2(323, 403, 175, 12);
            part.Color = Colors.Transparent;
            part.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
            part.Draw += () => part.DrawStyleBox(Bar(source), new Rect2(Vector2.Zero, part.Size));
            if (part.GetParent() is Control row) row.CustomMinimumSize = new Vector2(row.CustomMinimumSize.X, Spec.Plaque.BarHeight);
        }
        // A closable strip gets the plaque close; the Ranking action the chat tab art of plaque actions.
        foreach (var button in Descendants(strip).OfType<Button>())
        {
            if (button.TooltipText == "Close")
            {
                ClassicChatControls.SkinButton(button, Plugin.Kit.Layout("{nation}_notice_us").Find("btn_quit")!);
                button.Icon = null; button.Text = ""; button.FocusMode = Control.FocusModeEnum.None;
                button.CustomMinimumSize = new Vector2(20, 20);
            }
            else
            {
                TabButton(button);
                button.CustomMinimumSize = Spec.Plaque.Action;
                button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            }
        }
        Keep(strip, () => { foreach (var label in labels) Ink(label, PlaqueInk); });
    }

    private static void Announcement(Control node)
    {
        if (node is PanelContainer panel) panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        if (node.Name == "order_message" && !node.HasNode("classic_banner_stack")) node.AddChild(new ClassicBannerStack(node) { Name = "classic_banner_stack" });
        foreach (var label in Descendants(node).OfType<Label>())
        {
            int size = label.GetThemeFontSize("font_size");
            label.AddThemeFontOverride("font", Plugin.Kit.Bold);
            label.AddThemeFontSizeOverride("font_size", Math.Max(13, size));
            label.AddThemeConstantOverride("font_embolden", 0);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 3);
            label.AddThemeColorOverride("font_shadow_color", Colors.Black);
            label.AddThemeConstantOverride("shadow_offset_x", 1);
            label.AddThemeConstantOverride("shadow_offset_y", 1);
        }
    }

    private static void Loot(PanelContainer native)
    {
        if (native.GetChildren().OfType<MarginContainer>().FirstOrDefault() is not { } content) return;
        var cells = Descendants(native).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("loot_slot_"))
            .OrderBy(c => int.Parse(c.Name.ToString()["loot_slot_".Length..])).ToArray();
        if (cells.Length == 0) return;
        native.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        native.CustomMinimumSize = Spec.Loot.Size;
        var panel = new ClassicLootPanel { Name = "classic_loot" };
        native.AddChild(panel);
        content.Visible = false;
        var hover = new StyleBoxFlat { BgColor = new Color(1, 1, 1, .1f) };
        // The client's own cells, on the original art: their click takes, right click sets a loot rule, hover shows the tooltip.
        for (int i = 0; i < cells.Length && i < Spec.Loot.Cells.Length * Spec.Loot.Grids; i++)
        {
            Place(cells[i], panel, Spec.Loot.Cell(i));
            ClassicSlotLook.Attach(cells[i], light => light > 1 ? hover : new StyleBoxEmpty());
            cells[i].MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        }
        foreach (var (name, rect) in new[] { ("loot_take_all", Spec.Loot.TakeAll), ("loot_filter", Spec.Loot.Filter) })
            if (Find<Button>(native, name) is { } action)
            {
                Place(action, panel, rect); TabButton(action);
                // The caption draws the text; the button's own (transparent) text must not raise it above the 15-pixel art.
                action.AddThemeFontSizeOverride("font_size", 8);
            }
        // The client closes the box on Escape or when it walks away; the plaque adds the original close button.
        var close = new Button { Name = "loot_close", TooltipText = "Close", FocusMode = Control.FocusModeEnum.None };
        panel.AddChild(close);
        Place(close, panel, Spec.Loot.Close);
        ClassicChatControls.SkinButton(close, Plugin.Kit.Layout("{nation}_notice_us").Find("btn_quit")!);
        close.Pressed += () => NativeOverlays.CloseLoot(native);
        native.ResetSize();
        Keep(native, () => { if (native.Size != Spec.Loot.Size) native.Size = Spec.Loot.Size; });
    }
}

/// <summary>The original common message box art at its 327-pixel width and any height.</summary>
public partial class ClassicMessageArt : Control
{
    public ClassicMessageArt() { MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest; Resized += QueueRedraw; }

    public override void _Draw() => Paint(this, Size.Y);

    /// <summary>
    /// Draws the nine original pieces. A taller box stretches the middle band (y 64); a box shorter than the two
    /// 64-pixel halves trims their inner rows, which are plain frame sides, so the corners stay intact.
    /// </summary>
    public static void Paint(CanvasItem item, float height)
    {
        var root = Plugin.Kit.Layout("{nation}_messagebox_us");
        // Both halves are 64 pixels high, as is the band's offset.
        float band = ClassicOverlayLayout.Message.MiddleBand;
        float top = Math.Min(band, Mathf.Ceil(height / 2)), under = Math.Min(band, height - top);
        foreach (var image in root.Images)
        {
            if (image.Texture == null) continue;
            var texture = Plugin.Kit.Texture(image.Texture)!;
            if (image.Y == band)
            {
                float stretch = height - top - under;
                if (stretch > 0) item.DrawTextureRectRegion(texture, new Rect2(image.X, top, image.W, stretch), new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH));
            }
            else if (image.Y < band)
                item.DrawTextureRectRegion(texture, new Rect2(image.X, 0, image.W, top), new Rect2(image.SrcX, image.SrcY, image.SrcW, top));
            else
                item.DrawTextureRectRegion(texture, new Rect2(image.X, height - under, image.W, under),
                    new Rect2(image.SrcX, image.SrcY + image.SrcH - under, image.SrcW, under));
        }
    }
}

/// <summary>A message box host: blocks the world under the art and, when free-standing, stays centred.</summary>
public partial class ClassicMessagePanel : Control
{
    private readonly bool _centre;
    public ClassicMessagePanel(Vector2 size, bool centre)
    {
        _centre = centre; MouseFilter = MouseFilterEnum.Stop;
        Size = CustomMinimumSize = size;
        AddChild(new ClassicMessageArt { Name = "message_art", Size = size });
    }

    public override void _Process(double delta)
    {
        if (!_centre || !IsVisibleInTree() || GetParent() is not Control parent) return;
        Position = ((parent.Size - Size) / 2).Round();
    }
}

/// <summary>Shows a native button's own text on the original art with the art's caption ink.</summary>
public partial class ClassicButtonCaption : Label
{
    private static readonly StringName[] States = { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "font_disabled_color" };
    private readonly Button _button;
    private readonly Color _ink, _disabled;

    public ClassicButtonCaption() { _button = null!; }

    public ClassicButtonCaption(Button button, int size, bool bold, Color ink, Color shadow, Color disabled)
    {
        _button = button; _ink = ink; _disabled = disabled;
        MouseFilter = MouseFilterEnum.Ignore; ClipText = true;
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ClassicWorldOverlays.Text(this, size, ink, bold, shadow);
        // The client recolours some buttons per state (the registration's Join and Cancel). Clearing that inside the
        // client's own theme change keeps the caption the only text in every frame.
        _button.ThemeChanged += HideNativeText;
        Sync();
    }

    // Hidden captions skip the per-frame sync; a caption that is shown again syncs at once.
    public override void _Process(double delta) { if (IsVisibleInTree()) Sync(); }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Sync();
    }

    private void Sync()
    {
        if (_button == null || !GodotObject.IsInstanceValid(_button)) return;
        if (Text != _button.Text) Text = _button.Text;
        HideNativeText();
        var ink = _button.Disabled ? _disabled : _ink;
        if (GetThemeColor(HotNames.FontColor) != ink) AddThemeColorOverride(HotNames.FontColor, ink);
    }

    // The alpha check ends the theme change that each override raises again.
    private void HideNativeText()
    {
        if (!Plugin.Active || !GodotObject.IsInstanceValid(_button)) return;
        foreach (var state in States)
            if (_button.GetThemeColor(state).A > 0) _button.AddThemeColorOverride(state, Colors.Transparent);
    }
}

/// <summary>The nation information plaque at any size: original caps, stretched slate or stone body.</summary>
public static class ClassicPlaqueArt
{
    private const int MainWidth = 81, CapSource = 96, Height = 21;

    public static void Draw(CanvasItem item, Rect2 rect)
    {
        var (main, cap) = Boxes();
        float capWidth = ClassicOverlayLayout.Plaque.CapRight;
        item.DrawStyleBox(main, new Rect2(rect.Position, new Vector2(Math.Max(0, rect.Size.X - capWidth), rect.Size.Y)));
        item.DrawStyleBox(cap, new Rect2(rect.Position + new Vector2(rect.Size.X - capWidth, 0), new Vector2(capWidth, rect.Size.Y)));
    }

    private static (StyleBoxTexture, StyleBoxTexture) Boxes()
    {
        var art = Plugin.Kit.Layout("{nation}_msgoutput_small_us").Images.First();
        var texture = Plugin.Kit.Texture(art.Texture!);
        StyleBoxTexture Box(Rect2 region, int left, int right)
        {
            var box = new StyleBoxTexture { Texture = texture, RegionRect = region };
            box.SetTextureMargin(Side.Left, left); box.SetTextureMargin(Side.Right, right);
            box.SetTextureMargin(Side.Top, ClassicOverlayLayout.Plaque.CapTop); box.SetTextureMargin(Side.Bottom, ClassicOverlayLayout.Plaque.CapBottom);
            box.SetContentMarginAll(0);
            return box;
        }
        // The original plaque ends in a button well; the body joins the outer right cap directly.
        return (Box(new Rect2(art.SrcX, art.SrcY, MainWidth, Height), ClassicOverlayLayout.Plaque.CapLeft, 0),
            Box(new Rect2(art.SrcX + CapSource, art.SrcY, ClassicOverlayLayout.Plaque.CapRight, Height), 0, 0));
    }
}

/// <summary>The loot box art: the information plaque over the original dropped-item grids.</summary>
public partial class ClassicLootPanel : Control
{
    public ClassicLootPanel()
    {
        MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Nearest;
        Size = CustomMinimumSize = ClassicOverlayLayout.Loot.Size;
    }

    public override void _Draw()
    {
        ClassicPlaqueArt.Draw(this, ClassicOverlayLayout.Loot.Header);
        var grid = Plugin.Kit.Layout("co_droppeditem_us").Images.First();
        for (int i = 0; i < ClassicOverlayLayout.Loot.Grids; i++)
            DrawTextureRectRegion(Plugin.Kit.Texture(grid.Texture!)!, new Rect2(ClassicOverlayLayout.Loot.GridAt(i) + grid.Position, grid.SizeVec),
                new Rect2(grid.SrcX, grid.SrcY, grid.SrcW, grid.SrcH));
    }
}

/// <summary>
/// An information plaque carrying the client's Wanted list buttons (toggle and open) with chat tab art. It shows only
/// in the Ronark Land ladder zones, where the Wanted event runs; the toggle keeps the client's dimmed "off" state.
/// </summary>
public partial class ClassicWantedPlaque : Control
{
    private readonly WorldRoot _world;
    private readonly List<Button> _buttons = new();

    public ClassicWantedPlaque() { _world = null!; }

    public ClassicWantedPlaque(WorldRoot world)
    {
        _world = world; MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest;
        SetAnchorsPreset(LayoutPreset.TopRight);
    }

    public void Add(Button button)
    {
        button.Reparent(this, false);
        button.Icon = null;
        button.Text = button.Name == "wanted_toggle" ? "Wanted" : "List";
        button.Visible = true;
        ClassicWorldOverlays.TabButton(button);
        // The caption child draws the text; the button itself must not grow past the 55 x 15 tab art.
        button.AddThemeFontSizeOverride("font_size", 8);
        button.ClipText = true;
        button.AddThemeStyleboxOverride("hover_pressed", button.GetThemeStylebox("pressed"));
        _buttons.Add(button);
        Arrange();
    }

    private void Arrange()
    {
        var spec = ClassicOverlayLayout.Wanted.Size(_buttons.Count);
        Size = CustomMinimumSize = spec;
        for (int i = 0; i < _buttons.Count; i++)
        {
            var rect = ClassicOverlayLayout.Wanted.Button(i);
            _buttons[i].CustomMinimumSize = rect.Size; _buttons[i].Position = rect.Position; _buttons[i].Size = rect.Size;
            var button = _buttons[i];
            Callable.From(() => { if (GodotObject.IsInstanceValid(button)) button.Size = rect.Size; }).CallDeferred();
        }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_world)) return;
        int zone = Convert.ToInt32(NativeApi.Get<object>(_world, "ZoneId") ?? 0);
        bool shown = LibreKO.Domain.PkLadder.IsLadderZone(zone);
        if (Visible != shown) Visible = shown;
        var at = new Vector2(GetViewportRect().Size.X - Size.X - ClassicOverlayLayout.Wanted.Margin, ClassicOverlayLayout.Wanted.Top);
        if (Position != at) Position = at;
    }

    public override void _Draw() => ClassicPlaqueArt.Draw(this, new Rect2(Vector2.Zero, Size));
}

/// <summary>The information plaque behind a label (drawn behind the parent's text), sized to the label.</summary>
public partial class ClassicPlaqueBack : Control
{
    public ClassicPlaqueBack() { }
    public ClassicPlaqueBack(Control owner)
    {
        MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Resized += QueueRedraw;
    }
    public override void _Draw() => ClassicPlaqueArt.Draw(this, new Rect2(Vector2.Zero, Size));
}

/// <summary>
/// Keeps the order message (a flashing war notice) clear of the area banner: while the banner shows, the message
/// moves just below the banner's last line; otherwise it stays at the client's own position.
/// </summary>
public partial class ClassicBannerStack : Node
{
    public const float Gap = 10;
    private readonly Control _message;
    private float _home = float.NaN;

    public ClassicBannerStack() { _message = null!; }

    public ClassicBannerStack(Control message) { _message = message; }

    public override void _Process(double delta)
    {
        if (_message == null || !IsInstanceValid(_message)) return;
        if (float.IsNaN(_home)) _home = _message.Position.Y;
        float top = _home;
        if (_message.GetMeta(NativeOverlays.StackBelow, default(Variant)).AsGodotObject() is Control banner && IsInstanceValid(banner)
            && banner.IsVisibleInTree() && banner.Modulate.A > .01f)
        {
            float bottom = banner.GetChildren().OfType<Label>().Where(l => l.Visible && l.Text.Length > 0)
                .Select(l => l.GetGlobalRect().End.Y).DefaultIfEmpty(float.NegativeInfinity).Max();
            var origin = _message.GetGlobalRect().Position.Y - _message.Position.Y;
            if (bottom + Gap > origin + _home) top = Mathf.Ceil(bottom + Gap - origin);
        }
        if (Math.Abs(_message.Position.Y - top) > .5f) _message.Position = new Vector2(_message.Position.X, top);
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName FontColor = "font_color";
}
