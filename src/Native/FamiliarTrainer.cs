using System.Collections;
using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Completes the native familiar trainer ("pethatch") with the original incubation and transformation
/// layout: three stage cells filled by click, right-click or drag from a 28-slot inventory, a confirmation
/// that freezes the selected records and the name, a revision check before sending, and the trainer's
/// Enter and Escape keys. The native window keeps its state fields; the trainer reads and writes them so
/// the client's own reply, refusal and reset handling stays authoritative.
/// </summary>
public sealed partial class FamiliarTrainer : Node
{
    private const int NoPick = -1;
    private const int HatchTab = 0;
    private const int TransformTab = 1;
    private const string SelectionChanged = "Your selection changed. Please choose the items again.";
    private const string NotSent = "Not connected. Please try again after reconnecting.";
    private static readonly string[] SlotFields = { "PetHatch.slot", "PetHatch.petTransformSlot", "PetHatch.petScrollSlot" };
    private static readonly ConditionalWeakTable<WorldRoot, FamiliarTrainer> Trainers = new();

    private readonly WorldRoot _world;
    private readonly HudWindow _window;
    private readonly ItemSlotView[] _stages = new ItemSlotView[3];
    private readonly ItemSlotView[] _inventory = new ItemSlotView[Inventory.GridCount];
    private readonly int[] _selected = { NoPick, NoPick, NoPick };
    private ServiceTabs _tabs = null!;
    private LineEdit _name = null!;
    private Button _accept = null!;
    private Label _description = null!;
    private IGameConnection? _net;
    private int _revision;
    private bool _wasVisible;

    /// <summary>The confirmation that is waiting for an answer, if any.</summary>
    public Notice? Notice { get; private set; }

    private FamiliarTrainer(WorldRoot world, HudWindow window) { _world = world; _window = window; Name = "familiar_trainer"; }

    public FamiliarTrainer() { _world = null!; _window = null!; }

    public static FamiliarTrainer? Of(WorldRoot world) =>
        Trainers.TryGetValue(world, out var trainer) && IsInstanceValid(trainer) ? trainer : null;

    internal static void Prepare(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("classic_pet_hatch_controls") || Of(world) != null) return;
        foreach (string field in new[] { "PetHatch.tabs", "PetHatch.page", "PetHatch.petTransformPage", "PetHatch.NameInput", "PetHatch.btn", "PetHatch.status",
                     "PetHatch.eggPick", "PetHatch.petTransformPetPick", "PetHatch.petTransformScrollPick" })
            if (Native.Get<Node>(world, field) == null) return;
        if (!Native.Has(world, "PetHatch.inFlight") || !Native.HasMethod(Native.StaticOwner("LibreKO.Features.Windows.PetHatchFeature"), "IsPetEgg")) return;
        var trainer = new FamiliarTrainer(world, window);
        trainer.Build();
        Trainers.AddOrUpdate(world, trainer);
        window.AddChild(trainer);
        window.SetMeta("classic_pet_hatch_controls", 1);
        NativeWindows.Sync(window, trainer.Tick);
    }

    private T Field<T>(string name) where T : class => Native.Get<T>(_world, name)!;
    private Inventory Inv => Game.Session.Self.Inventory;
    private bool Shown => Native.Get<bool>(_world, "PetHatch.shown");
    private bool InFlight => Native.Get<bool>(_world, "PetHatch.inFlight");
    private bool SelfDead => Native.Get<bool>(_world, "Avatar.Dead");
    private bool Transforming => _tabs.Current == TransformTab;

    /// <summary>Whether the draft may change: the trainer is open, nothing is pending and the owner is alive.</summary>
    public bool Editing => Shown && !InFlight && Notice == null && !SelfDead;

    private void Build()
    {
        _tabs = Field<ServiceTabs>("PetHatch.tabs"); _tabs.Name = "pet_hatch_tabs";
        _name = Field<LineEdit>("PetHatch.NameInput"); _name.Name = "pet_hatch_name";
        _accept = Field<Button>("PetHatch.btn"); _accept.Name = "pet_hatch_accept";
        var status = Field<Label>("PetHatch.status"); status.Name = "pet_hatch_status";
        foreach (var button in _accept.GetParent().GetChildren().OfType<Button>())
            if (button != _accept && button.Text == "Close") button.Name = "pet_hatch_cancel";
        _window.Title = "Familiar";
        var root = _window.Body;
        var hatchPage = Field<VBoxContainer>("PetHatch.page"); hatchPage.Name = "pet_hatch_page";
        var transformPage = Field<VBoxContainer>("PetHatch.petTransformPage"); transformPage.Name = "pet_transform_page";
        var picks = new[] { Field<Label>("PetHatch.eggPick"), Field<Label>("PetHatch.petTransformPetPick"), Field<Label>("PetHatch.petTransformScrollPick") };
        foreach (var child in hatchPage.GetChildren().Concat(transformPage.GetChildren()).OfType<CanvasItem>())
            if (child != _name) child.Visible = false;

        _description = UiTheme.Text("", 13, UiTheme.TextHi); _description.Name = "pet_hatch_description";
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_description); root.MoveChild(_description, _tabs.GetIndex() + 1);
        BuildStage(hatchPage, 0, "Egg", picks[0]);
        hatchPage.MoveChild(hatchPage.GetChild(hatchPage.GetChildCount() - 1), 0);
        var caption = UiTheme.Text("Familiar name", 13, UiTheme.GoldBright); caption.Name = "pet_hatch_name_caption";
        hatchPage.AddChild(caption); hatchPage.MoveChild(caption, _name.GetIndex());
        var stages = new HBoxContainer(); transformPage.AddChild(stages);
        BuildStage(stages, 1, "Familiar", picks[1]);
        BuildStage(stages, 2, "Transformation scroll", picks[2]);
        var note = UiTheme.Text("The scroll is used up to change the familiar's form.", 13, UiTheme.TextDim);
        note.Name = "pet_transform_note"; note.AutowrapMode = TextServer.AutowrapMode.WordSmart; transformPage.AddChild(note);
        var heading = UiTheme.SectionTitle("Inventory"); heading.Name = "pet_hatch_inventory_heading";
        root.AddChild(heading); root.MoveChild(heading, transformPage.GetIndex() + 1);
        var grid = new GridContainer { Name = "pet_hatch_inventory", Columns = Inventory.GridCount / 4 };
        grid.AddThemeConstantOverride("h_separation", 4); grid.AddThemeConstantOverride("v_separation", 4);
        root.AddChild(grid); root.MoveChild(grid, heading.GetIndex() + 1);
        for (int i = 0; i < _inventory.Length; i++)
        {
            var cell = new ItemSlotView(PickSize) { Name = "pet_hatch_inventory_" + i, Index = Inventory.GridStart + i };
            _inventory[i] = cell; grid.AddChild(cell);
            cell.Clicked += c => Select(c.Index); cell.RightClicked += c => Select(c.Index);
            cell.DragOut = c => Editing && StageFor(c.Index) >= 0
                ? (Variant)new Godot.Collections.Dictionary { ["invFrom"] = c.Index } : default(Variant);
            cell.Hovered += c => ShowTooltip(c.Index, c.Item); cell.Unhovered += _ => HideTooltip();
        }

        Rewire(_accept, BaseButton.SignalName.Pressed, Callable.From(RequestConfirmation));
        Rewire(_name, LineEdit.SignalName.TextSubmitted, Callable.From<string>(_ => RequestConfirmation()));
        _tabs.Selected += _ => { Rebuild(); Refresh(); };
        _window.VisibilityChanged += OnVisibilityChanged;
        _wasVisible = _window.Visible;
        for (int stage = 0; stage < _selected.Length; stage++) _selected[stage] = Native.Get<int>(_world, SlotFields[stage]);
        Observe(Game.Connection);
        Rebuild(); Refresh();
    }

    private const float PickSize = 44f;

    private void BuildStage(Container parent, int stage, string caption, Label pick)
    {
        var section = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; parent.AddChild(section);
        var heading = UiTheme.Text(caption, 13, UiTheme.GoldBright); heading.Name = "pet_hatch_stage_heading_" + stage;
        section.AddChild(heading);
        var cell = new ItemSlotView(PickSize) { Name = "pet_hatch_stage_" + stage, Index = NoPick };
        _stages[stage] = cell; section.AddChild(cell);
        cell.CanDrop = (_, data) => DropSlot(stage, data) >= 0;
        cell.Dropped = (_, data) => { int abs = DropSlot(stage, data); if (abs >= 0) Select(abs); };
        cell.RightClicked += _ => Clear(stage);
        cell.Hovered += c => { if (c.Index >= Inventory.GridStart && c.Index < Inv.Length) ShowTooltip(c.Index, c.Item); };
        cell.Unhovered += _ => HideTooltip();
        pick.Name = "pet_hatch_pick_" + stage; pick.Visible = true; pick.AddThemeFontSizeOverride("font_size", 13);
        pick.AutowrapMode = TextServer.AutowrapMode.WordSmart; pick.Reparent(section, false);
    }

    /// <summary>Replaces the client's direct send on a signal with the trainer's confirmation.</summary>
    private void Rewire(GodotObject source, StringName signal, Callable replacement)
    {
        var feature = Native.Get<object>(_world, "PetHatch");
        foreach (var connection in source.GetSignalConnectionList(signal))
        {
            var callable = connection["callable"].AsCallable();
            if (callable.Delegate?.Target is { } target && (target == _world || target == feature)) source.Disconnect(signal, callable);
        }
        source.Connect(signal, replacement);
    }

    public override void _ExitTree() { Observe(null); Dismiss(); }

    private void Observe(IGameConnection? net)
    {
        if (_net == net && _observing is { Active: true }) return;
        _observing?.Release();
        _observing = null;
        _net = net;
        if (net == null) return;
        _observing = NativeHooks.Listen("Net.Pet (familiar trainer)", () =>
        {
            Game.Session.Items.InventorySlotEvent += OnInventorySlot; Game.Session.Items.InventoryGridRefreshEvent += OnInventoryGrid;
            Game.Session.Pet.PetHatchedEvent += OnResult; Game.Session.Pet.PetTransformedEvent += OnResult;
        }, () =>
        {
            Game.Session.Items.InventorySlotEvent -= OnInventorySlot; Game.Session.Items.InventoryGridRefreshEvent -= OnInventoryGrid;
            Game.Session.Pet.PetHatchedEvent -= OnResult; Game.Session.Pet.PetTransformedEvent -= OnResult;
        });
    }

    private NativeHooks.Hook? _observing;

    private void OnInventorySlot(int abs, ItemSlot _)
    {
        if (Shown && abs >= Inventory.GridStart && abs < Inventory.GridStart + Inventory.GridCount) { Rebuild(); Refresh(); }
    }

    private void OnInventoryGrid(ItemSlot[] _) { if (Shown) { Rebuild(); Refresh(); } }

    private void OnResult(int _, PetItemInfo __) { Rebuild(); Refresh(); }

    private void OnVisibilityChanged()
    {
        bool visible = _window.Visible;
        if (visible == _wasVisible) return;
        _wasVisible = visible;
        if (!visible) { Dismiss(); Refresh(); return; }
        // A fresh trainer visit starts with empty stage cells; a pending operation keeps its draft.
        if (!InFlight) for (int stage = 0; stage < _selected.Length; stage++) _selected[stage] = NoPick;
        Rebuild(); Refresh();
    }

    private void Tick()
    {
        if (!IsInstanceValid(_world)) return;
        if (_net != Game.Connection) Observe(Game.Connection);
        Rebuild(); Refresh();
    }

    private bool Matches(int stage, ItemSlot slot) => Native.Call(_world, stage switch
    {
        0 => "IsPetEgg",
        1 => "IsFamiliarItem",
        _ => "IsTransformScroll",
    }, slot) is true;

    private int StageFor(int abs)
    {
        var inv = Inv;
        if (abs < Inventory.GridStart || abs >= Inventory.GridStart + Inventory.GridCount || abs >= inv.Length) return NoPick;
        if (!Transforming) return Matches(0, inv[abs]) ? 0 : NoPick;
        return Matches(1, inv[abs]) ? 1 : Matches(2, inv[abs]) ? 2 : NoPick;
    }

    private int DropSlot(int stage, Variant data)
    {
        if (!Editing || data.VariantType != Variant.Type.Dictionary) return NoPick;
        var values = data.AsGodotDictionary();
        if (!values.ContainsKey("invFrom") || values["invFrom"].VariantType != Variant.Type.Int) return NoPick;
        int abs = values["invFrom"].AsInt32();
        return StageFor(abs) == stage ? abs : NoPick;
    }

    /// <summary>Stages an inventory item in the cell that accepts its kind on the current tab.</summary>
    public void Select(int abs)
    {
        if (!Editing) return;
        int stage = StageFor(abs);
        if (stage < 0) return;
        _selected[stage] = abs;
        Native.Call(_world, "PetHatch.SetStatus", "", false);
        Rebuild(); Refresh();
    }

    private void Clear(int stage)
    {
        if (!Editing) return;
        _selected[stage] = NoPick;
        Rebuild(); Refresh(); HideTooltip();
    }

    /// <summary>
    /// Keeps a staged item only while the same slot still holds an eligible item, mirrors the selection into
    /// the native trainer and redraws the stage and inventory cells.
    /// </summary>
    public void Rebuild()
    {
        var inv = Inv;
        bool changed = false;
        for (int stage = 0; stage < _selected.Length; stage++)
        {
            int abs = _selected[stage];
            if (abs != NoPick && (abs < Inventory.GridStart || abs >= Inventory.GridStart + Inventory.GridCount || abs >= inv.Length || !Matches(stage, inv[abs])))
                _selected[stage] = abs = NoPick;
            if (Native.Get<int>(_world, SlotFields[stage]) == abs) continue;
            Native.Set(_world, SlotFields[stage], abs); changed = true;
        }
        if (changed) Native.Call(_world, "PetHatch.RefreshPetPickLooks");
        for (int stage = 0; stage < _stages.Length; stage++)
        {
            int abs = _selected[stage];
            _stages[stage].Index = abs;
            Show(_stages[stage], abs >= Inventory.GridStart && abs < inv.Length ? inv[abs] : default);
        }
        bool transforming = Transforming;
        for (int i = 0; i < _inventory.Length; i++)
        {
            var cell = _inventory[i];
            Show(cell, Inventory.GridStart + i < inv.Length ? inv[Inventory.GridStart + i] : default);
            bool selected = transforming ? cell.Index == _selected[1] || cell.Index == _selected[2] : cell.Index == _selected[0];
            var look = selected ? SlotLook.Selected : SlotLook.Normal;
            if (cell.Look != look) cell.Look = look;
        }
        _description.Text = transforming ? "Transform your familiar with a scroll." : "Incubate your familiar egg.";
    }

    private static void Show(ItemSlotView cell, ItemSlot item) { if (!cell.Item.Equals(item)) cell.Set(item); }

    /// <summary>Applies the trainer's editing state to the native action, name field and tabs.</summary>
    public void Refresh()
    {
        bool editing = Editing;
        var inv = Inv;
        bool Valid(int stage) => _selected[stage] >= 0 && _selected[stage] < inv.Length && Matches(stage, inv[_selected[stage]]);
        bool ready = Transforming ? Valid(1) && Valid(2)
            : Valid(0) && Native.Call(Native.StaticOwner("LibreKO.Features.Windows.PetHatchFeature"), "IsValidPetName", _name.Text) is true;
        if (_accept.Disabled != (!editing || !ready)) _accept.Disabled = !editing || !ready;
        if (_name.Editable != editing) _name.Editable = editing;
        // Skins may move the tab buttons out of the tab strip; the strip still owns them.
        foreach (var tab in Native.Get<List<Button>>(_tabs, "buttons") ?? _tabs.GetChildren().OfType<Button>().ToList())
            if (tab.Disabled != !editing) tab.Disabled = !editing;
    }

    /// <summary>Freezes the current selection and name and asks the player to confirm the operation.</summary>
    public void RequestConfirmation()
    {
        Rebuild(); Refresh();
        if (_accept.Disabled || !Editing) return;
        bool transform = Transforming;
        int slot = transform ? _selected[1] : _selected[0];
        int scroll = _selected[2], npc = Native.Get<int>(_world, "PetHatch.npc");
        var inv = Inv;
        var item = inv[slot]; var material = transform ? inv[scroll] : default;
        string name = _name.Text;
        int revision = ++_revision;
        Notice = Notice.Confirm(Native.Get<Node>(_world, "PetHatch.layer") ?? _window,
            transform ? "Would you like to transform this familiar?\nThe transformation scroll will be used."
                : $"Would you like to incubate the egg?\nFamiliar name: {name}",
            "Yes", "No", () => Submit(revision, transform, npc, slot, item, scroll, material, name),
            () => Cancel(revision), title: transform ? "Familiar Transformation" : "Familiar Hatching");
        Refresh();
    }

    private void Submit(int revision, bool transform, int npc, int slot, ItemSlot item, int scroll, ItemSlot material, string name)
    {
        if (revision != _revision || !Shown || Notice == null || InFlight) return;
        Notice = null;
        var inv = Inv;
        bool same = slot >= Inventory.GridStart && slot < inv.Length && inv[slot].Equals(item)
            && (transform ? Matches(1, inv[slot]) && scroll >= Inventory.GridStart && scroll < inv.Length
                && inv[scroll].Equals(material) && Matches(2, inv[scroll])
                : Matches(0, inv[slot]) && Native.Call(Native.StaticOwner("LibreKO.Features.Windows.PetHatchFeature"), "IsValidPetName", name) is true);
        if (SelfDead || !same)
        {
            Rebuild(); Native.Call(_world, "PetHatch.SetStatus", SelectionChanged, true); Refresh();
            return;
        }
        Native.Set(_world, "PetHatch.inFlight", true);
        bool sent = transform
            ? SendTransform(npc, item, slot - Inventory.GridStart, material.ItemId, scroll - Inventory.GridStart)
            : Send(() => Game.Requests.Pet.Hatch(npc, item.ItemId, slot - Inventory.GridStart, name));
        if (!sent) { Native.Set(_world, "PetHatch.inFlight", false); Native.Call(_world, "PetHatch.SetStatus", NotSent, true); }
        else Native.Call(_world, "PetHatch.SetStatus", transform ? "Transforming…" : "Hatching…", false);
        Refresh();
    }

    private static bool Send(Func<bool> send) => Game.Connection.Connected && send();

    private static bool SendTransform(int npc, ItemSlot familiar, int petSlot, int materialItemId, int materialSlot) =>
        Send(() => Game.Requests.Pet.Transform(npc, familiar.ItemId, petSlot, (int)familiar.UniqueId, materialItemId, materialSlot));

    private void Cancel(int revision)
    {
        if (revision != _revision || Notice == null) return;
        Notice = null; Refresh();
    }

    private void Dismiss()
    {
        _revision++;
        if (Notice is { } notice && IsInstanceValid(notice)) notice.Close();
        Notice = null;
    }

    private void ShowTooltip(int abs, ItemSlot item) => FamiliarWindow.ShowItemTooltip(_world, abs, item);
    private void HideTooltip() => Native.Call(_world, "ItemTooltip.Hide");

    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode is not (Key.Enter or Key.KpEnter or Key.Escape)) return;
        if (!Shown || !_window.IsVisibleInTree() || Notice != null) return;
        // Keys the world gives to HUD editing, active chat and a focused whisper keep that precedence.
        if (Native.Has(_world, "Chat") && Native.Get<object>(_world, "Chat") is { } chat && Native.Get<bool>(chat, "IsActive")) return;
        var focus = GetViewport().GuiGetFocusOwner();
        if (key.Keycode == Key.Escape && WhisperOwns(focus)) return;
        if (focus is LineEdit or TextEdit or SpinBox && !_window.IsAncestorOf(focus)) return;
        GetViewport().SetInputAsHandled();
        if (key.Keycode == Key.Escape) Native.Call(_world, "PetHatch.Close");
        else RequestConfirmation();
    }

    private bool WhisperOwns(Control? focus)
    {
        if (focus == null || Native.Get<IDictionary>(_world, "Whispers.Windows") is not { } whispers) return false;
        for (Node? node = focus; node != null; node = node.GetParent())
        {
            if (node is not HudWindow window) continue;
            foreach (var chat in whispers.Values)
                if (chat != null && Native.Get<HudWindow>(chat, "Window") == window && !window.Minimized) return true;
        }
        return false;
    }
}
