using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Native;
using Message = KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Message;
using Spec = KnightOnlineUiClassic.Layout.ClassicUserVoteLayout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The Juraid user vote windows (3277df5) on the original message box. The client builds each window on every
/// opening and frees it when answered; the Classic content replaces the window's flow content and drives the
/// client through the plugin user-vote API (<see cref="IGameUserVote"/>): the reason picked and the typed text
/// go to <c>Send</c>, Cancel to <c>CancelSelect</c>, Yes/Draw/No to <c>Answer</c> (the client still answers Draw
/// when the 30 second countdown ends), OK to <c>CloseNotice</c>.
/// </summary>
public static class ClassicUserVoteSkin
{
    public const string SelectId = "user_vote", AskId = "user_vote_ask", NoticeId = "user_vote_notice";
    public static readonly string[] WindowIds = { SelectId, AskId, NoticeId };

    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();

    public static ClassicUserVoteDialog? Apply(Control body)
    {
        if (!Plugin.Active || !GodotObject.IsInstanceValid(body)) return null;
        Node? owner = body;
        while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_user_vote") || !WindowIds.Contains(window.Id)) return null;
        window.SetMeta("classic_user_vote", true);
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var dialog = new ClassicUserVoteDialog(window, Plugin.Kit.Game.UserVote);
        window.AddChild(dialog);
        window.ResetSize();
        return dialog;
    }
}

public partial class ClassicUserVoteDialog : Control
{
    private readonly HudWindow _window = null!;
    private readonly IGameUserVote _vote = null!;
    private readonly ClassicMessageArt _art = null!;
    private readonly List<CheckBox> _reasons = new();
    private LineEdit? _other;
    private ProgressBar? _bar;
    private int _reason = 1;
    private bool _done;

    public ClassicUserVoteDialog() { }

    public ClassicUserVoteDialog(HudWindow window, IGameUserVote vote)
    {
        _window = window; _vote = vote;
        Name = "classic_user_vote"; MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Nearest;
        _art = new ClassicMessageArt { Name = "message_art" };
        AddChild(_art);
        string title = window.Id == ClassicUserVoteSkin.NoticeId ? vote.NoticeTitle : vote.Title;
        var heading = Caption("vote_title", title, Message.Title, Spec.TitleSize, ClassicWorldOverlays.MessageTitle);
        heading.HorizontalAlignment = HorizontalAlignment.Center; heading.ClipText = true;
        heading.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        float bottom = window.Id switch
        {
            ClassicUserVoteSkin.SelectId => BuildSelect(),
            ClassicUserVoteSkin.AskId => BuildAsk(),
            _ => BuildNotice(),
        };
        var size = Spec.Size(bottom);
        CustomMinimumSize = Size = size;
        _art.Size = size;
        if (window.Id == ClassicUserVoteSkin.SelectId)
        {
            Action("vote_ok", "OK", Message.Left(size.Y), false, Submit);
            Action("vote_cancel", "Cancel", Message.Right(size.Y), true, Cancel);
        }
        else if (window.Id == ClassicUserVoteSkin.AskId)
        {
            var answers = new (string Name, string Text, byte Answer)[] { ("vote_yes", "Yes", (byte)UserVoteAnswer.Yes), ("vote_draw", "Draw", (byte)UserVoteAnswer.Draw), ("vote_no", "No", (byte)UserVoteAnswer.No) };
            for (int i = 0; i < answers.Length; i++)
            {
                var answer = (UserVoteAnswer)answers[i].Answer;
                Action(answers[i].Name, answers[i].Text, Spec.Answer(size.Y, i), answer == UserVoteAnswer.No, () => Answer(answer));
            }
        }
        else Action("vote_notice_ok", "OK", Message.Single(size.Y), false, CloseNotice);
    }

    /// <summary>The prompt, the four reasons as radio rows, the field for the fourth (other) reason and the cost.</summary>
    private float BuildSelect()
    {
        float y = Text("vote_prompt", _vote.SelectPrompt, Spec.Top, ClassicWorldOverlays.MessageText) + Spec.LineGap;
        var group = new ButtonGroup();
        // The client's reason texts, or English captions where its text table has none.
        var reasons = NativeUserVote.Reasons(_vote);
        for (int i = 0; i < reasons.Count; i++)
        {
            int reason = i + 1;
            var check = new CheckBox { Name = "vote_reason_" + reason, Text = reasons[i], ButtonGroup = group, ButtonPressed = reason == 1, FocusMode = FocusModeEnum.None };
            Radio(check);
            check.Pressed += () => _reason = reason;
            Place(check, Spec.Reason(y, i));
            _reasons.Add(check);
        }
        y += Spec.Reasons(reasons.Count) + 4;
        _other = new LineEdit { Name = "vote_other", MaxLength = UserVote.OtherTextMaxLetters, PlaceholderText = reasons.Count >= UserVote.ReasonOther ? reasons[UserVote.ReasonOther - 1] : "", Alignment = HorizontalAlignment.Left };
        var field = ClassicDarkFrame.InputBox(); field.ContentMarginTop = field.ContentMarginBottom = 0;
        _other.AddThemeStyleboxOverride("normal", field); _other.AddThemeStyleboxOverride("focus", field);
        _other.AddThemeFontOverride("font", Plugin.Kit.Bold); _other.AddThemeFontSizeOverride("font_size", Spec.ReasonSize);
        _other.AddThemeColorOverride("font_color", ClassicWorldOverlays.MessageText);
        _other.AddThemeColorOverride("font_placeholder_color", ClassicDesign.Muted);
        _other.AddThemeColorOverride("caret_color", ClassicWorldOverlays.MessageText);
        // Typing picks the other reason, as in the client.
        _other.TextChanged += _ => PickReason(UserVote.ReasonOther);
        _other.TextSubmitted += _ => Submit();
        Place(_other, Spec.Edit(y));
        y += Spec.Field + Spec.LineGap;
        return Text("vote_cost", _vote.CostLine, y, ClassicDesign.FrameTitle);
    }

    /// <summary>The question, the reason in gold and the original countdown bar.</summary>
    private float BuildAsk()
    {
        float y = Text("vote_question", _vote.AskQuestion, Spec.Top, ClassicWorldOverlays.MessageText);
        string reason = NativeUserVote.AskReason(_vote);
        if (reason.Length > 0) y = Text("vote_reason", reason, y + 4, ClassicDesign.FrameTitle);
        y += Spec.LineGap;
        _bar = new ProgressBar { Name = "vote_countdown", ShowPercentage = false, MaxValue = _vote.AskSeconds, Value = _vote.AskSecondsLeft, MouseFilter = MouseFilterEnum.Ignore };
        ClassicWorldOverlays.ProgressBar(_bar, false);
        Place(_bar, Spec.Bar(y));
        return y + Spec.BarHeight;
    }

    private float BuildNotice()
    {
        float y = Spec.Top;
        var lines = _vote.NoticeLines;
        for (int i = 0; i < lines.Count; i++) y = Text("vote_notice_" + i, lines[i], i == 0 ? y : y + 4, ClassicWorldOverlays.MessageText);
        return y;
    }

    private void PickReason(int reason)
    {
        _reason = reason;
        // Through the group, so the previous reason is released (Pressed is not raised by this).
        if (reason - 1 < _reasons.Count) _reasons[reason - 1].ButtonPressed = true;
    }

    public int Reason => _reason;

    private void Submit()
    {
        if (_done) return;
        // The client checks the text and keeps the window when it refuses; it frees the window once it sends.
        _vote.Send(_reason, _other?.Text ?? "");
    }

    private void Cancel()
    {
        if (_done) return;
        _done = true;
        _vote.CancelSelect();
    }

    private void Answer(UserVoteAnswer answer)
    {
        if (_done) return;
        _done = true;
        _vote.Answer(answer);
    }

    private void CloseNotice()
    {
        if (_done) return;
        _done = true;
        _vote.CloseNotice();
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        // A dialog of its own: centred on the screen like the other Classic message boxes.
        var centred = ((GetViewportRect().Size - _window.Size) / 2).Floor();
        if (_window.Position != centred) _window.Position = centred;
        if (_bar == null) return;
        double left = _vote.AskSecondsLeft;
        _bar.MaxValue = _vote.AskSeconds; _bar.Value = left;
        bool urgent = left <= 10;
        if (_bar.GetMeta(NativeOverlays.Urgent, false).AsBool() != urgent) ClassicWorldOverlays.ProgressBar(_bar, urgent);
    }

    public override void _Input(InputEvent ev)
    {
        if (!IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (_window.Id == ClassicUserVoteSkin.SelectId && key.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); Cancel(); }
        else if (_window.Id == ClassicUserVoteSkin.NoticeId && key.Keycode is Key.Escape or Key.Enter or Key.KpEnter) { GetViewport().SetInputAsHandled(); CloseNotice(); }
    }

    private float Text(string name, string text, float top, Color ink)
    {
        var label = new Label { Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        ClassicWorldOverlays.Text(label, Spec.TextSize, ink);
        float height = Math.Max(1, ClassicWorldOverlays.Wrapped(label, Spec.TextWidth));
        label.VerticalAlignment = VerticalAlignment.Top;
        Place(label, Spec.Text(top, height));
        return top + height;
    }

    private Label Caption(string name, string text, Rect2 rect, int size, Color ink)
    {
        var label = new Label { Name = name, Text = text, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        ClassicWorldOverlays.Text(label, size, ink);
        Place(label, rect);
        return label;
    }

    /// <summary>A radio row with the original check socket, Arial Bold and the message ink.</summary>
    private static void Radio(CheckBox check)
    {
        check.AddThemeFontOverride("font", Plugin.Kit.Bold); check.AddThemeFontSizeOverride("font_size", Spec.ReasonSize);
        foreach (var state in new[] { "font_color", "font_focus_color", "font_pressed_color" }) check.AddThemeColorOverride(state, ClassicWorldOverlays.MessageText);
        foreach (var state in new[] { "font_hover_color", "font_hover_pressed_color" }) check.AddThemeColorOverride(state, ClassicDesign.FrameTitle);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            check.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        check.AddThemeConstantOverride("h_separation", 6);
        check.ClipText = true; check.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        if (Plugin.Kit.Layout("{nation}_chat_us").Find("btn_check_normal") is not { } socket) return;
        foreach (var state in new[] { "unchecked", "checked", "radio_unchecked", "radio_checked" })
        {
            var art = socket.Images.FirstOrDefault(i => i.Tag == (state.Contains("unchecked") ? 0 : 1));
            if (art?.Texture is { } texture)
                check.AddThemeIconOverride(state, new AtlasTexture { Atlas = Plugin.Kit.Texture(texture), Region = new Rect2(art.SrcX, art.SrcY, art.SrcW, art.SrcH), FilterClip = true });
        }
    }

    private void Action(string name, string text, Rect2 rect, bool cancel, Action pressed)
    {
        var button = new Button { Name = name, Text = text, FocusMode = FocusModeEnum.None };
        Place(button, rect);
        ClassicWorldOverlays.MessageButton(button, cancel);
        button.Pressed += pressed;
    }

    private void Place(Control control, Rect2 rect)
    {
        AddChild(control);
        control.Position = rect.Position; control.Size = rect.Size;
        control.SetMeta(ClassicWorldOverlays.ExpectedRect, rect);
        // Sizes set before the dialog enters the tree are clamped to stale theme minimums; set them again in it.
        control.Ready += () => { control.Position = rect.Position; control.Size = rect.Size; };
    }
}
