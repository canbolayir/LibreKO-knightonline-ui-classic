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
        NativeWindows.Prepare("hairpalette", NativeAppearance.PreparePalette);
        NativeWindows.Prepare("nationtransfer", NativeAppearance.PrepareTransfer);
    }
}

/// <summary>
/// Gender change, Beauty Shop and nation transfer in the Classic appearance composition. Gender change
/// and the Beauty Shop only need names and the Classic preview/editor behaviour; the Beauty Shop's hair
/// colour becomes a swatch row that opens the client's hair palette, and its editor stays locked while a
/// request is pending. Nation transfer gets a scrolling account list ordered by slot, an empty-account view, normalised
/// drafts, and a certificate confirmation that freezes the account's picks.
/// </summary>
public static class NativeAppearance
{
    public const string TransferTitle = "Nation Transfer";
    private const int NationTransferCertificate = 810096000;
    private const string CertificateTypo = "nation tranfer item is notavailable";
    private const string CertificateText = "A Nation Transfer Certificate is required.";

    private static readonly ConditionalWeakTable<LookPreview, Button[]> _turns = new();
    private sealed record Account(IReadOnlyList<NationTransferCandidate> Candidates, int Target);
    private static readonly ConditionalWeakTable<WorldRoot, Account> _accounts = new();

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        switch (window.Id)
        {
            case "genderchange": PrepareGender(window, world); break;
            case "changehair": PrepareBeauty(window, world); break;
            case "hairpalette": PreparePalette(window, world); break;
            case "nationtransfer": PrepareTransfer(window, world); break;
        }
    }

    public static void PrepareGender(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_appearance")) return;
        var preview = Native.Get<LookPreview>(world, "GenderChangeWindow.Preview");
        var editor = Native.Get<LookEditor>(world, "GenderChangeWindow.Editor");
        var status = Native.Get<Label>(world, "GenderChangeWindow.status");
        var accept = Native.Get<Button>(world, "GenderChangeWindow.confirm");
        if (preview == null || editor == null || status == null || accept == null) return;
        if (!NameCommon(preview, editor, status, accept)) return;
        NativeLookEditor.Attach(editor);
        LookFraming.Attach(preview, () => editor.Race);
        window.SetMeta("classic_appearance_controls", 1);
    }

    public static void PrepareBeauty(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_appearance")) return;
        var preview = Native.Get<LookPreview>(world, "ChangeHair.BeautyPreview");
        var editor = Native.Get<LookEditor>(world, "ChangeHair.BeautyEditor");
        var status = Native.Get<Label>(world, "ChangeHair.status");
        var accept = Native.Get<Button>(world, "ChangeHair.beautyConfirm");
        // The client's "Change hair color" action sits between the editor and the status line.
        var palette = window.Body.GetChildren().OfType<Button>().FirstOrDefault();
        if (preview == null || editor == null || status == null || accept == null || palette == null || !Native.Has(world, "ChangeHair.BeautyInFlight")) return;
        if (!NameCommon(preview, editor, status, accept)) return;
        NativeLookEditor.Attach(editor, () => CharacterPreview.HairCount(editor.Race) > 0);
        if (Native.Get<ColorPickerButton>(editor, "colourPicker") is not { } picker || picker.GetParent() is not HBoxContainer colourRow) return;
        picker.Visible = false;
        colourRow.Visible = true;
        palette.Reparent(colourRow, false);
        palette.Name = "look_palette";
        palette.TooltipText = palette.Text;
        palette.Text = "";
        palette.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        accept.Text = "Apply";
        var name = NativeIdentity.HudLabel(14); name.Name = "look_name"; window.Body.AddChild(name);
        var identity = NativeIdentity.HudLabel(13); identity.Name = "look_identity"; window.Body.AddChild(identity);
        LookFraming.Attach(preview, () => editor.Race);
        void Opened()
        {
            if (Game.Connection is not { } net) return;
            name.Text = Game.Session.Self.LastEnter.Name;
            identity.Text = StarterStats.RaceName(Game.Session.Self.LastEnter.Race);
        }
        window.VisibilityChanged += () => { if (window.Visible) Opened(); };
        if (window.Visible) Opened();
        NativeWindows.Sync(window, () =>
        {
            // A sent look cannot be edited until the server answers; the palette closes with it.
            bool busy = Native.Get<bool>(world, "ChangeHair.BeautyInFlight");
            if (NativeLookEditor.Locked(editor) != busy)
            {
                NativeLookEditor.SetLocked(editor, busy);
                if (busy) Native.TryCall(world, "ChangeHair.CloseHairPalette", out _);
            }
            palette.Disabled = busy || CharacterPreview.HairCount(editor.Race) == 0;
        });
        window.SetMeta("classic_appearance_controls", 1);
    }

    /// <summary>The hair palette the Beauty Shop opens: names its prompt, picker and actions for the Classic skin.</summary>
    public static void PreparePalette(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_appearance")) return;
        var body = window.Body;
        var prompt = body.GetChildren().OfType<Label>().FirstOrDefault();
        var picker = Native.Get<ColorPicker>(world, "ChangeHair.hairPalettePicker");
        var actions = body.GetChildren().OfType<HBoxContainer>().LastOrDefault()?.GetChildren().OfType<Button>().ToArray();
        if (prompt == null || picker == null || picker.GetParent() != body || actions is not { Length: 2 }) return;
        prompt.Name = "palette_prompt"; picker.Name = "palette_picker";
        actions[0].Name = "palette_accept"; actions[1].Name = "palette_cancel";
        window.SetMeta("classic_palette_controls", 1);
    }

    /// <summary>The Beauty Shop or gender editor whose colour the palette edits.</summary>
    public static LookEditor? PaletteTarget(WorldRoot world) =>
        Native.Get<LookEditor>(world, "ChangeHair.hairPaletteTarget") ?? Native.Get<LookEditor>(world, "ChangeHair.BeautyEditor");

    public static void PrepareTransfer(HudWindow window, WorldRoot world)
    {
        // The Classic confirmation drives the client's staged transfer; without it the native window stays in charge.
        if (!Native.HasMethod(world, "NationTransfer.SetTransferLocked") || !Native.HasMethod(world, "NationTransfer.CancelTransferConfirmation")
            || !Native.Has(world, "NationTransfer.transferRevision") || !Native.Has(world, "NationTransfer.transferNotice")) return;
        if (!NativeIdentity.Begin(window, "native_appearance")) return;
        var preview = Native.Get<LookPreview>(world, "NationTransfer.transferPreview");
        var editor = Native.Get<LookEditor>(world, "NationTransfer.transferEditor");
        var status = Native.Get<Label>(world, "NationTransfer.transferStatus");
        var accept = Native.Get<Button>(world, "NationTransfer.transferConfirm");
        var header = Native.Get<Label>(world, "NationTransfer.transferHeader");
        var list = Native.Get<VBoxContainer>(world, "NationTransfer.transferList");
        var layer = Native.Get<CanvasLayer>(world, "NationTransfer.transferLayer");
        if (preview == null || editor == null || status == null || accept == null || header == null || list == null
            || layer == null || list.GetParent() is not Control column || Game.Connection is not { } net) return;
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
        WatchNotices(world, layer);
        NativeWindows.Sync(window, () => SyncTransfer(world, editor, status));
        window.SetMeta("classic_appearance_controls", 1);
    }

    /// <summary>Opens the account only while no transfer is shown, pending or completed.</summary>
    private static void ReplaceOpen(WorldRoot world, IGameConnection net)
    {
        Action<IReadOnlyList<NationTransferCandidate>> open = candidates => OpenTransfer(world, candidates);
        var feature = Native.Get<object>(world, "NationTransfer");
        NativeHooks.Replace(Game.Session.NationTransfer, "NationTransferOpenEvent",
            handler => handler.Target == feature && handler.Method.Name == "OnOpen", open, world);
    }

    public static void OpenTransfer(WorldRoot world, IReadOnlyList<NationTransferCandidate> candidates)
    {
        if (Native.Get<bool>(world, "NationTransfer.transferInFlight") || TransferNotice(world) != null || Native.Get<bool>(world, "NationTransfer.transferCompleted")
            || Native.Get<bool>(world, "NationTransfer.transferShown"))
        {
            // The network layer already cached the ignored list; the shown account stays the one to submit.
            if (_accounts.TryGetValue(world, out var shown))
            {
                Native.Set(Game.Session.NationTransfer, "Candidates", shown.Candidates);
                Native.Set(Game.Session.NationTransfer, "Target", shown.Target);
            }
            return;
        }
        _accounts.AddOrUpdate(world, new Account(candidates, Game.Session.NationTransfer.Target));
        var editor = Native.Get<LookEditor>(world, "NationTransfer.transferEditor")!;
        var preview = Native.Get<LookPreview>(world, "NationTransfer.transferPreview")!;
        var list = Native.Get<VBoxContainer>(world, "NationTransfer.transferList")!;
        Native.Call(world, "NpcDialogWindow.Close");
        Native.Call(world, "NationTransfer.Open", candidates);
        foreach (var child in list.GetChildren())
            if (child.IsQueuedForDeletion()) list.RemoveChild(child);
        NativeLookEditor.SetLocked(editor, false);
        if (list.GetParent() is ScrollContainer scroll) scroll.ScrollVertical = 0;
        if (candidates.Count == 0)
        {
            Native.Get<Label>(world, "NationTransfer.transferHeader")!.Text = "No characters are available for nation transfer.";
            editor.Visible = false;
            LookFraming.Clear(preview);
            SetPreviewAvailable(preview, false);
            Native.Call(world, "NationTransfer.SetTransferStatus", "", false);
            Native.Get<Button>(world, "NationTransfer.transferConfirm")!.Disabled = true;
            Native.Get<HudWindow>(world, "NationTransfer.TransferPanel")!.Visible = true;
            Native.Set(world, "NationTransfer.transferShown", true);
            return;
        }
        editor.Visible = true;
        SetPreviewAvailable(preview, true);
        var picks = Native.Get<Dictionary<int, NationTransferPick>>(world, "NationTransfer.transferPicks")!;
        foreach (var candidate in candidates)
        {
            int faces = CharacterPreview.FaceCount(candidate.Race), hairs = CharacterPreview.HairCount(candidate.Race);
            int face = faces > 0 ? Mathf.Clamp(candidate.Face, 0, faces - 1) : candidate.Face;
            int style = hairs > 0 ? Mathf.Clamp(HairCode.StyleOf(candidate.Hair), 0, hairs - 1) : HairCode.StyleOf(candidate.Hair);
            picks[candidate.Slot] = new NationTransferPick(candidate.Slot, candidate.Name, candidate.Race, face, (style << 24) | (candidate.Hair & 0xffffff));
        }
        if (Native.Get<System.Collections.IList>(world, "NationTransfer.transferRows") is { } rows)
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
        Native.Call(world, "NationTransfer.SelectTransferCharacter", first);
        picks[first.Slot] = new NationTransferPick(first.Slot, first.Name, editor.Race, editor.Face, editor.Hair);
    }

    private static void SetPreviewAvailable(LookPreview preview, bool available)
    {
        preview.Visible = available;
        if (_turns.TryGetValue(preview, out var turns))
            foreach (var button in turns) button.Visible = available;
    }

    public static Notice? TransferNotice(WorldRoot world) => Native.Get<Notice>(world, "NationTransfer.transferNotice");

    /// <summary>Opens the certificate confirmation with the account's picks captured at this moment.</summary>
    public static void TransferPressed(WorldRoot world)
    {
        var picks = Native.Get<Dictionary<int, NationTransferPick>>(world, "NationTransfer.transferPicks");
        if (!Native.Get<bool>(world, "NationTransfer.transferShown") || Native.Get<bool>(world, "NationTransfer.transferInFlight") || TransferNotice(world) != null
            || picks is not { Count: > 0 } || Native.Get<bool>(world, "Avatar.Dead")) return;
        int revision = Native.Get<int>(world, "NationTransfer.transferRevision") + 1;
        Native.Set(world, "NationTransfer.transferRevision", revision);
        var captured = picks.Values.OrderBy(pick => pick.Slot).ToArray();
        Native.Call(world, "NationTransfer.SetTransferLocked", true);
        if (Native.Get<LookEditor>(world, "NationTransfer.transferEditor") is { } editor) NativeLookEditor.SetLocked(editor, true);
        string nation = Nations.Name(Native.Get<int>(world, "NationTransfer.transfer"));
        var layer = Native.Get<Node>(world, "NationTransfer.transferLayer") ?? world;
        var notice = Notice.Confirm(layer,
            $"All {captured.Length} characters move to {nation}.\nYour {ItemData.DisplayName(NationTransferCertificate).Trim()} will be used.\nYou will return to character selection.",
            "Transfer", "Cancel", () => SendTransfer(world, revision, captured),
            () => Native.Call(world, "NationTransfer.CancelTransferConfirmation", revision), title: TransferTitle);
        Native.Set(world, "NationTransfer.transferNotice", notice);
    }

    private static void SendTransfer(WorldRoot world, int revision, NationTransferPick[] picks)
    {
        if (Native.Get<bool>(world, "Avatar.Dead") && revision == Native.Get<int>(world, "NationTransfer.transferRevision") && TransferNotice(world) != null
            && !Native.Get<bool>(world, "NationTransfer.transferInFlight"))
        {
            Native.Set(world, "NationTransfer.transferNotice", null);
            Native.Call(world, "NationTransfer.SetTransferLocked", false);
            return;
        }
        Native.Call(world, "NationTransfer.Send", revision, (IReadOnlyList<NationTransferPick>)picks);
    }

    private static void SyncTransfer(WorldRoot world, LookEditor editor, Label status)
    {
        bool locked = Native.Get<bool>(world, "NationTransfer.transferInFlight") || TransferNotice(world) != null;
        if (NativeLookEditor.Locked(editor) != locked) NativeLookEditor.SetLocked(editor, locked);
        if (status.Text.Contains(CertificateTypo, StringComparison.OrdinalIgnoreCase))
            status.Text = status.Text.Replace(CertificateTypo, CertificateText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Transfer messages belong to the transfer layer, not the world behind it. The World's children are checked when
    /// they change (once per change burst, before the frame draws, when the notice's request is set) instead of every
    /// frame; one check covers the notices that are already there. The World's child list signal also fires while the
    /// World is outside the scene tree, as in the preview fixtures.
    /// </summary>
    private static void WatchNotices(WorldRoot world, CanvasLayer layer)
    {
        bool queued = false;
        Action changed = () =>
        {
            if (queued) return;
            queued = true;
            Callable.From(() => { queued = false; Adopt(world, layer); }).CallDeferred();
        };
        NativeHooks.Listen("World.ChildOrderChanged(transfer notices)", () => world.ChildOrderChanged += changed,
            () => { if (GodotObject.IsInstanceValid(world)) world.ChildOrderChanged -= changed; }, world);
        Adopt(world, layer);
    }

    private static void Adopt(WorldRoot world, CanvasLayer layer)
    {
        if (!Plugin.Active || !GodotObject.IsInstanceValid(world)) return;
        foreach (var notice in world.GetChildren().OfType<Notice>()) Adopt(notice, layer);
    }

    private static void Adopt(Notice notice, CanvasLayer layer)
    {
        if (!Plugin.Active || !GodotObject.IsInstanceValid(notice) || !GodotObject.IsInstanceValid(layer)) return;
        if (notice.IsQueuedForDeletion() || notice.GetParent() == layer || notice.GetParent() is not WorldRoot) return;
        if (Native.Get<DialogRequest>(notice, "request")?.Title == TransferTitle) notice.Reparent(layer, false);
    }

    private static bool NameCommon(LookPreview preview, LookEditor editor, Label status, Button accept)
    {
        var cancel = accept.GetParent()?.GetChildren().OfType<Button>().FirstOrDefault(b => b != accept);
        var turns = preview.GetParent()?.GetChildren().OfType<HBoxContainer>().FirstOrDefault()?.GetChildren().OfType<Button>().ToArray();
        if (cancel == null || turns is not { Length: 2 or 3 }) return false;
        preview.Name = "look_preview"; editor.Name = "look_editor"; status.Name = "look_status";
        accept.Name = "look_accept"; cancel.Name = "look_cancel";
        turns[0].Name = "look_turn_left"; turns[1].Name = "look_turn_right";
        if (turns.Length == 3)
        {
            // The Beauty Shop's zoom keeps the Classic framing when it zooms back out.
            turns[2].Name = "look_zoom";
            NativeIdentity.Rewire(turns[2], () => LookFraming.ToggleZoom(preview));
        }
        _turns.AddOrUpdate(preview, turns);
        return true;
    }
}
