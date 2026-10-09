using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterAppearance(PluginContext context)
    {
        NativeWindows.Prepare("genderchange", NativeAppearance.PrepareGender);
        NativeWindows.Prepare("changehair", NativeAppearance.PrepareBeauty);
        NativeWindows.Prepare("nationtransfer", NativeAppearance.PrepareTransfer);
    }
}

/// <summary>
/// Gender change, Beauty Shop and nation transfer in the Classic appearance composition. Gender change
/// only needs names and the Classic preview/editor behaviour. The Beauty Shop gets the 3D preview and
/// look editor of the original service in place of the client's steppers, and sends the editor's look.
/// Nation transfer gets a scrolling account list ordered by slot, an empty-account view, normalised
/// drafts, and a certificate confirmation that freezes the account's picks.
/// </summary>
public static class NativeAppearance
{
    public const string TransferTitle = "Nation Transfer";
    private const int PreviewWidth = 220, PreviewHeight = 300;
    private const float TurnDegrees = 30f;
    private const int NationTransferCertificate = 810096000;
    private const string CertificateTypo = "nation tranfer item is notavailable";
    private static readonly Color WarningColour = new("ff6a6a");
    private const string CertificateText = "A Nation Transfer Certificate is required.";

    private sealed class Beauty
    {
        public LookPreview Preview = null!;
        public LookEditor Editor = null!;
        public Label Name = null!, Identity = null!;
        public Button Apply = null!;
        public bool Awaiting;
    }

    private static readonly ConditionalWeakTable<World, Beauty> _beauty = new();
    private static readonly ConditionalWeakTable<LookPreview, Button[]> _turns = new();
    private sealed record Account(IReadOnlyList<NationTransferCandidate> Candidates, int Target);
    private static readonly ConditionalWeakTable<World, Account> _accounts = new();

    public static void Prepare(HudWindow window, World world)
    {
        switch (window.Id)
        {
            case "genderchange": PrepareGender(window, world); break;
            case "changehair": PrepareBeauty(window, world); break;
            case "nationtransfer": PrepareTransfer(window, world); break;
        }
    }

    public static void PrepareGender(HudWindow window, World world)
    {
        if (!NativeIdentity.Begin(window, "native_appearance")) return;
        var preview = Native.Get<LookPreview>(world, "_genderPreview");
        var editor = Native.Get<LookEditor>(world, "_genderEditor");
        var status = Native.Get<Label>(world, "_genderStatus");
        var accept = Native.Get<Button>(world, "_genderConfirm");
        if (preview == null || editor == null || status == null || accept == null) return;
        if (!NameCommon(preview, editor, status, accept)) return;
        NativeLookEditor.Attach(editor);
        LookFraming.Attach(preview, () => editor.Race);
        window.SetMeta("classic_appearance_controls", 1);
    }

    public static void PrepareBeauty(HudWindow window, World world)
    {
        if (!NativeIdentity.Begin(window, "native_appearance")) return;
        var apply = Native.Get<Button>(world, "_changeHairApply");
        var status = Native.Get<Label>(world, "_changeHairStatus");
        if (apply == null || status == null || Net.I is not { } net) return;
        var body = window.Body;
        foreach (var child in body.GetChildren().OfType<Control>()) child.Visible = false;

        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); body.AddChild(row);
        var preview = new LookPreview(PreviewWidth, PreviewHeight) { Name = "look_preview" };
        row.AddChild(PreviewColumn(preview));
        var form = new VBoxContainer(); form.AddThemeConstantOverride("separation", 8); row.AddChild(form);
        var name = NativeIdentity.HudLabel(14); name.Name = "look_name"; form.AddChild(name);
        var identity = NativeIdentity.HudLabel(13); identity.Name = "look_identity"; form.AddChild(identity);
        var editor = new LookEditor { Name = "look_editor" };
        form.AddChild(editor);
        NativeLookEditor.Attach(editor, () => CharacterPreview.HairCount(editor.Race) > 0);
        editor.GetNode<Control>("look_race_heading").Hide();
        editor.GetNode<Control>("look_races").Hide();
        status.Reparent(form, false);
        status.Visible = true; status.Name = "look_status";
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.CustomMinimumSize = new Vector2(230, 0);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 8); form.AddChild(actions);
        apply.Reparent(actions, false);
        apply.Visible = true; apply.Name = "look_accept"; apply.Text = "Apply";
        apply.AddThemeFontSizeOverride("font_size", 13);
        var cancel = ActionButton("Cancel", () => Native.Call(world, "CloseChangeHair")); cancel.Name = "look_cancel"; actions.AddChild(cancel);

        var state = new Beauty { Preview = preview, Editor = editor, Name = name, Identity = identity, Apply = apply };
        _beauty.AddOrUpdate(world, state);
        LookFraming.Attach(preview, () => editor.Race);
        editor.Changed += () => ShowBeautyLook(world);
        NativeIdentity.Rewire(apply, () => SubmitBeauty(world));
        window.VisibilityChanged += () =>
        {
            if (window.Visible) OpenBeauty(world, state);
            else { NativeLookEditor.CloseColourPicker(editor); LookFraming.Clear(preview); }
        };
        Action<bool, int, int> result = (_, _, _) => BeautyResult(state);
        net.ChangeHairResultEvent += result;
        world.TreeExiting += () => net.ChangeHairResultEvent -= result;
        // The Classic service shows refusals in the shared warning colour.
        NativeWindows.Sync(window, () =>
        {
            if (status.GetThemeColor("font_color") == WarningColour) status.AddThemeColorOverride("font_color", UiTheme.Bad);
        });
        window.SetMeta("classic_appearance_controls", 1);
        if (window.Visible) OpenBeauty(world, state);
    }

    private static void OpenBeauty(World world, Beauty state)
    {
        if (Net.I is not { } net) return;
        var me = net.LastEnter;
        NativeLookEditor.SetLocked(state.Editor, false);
        state.Editor.Load(Array.Empty<int>(), me.Race, Native.Get<int>(world, "_selfFace"), Native.Get<int>(world, "_selfHair"));
        state.Name.Text = me.Name;
        state.Identity.Text = StarterStats.RaceName(me.Race);
        state.Apply.Disabled = false;
        ShowBeautyLook(world);
    }

    /// <summary>Shows the editor's look; nothing changes while a request is pending.</summary>
    public static void ShowBeautyLook(World world)
    {
        if (!_beauty.TryGetValue(world, out var state) || Native.Get<bool>(world, "_changeHairInFlight")) return;
        state.Editor.Refresh();
        NativeLookEditor.Refresh(state.Editor);
        state.Preview.Show(state.Editor.Race, state.Editor.Face, state.Editor.HairStyle, state.Editor.HairColour);
    }

    /// <summary>Sends the edited face, style and colour as one Beauty Shop request.</summary>
    public static void SubmitBeauty(World world)
    {
        if (!_beauty.TryGetValue(world, out var state) || !Native.Get<bool>(world, "_changeHairShown")
            || Native.Get<bool>(world, "_changeHairInFlight") || Native.Get<bool>(world, "_selfDead") || Net.I is not { } net) return;
        if (!HasBeautyCoupon(world)) return;
        Native.Set(world, "_changeHairInFlight", true);
        state.Awaiting = true;
        NativeLookEditor.SetLocked(state.Editor, true);
        state.Apply.Disabled = true;
        Native.Call(world, "SetChangeHairStatus", "Applying…", false);
        if (Native.Call(net, "SendChangeHair", state.Editor.Hair, state.Editor.Face) is false)
            Native.Call(world, "OnChangeHairResult", false, 0, 0);
    }

    /// <summary>
    /// Clients that charge a Makeover Coupon refuse before sending when the bag has none, with the client's
    /// own text; older clients have no coupon rule.
    /// </summary>
    private static bool HasBeautyCoupon(World world)
    {
        if (Native.ClientType("LibreKO.Domain.BeautyShop") is not { } shop || !Native.TryGet<int>(shop, "Coupon", out int coupon)) return true;
        if (Native.Call(world, "HasItemInBackpack", coupon) is not false) return true;
        int text = Native.TryGet<int>(shop, "NoCouponText", out int id) ? id : 0;
        Native.Call(world, "SetChangeHairStatus", LibreKO.Domain.ItemData.Text(text, "You need a Makeover Coupon."), true);
        return false;
    }

    private static void BeautyResult(Beauty state)
    {
        if (!state.Awaiting || !GodotObject.IsInstanceValid(state.Editor)) return;
        state.Awaiting = false;
        NativeLookEditor.SetLocked(state.Editor, false);
        state.Apply.Disabled = false;
    }

    public static void PrepareTransfer(HudWindow window, World world)
    {
        // The Classic confirmation drives the client's staged transfer; without it the native window stays in charge.
        if (!Native.HasMethod(world, "SetTransferLocked") || !Native.HasMethod(world, "CancelTransferConfirmation")
            || !Native.Has(world, "_transferRevision") || !Native.Has(world, "_transferNotice")) return;
        if (!NativeIdentity.Begin(window, "native_appearance")) return;
        var preview = Native.Get<LookPreview>(world, "_transferPreview");
        var editor = Native.Get<LookEditor>(world, "_transferEditor");
        var status = Native.Get<Label>(world, "_transferStatus");
        var accept = Native.Get<Button>(world, "_transferConfirm");
        var header = Native.Get<Label>(world, "_transferHeader");
        var list = Native.Get<VBoxContainer>(world, "_transferList");
        var layer = Native.Get<CanvasLayer>(world, "_transferLayer");
        if (preview == null || editor == null || status == null || accept == null || header == null || list == null
            || layer == null || list.GetParent() is not Control column || Net.I is not { } net) return;
        if (!NameCommon(preview, editor, status, accept)) return;
        header.Name = "transfer_header";
        NativeWindows.Name(column.GetChildren().OfType<Label>().FirstOrDefault(), "transfer_characters_heading");
        var scroll = new ScrollContainer
        {
            Name = "transfer_scroll", CustomMinimumSize = new Vector2(180, 300),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        int index = list.GetIndex();
        list.Reparent(scroll, false);
        column.AddChild(scroll);
        column.MoveChild(scroll, index);
        list.Name = "transfer_characters";
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        NativeLookEditor.Attach(editor, () => CharacterPreview.HairCount(editor.Race) > 0);
        LookFraming.Attach(preview, () => editor.Race);
        NativeIdentity.Rewire(accept, () => TransferPressed(world));
        ReplaceOpen(world, net);
        window.VisibilityChanged += () => { if (!window.Visible) NativeLookEditor.CloseColourPicker(editor); };
        layer.AddChild(new NativeProcess(() => AdoptNotices(world, layer)) { Name = "transfer_notices" });
        NativeWindows.Sync(window, () => SyncTransfer(world, editor, status));
        window.SetMeta("classic_appearance_controls", 1);
    }

    /// <summary>Opens the account only while no transfer is shown, pending or completed.</summary>
    private static void ReplaceOpen(World world, Net net)
    {
        if (Native.Get<Delegate>(net, "NationTransferOpenEvent") is not { } handlers) return;
        Delegate? remaining = handlers;
        foreach (var handler in handlers.GetInvocationList())
            if (handler.Target == world && handler.Method.Name == "OnNationTransferOpen") remaining = Delegate.Remove(remaining, handler);
        Action<IReadOnlyList<NationTransferCandidate>> open = candidates => OpenTransfer(world, candidates);
        Native.Set(net, "NationTransferOpenEvent", Delegate.Combine(remaining, open));
    }

    public static void OpenTransfer(World world, IReadOnlyList<NationTransferCandidate> candidates)
    {
        if (Native.Get<bool>(world, "_transferInFlight") || TransferNotice(world) != null || Native.Get<bool>(world, "_transferCompleted")
            || Native.Get<bool>(world, "_transferShown"))
        {
            // The network layer already cached the ignored list; the shown account stays the one to submit.
            if (Net.I is { } net && _accounts.TryGetValue(world, out var shown))
            {
                Native.Set(net, "_nationTransferCandidates", shown.Candidates);
                Native.Set(net, "_nationTransferTarget", shown.Target);
            }
            return;
        }
        if (Net.I is { } current)
            _accounts.AddOrUpdate(world, new Account(candidates, Native.Get<int>(current, "_nationTransferTarget")));
        var editor = Native.Get<LookEditor>(world, "_transferEditor")!;
        var preview = Native.Get<LookPreview>(world, "_transferPreview")!;
        var list = Native.Get<VBoxContainer>(world, "_transferList")!;
        Native.Call(world, "CloseNpcDialog");
        Native.Call(world, "OpenNationTransfer", candidates);
        foreach (var child in list.GetChildren())
            if (child.IsQueuedForDeletion()) list.RemoveChild(child);
        NativeLookEditor.SetLocked(editor, false);
        if (list.GetParent() is ScrollContainer scroll) scroll.ScrollVertical = 0;
        if (candidates.Count == 0)
        {
            Native.Get<Label>(world, "_transferHeader")!.Text = "No characters are available for nation transfer.";
            editor.Visible = false;
            LookFraming.Clear(preview);
            SetPreviewAvailable(preview, false);
            Native.Call(world, "SetTransferStatus", "", false);
            Native.Get<Button>(world, "_transferConfirm")!.Disabled = true;
            Native.Get<HudWindow>(world, "_transferPanel")!.Visible = true;
            Native.Set(world, "_transferShown", true);
            return;
        }
        editor.Visible = true;
        SetPreviewAvailable(preview, true);
        var picks = Native.Get<Dictionary<int, NationTransferPick>>(world, "_transferPicks")!;
        foreach (var candidate in candidates)
        {
            int faces = CharacterPreview.FaceCount(candidate.Race), hairs = CharacterPreview.HairCount(candidate.Race);
            int face = faces > 0 ? Mathf.Clamp(candidate.Face, 0, faces - 1) : candidate.Face;
            int style = hairs > 0 ? Mathf.Clamp(HairCode.StyleOf(candidate.Hair), 0, hairs - 1) : HairCode.StyleOf(candidate.Hair);
            picks[candidate.Slot] = new NationTransferPick(candidate.Slot, candidate.Name, candidate.Race, face, (style << 24) | (candidate.Hair & 0xffffff));
        }
        if (Native.Get<System.Collections.IList>(world, "_transferRows") is { } rows)
        {
            var ordered = rows.Cast<ITuple>().Select(row => (Candidate: (NationTransferCandidate)row[0]!, Button: (Button)row[1]!))
                .OrderBy(row => row.Candidate.Slot).ToArray();
            for (int i = 0; i < ordered.Length; i++)
            {
                var (candidate, button) = ordered[i];
                string job = CharacterClassCatalog.DisplayName(candidate.Class);
                button.Name = "transfer_character_" + candidate.Slot;
                button.Text = $"{candidate.Name}\n{job}";
                button.TooltipText = $"{candidate.Name} · {job}";
                list.MoveChild(button, i);
            }
        }
        var first = candidates.OrderBy(candidate => candidate.Slot).First();
        Native.Call(world, "SelectTransferCharacter", first);
        picks[first.Slot] = new NationTransferPick(first.Slot, first.Name, editor.Race, editor.Face, editor.Hair);
    }

    private static void SetPreviewAvailable(LookPreview preview, bool available)
    {
        preview.Visible = available;
        if (_turns.TryGetValue(preview, out var turns))
            foreach (var button in turns) button.Visible = available;
    }

    public static Notice? TransferNotice(World world) => Native.Get<Notice>(world, "_transferNotice");

    /// <summary>Opens the certificate confirmation with the account's picks captured at this moment.</summary>
    public static void TransferPressed(World world)
    {
        var picks = Native.Get<Dictionary<int, NationTransferPick>>(world, "_transferPicks");
        if (!Native.Get<bool>(world, "_transferShown") || Native.Get<bool>(world, "_transferInFlight") || TransferNotice(world) != null
            || picks is not { Count: > 0 } || Native.Get<bool>(world, "_selfDead")) return;
        int revision = Native.Get<int>(world, "_transferRevision") + 1;
        Native.Set(world, "_transferRevision", revision);
        var captured = picks.Values.OrderBy(pick => pick.Slot).ToArray();
        Native.Call(world, "SetTransferLocked", true);
        if (Native.Get<LookEditor>(world, "_transferEditor") is { } editor) NativeLookEditor.SetLocked(editor, true);
        string nation = Nations.Name(Native.Get<int>(world, "_transferNation"));
        var layer = Native.Get<Node>(world, "_transferLayer") ?? world;
        var notice = Notice.Confirm(layer,
            $"All {captured.Length} characters move to {nation}.\nYour {ItemData.DisplayName(NationTransferCertificate).Trim()} will be used.\nYou will return to character selection.",
            "Transfer", "Cancel", () => SendTransfer(world, revision, captured),
            () => Native.Call(world, "CancelTransferConfirmation", revision), title: TransferTitle);
        Native.Set(world, "_transferNotice", notice);
    }

    private static void SendTransfer(World world, int revision, NationTransferPick[] picks)
    {
        if (Native.Get<bool>(world, "_selfDead") && revision == Native.Get<int>(world, "_transferRevision") && TransferNotice(world) != null
            && !Native.Get<bool>(world, "_transferInFlight"))
        {
            Native.Set(world, "_transferNotice", null);
            Native.Call(world, "SetTransferLocked", false);
            return;
        }
        Native.Call(world, "SendNationTransfer", revision, (IReadOnlyList<NationTransferPick>)picks);
    }

    private static void SyncTransfer(World world, LookEditor editor, Label status)
    {
        bool locked = Native.Get<bool>(world, "_transferInFlight") || TransferNotice(world) != null;
        if (NativeLookEditor.Locked(editor) != locked) NativeLookEditor.SetLocked(editor, locked);
        if (status.Text.Contains(CertificateTypo, StringComparison.OrdinalIgnoreCase))
            status.Text = status.Text.Replace(CertificateTypo, CertificateText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Transfer messages belong to the transfer layer, not the world behind it.</summary>
    private static void AdoptNotices(World world, CanvasLayer layer)
    {
        foreach (var notice in world.GetChildren().OfType<Notice>())
            if (!notice.IsQueuedForDeletion() && Native.Get<DialogRequest>(notice, "_request")?.Title == TransferTitle)
                notice.Reparent(layer, false);
    }

    private static bool NameCommon(LookPreview preview, LookEditor editor, Label status, Button accept)
    {
        var cancel = accept.GetParent()?.GetChildren().OfType<Button>().FirstOrDefault(b => b != accept);
        var turns = preview.GetParent()?.GetChildren().OfType<HBoxContainer>().FirstOrDefault()?.GetChildren().OfType<Button>().ToArray();
        if (cancel == null || turns is not { Length: 2 }) return false;
        preview.Name = "look_preview"; editor.Name = "look_editor"; status.Name = "look_status";
        accept.Name = "look_accept"; cancel.Name = "look_cancel";
        turns[0].Name = "look_turn_left"; turns[1].Name = "look_turn_right";
        _turns.AddOrUpdate(preview, turns);
        return true;
    }

    private static Control PreviewColumn(LookPreview preview)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        column.AddChild(preview);
        var turnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        turnRow.AddThemeConstantOverride("separation", 8);
        var left = LookEditor.StepButton("◀", () => preview.Turn(-TurnDegrees)); left.Name = "look_turn_left";
        var right = LookEditor.StepButton("▶", () => preview.Turn(TurnDegrees)); right.Name = "look_turn_right";
        turnRow.AddChild(left); turnRow.AddChild(right);
        column.AddChild(turnRow);
        return column;
    }

    private static Button ActionButton(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += pressed;
        return button;
    }
}
