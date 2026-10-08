using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Completes the native familiar window ("pet") with the original familiar window content: the portrait,
/// the Ability/Items/Skills pages, attack, defence and the six resistances from the native sheet, and the
/// sixteen familiar skills over two pages. Native controls keep their behaviour and receive the names the
/// Classic skin looks for. Skills are cast through the client's own familiar skill path.
/// </summary>
public sealed partial class FamiliarWindow : Node
{
    private static readonly string[] Captions = { "Attack", "Defense", "Flame", "Glacier", "Lightning", "Magic", "Curse", "Poison" };
    private static readonly ConditionalWeakTable<World, FamiliarWindow> Windows = new();

    private readonly World _world;
    private readonly HudWindow _window;
    private readonly Label[] _details = new Label[Captions.Length];
    private readonly FamiliarSkillButton[] _skills = new FamiliarSkillButton[PetSkills.SlotsPerPage];
    private readonly ItemSlotView[] _bag;
    private FamiliarPortraitView _portrait = null!;
    private Label _skillPage = null!;
    private Button _previous = null!, _next = null!;
    private List<int> _skillIds = new();
    private int _page;
    private int _class = -1;
    private Net? _net;

    private FamiliarWindow(World world, HudWindow window, ItemSlotView[] bag)
    {
        _world = world; _window = window; _bag = bag; Name = "familiar_window";
    }

    public FamiliarWindow() { _world = null!; _window = null!; _bag = Array.Empty<ItemSlotView>(); }

    /// <summary>The familiar window controller of a World, when its window has been prepared.</summary>
    public static FamiliarWindow? Of(World world) =>
        Windows.TryGetValue(world, out var window) && IsInstanceValid(window) ? window : null;

    internal static void Prepare(HudWindow window, World world)
    {
        if (window.HasMeta("classic_pet_controls") || Of(world) != null) return;
        var bag = Native.Get<ItemSlotView[]>(world, "_petBagCells");
        bool named = bag != null && bag.Length == PetSheet.InventorySize
            & NativeWindows.Name(NativeWindows.Control<Label>(world, "_petNameLbl"), "pet_name")
            & NativeWindows.Name(NativeWindows.Control<Label>(world, "_petLevelLbl"), "pet_level")
            & NativeWindows.Name(NativeWindows.Control<StatBar>(world, "_petHpBar"), "pet_hp")
            & NativeWindows.Name(NativeWindows.Control<StatBar>(world, "_petMpBar"), "pet_mp")
            & NativeWindows.Name(NativeWindows.Control<StatBar>(world, "_petExpBar"), "pet_exp")
            & NativeWindows.Name(NativeWindows.Control<StatBar>(world, "_petSatBar"), "pet_satisfaction")
            & NativeWindows.Name(NativeWindows.Control<Button>(world, "_petAttackBtn"), "pet_attack")
            & NativeWindows.Name(NativeWindows.Control<Button>(world, "_petDefendBtn"), "pet_defend")
            & NativeWindows.Name(NativeWindows.Control<Button>(world, "_petLootBtn"), "pet_loot")
            & NativeWindows.Name(NativeWindows.Control<Button>(world, "_petFeedBtn"), "pet_feed")
            & NativeWindows.Name(NativeWindows.Control<Button>(world, "_petDismissBtn"), "pet_dismiss")
            & NativeWindows.Name(NativeWindows.Control<Label>(world, "_petStatus"), "pet_status");
        if (!named) return;
        var controller = new FamiliarWindow(world, window, bag!);
        controller.Build();
        Windows.AddOrUpdate(world, controller);
        window.AddChild(controller);
        window.SetMeta("classic_pet_controls", 1);
        NativeWindows.Sync(window, controller.Tick);
    }

    private void Build()
    {
        SkillData.EnsureLoaded();
        for (int i = 0; i < _bag.Length; i++)
        {
            _bag[i].Name = "pet_item_" + i;
            _bag[i].Hovered += ShowBagTooltip;
            _bag[i].Unhovered += _ => Native.Call(_world, "HideItemTooltip");
        }
        _window.VisibilityChanged += () => { if (!_window.Visible) Native.Call(_world, "HideItemTooltip"); };

        var root = _window.Body;
        var oldChildren = root.GetChildren();
        var name = NativeWindows.Control<Label>(_world, "_petNameLbl")!;
        var attack = NativeWindows.Control<Button>(_world, "_petAttackBtn")!;
        var status = NativeWindows.Control<Label>(_world, "_petStatus")!;
        _portrait = new FamiliarPortraitView { Name = "pet_portrait", CustomMinimumSize = new Vector2(80, 80), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_portrait);
        var tabs = new ServiceTabs { Name = "pet_pages" };
        tabs.SetTabs(new[] { "Ability", "Items", "Skills" }, 0); root.AddChild(tabs);
        var pages = new VBoxContainer[3];
        for (int i = 0; i < pages.Length; i++) { pages[i] = new VBoxContainer { Name = "pet_page_" + i, Visible = i == 0 }; root.AddChild(pages[i]); }
        foreach (string field in new[] { "_petHpBar", "_petMpBar", "_petExpBar", "_petSatBar" })
            NativeWindows.Control<StatBar>(_world, field)!.GetParent().Reparent(pages[0]);
        _bag[0].GetParent().Reparent(pages[1]);
        NativeWindows.Control<Button>(_world, "_petFeedBtn")!.GetParent().Reparent(pages[1]);
        foreach (var node in oldChildren)
            if (node.GetParent() == root && node != name.GetParent() && node != attack.GetParent() && node != status)
            { root.RemoveChild(node); node.QueueFree(); }
        root.MoveChild(status, root.GetChildCount() - 1);
        tabs.Selected += page => { for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == page; };

        var stats = new GridContainer { Columns = 2, Name = "pet_details" };
        pages[0].AddChild(stats);
        for (int i = 0; i < Captions.Length; i++)
        {
            var label = UiTheme.Text(Captions[i] + "  —", 13, UiTheme.TextHi);
            label.Name = "pet_detail_" + i; stats.AddChild(label); _details[i] = label;
        }
        var skills = new GridContainer { Columns = 4, Name = "pet_skills" }; pages[2].AddChild(skills);
        for (int i = 0; i < _skills.Length; i++)
        {
            int index = i;
            var button = new FamiliarSkillButton { Name = "pet_skill_" + i };
            button.UseActionStyle();
            button.FocusMode = Control.FocusModeEnum.None;
            button.CustomMinimumSize = new Vector2(44, 44);
            button.Pressed += () => Use(index);
            skills.AddChild(button); _skills[i] = button;
        }
        var paging = new HBoxContainer(); pages[2].AddChild(paging);
        _previous = UiTheme.SmallButton("<", "Previous skills"); _previous.Name = "pet_skill_previous";
        _next = UiTheme.SmallButton(">", "Next skills"); _next.Name = "pet_skill_next";
        _skillPage = UiTheme.Text("1/1", 13, UiTheme.TextHi); _skillPage.Name = "pet_skill_page";
        _previous.Pressed += () => { _page = Math.Max(0, _page - 1); Refresh(); };
        _next.Pressed += () => { _page++; Refresh(); };
        paging.AddChild(_previous); paging.AddChild(_skillPage); paging.AddChild(_next);
        Observe(Net.I);
        Refresh();
    }

    public override void _ExitTree() => Observe(null);

    /// <summary>Follows the familiar sheet notifications of the active network session.</summary>
    private void Observe(Net? net)
    {
        if (_net == net) return;
        if (_net != null)
        {
            _net.PetSummonedEvent -= OnSummoned; _net.PetGoneEvent -= Refresh; _net.PetVitalsEvent -= Refresh;
            _net.PetModeEvent -= OnMode; _net.PetExpEvent -= OnExp; _net.MagicEvent -= OnMagic;
        }
        _net = net;
        if (_net == null) return;
        _net.PetSummonedEvent += OnSummoned; _net.PetGoneEvent += Refresh; _net.PetVitalsEvent += Refresh;
        _net.PetModeEvent += OnMode; _net.PetExpEvent += OnExp; _net.MagicEvent += OnMagic;
    }

    private void OnSummoned(PetSheet _) => Refresh();
    private void OnMode(int _) => Refresh();
    private void OnExp(long _) => Refresh();

    /// <summary>A refused or cancelled familiar cast releases its cooldown on the client's familiar timers.</summary>
    private void OnMagic(int sub, int skillId, int casterId, int targetId, short[] data)
    {
        if (sub is MagicSub.Fail or MagicSub.Cancel) RefreshCooldowns(Now());
    }

    private bool SelfDead => Native.Get<bool>(_world, "_selfDead");

    private void ShowBagTooltip(ItemSlotView cell)
    {
        if (Net.I.Pet is { } pet && cell.Index >= 0 && cell.Index < pet.Items.Length && !pet.Items[cell.Index].IsEmpty)
            ShowItemTooltip(_world, -1, pet.Items[cell.Index]);
    }

    /// <summary>Shows the client's item tooltip; the tooltip never takes the pointer from neighbouring cells.</summary>
    internal static void ShowItemTooltip(World world, int abs, ItemSlot item)
    {
        Native.Call(world, "ShowItemTooltip", abs, item, "");
        if (Native.Get<Control>(world, "_itemTipPanel") is not { } tooltip) return;
        tooltip.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var control in Native.Descendants(tooltip).OfType<Control>()) control.MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    private void Use(int index)
    {
        int skill = _page * PetSkills.SlotsPerPage + index;
        if (!SelfDead && skill < _skillIds.Count) Native.Call(_world, "UsePetSkill", _skillIds[skill]);
        RefreshCooldowns(Now());
    }

    /// <summary>The client's familiar tick: the portrait and cooldowns follow the World's own processing.</summary>
    private void Tick()
    {
        if (!IsInstanceValid(_world) || !_world.CanProcess()) return;
        if (_net != Net.I) Observe(Net.I);
        RefreshPortrait();
        RefreshCooldowns(Now());
    }

    private double Now() => Native.Call(_world, "Now") is double now ? now : Time.GetTicksMsec() / 1000d;

    /// <summary>Shows the native familiar sheet: portrait, attack, defence, resistances and the skill page.</summary>
    public void Refresh() => Refresh(Net.I.Pet);

    /// <summary>Shows the given familiar sheet, as the client does when it presents a sheet.</summary>
    public void Refresh(PetSheet? pet)
    {
        if (!IsInstanceValid(_world) || _details[0] == null) return;
        if (pet == null) _portrait.SetAppearance(null); else RefreshPortrait();
        for (int i = 0; i < _details.Length; i++)
        {
            int value = pet == null ? 0 : i == 0 ? pet.Attack : i == 1 ? pet.Defence : pet.Resists[i - 2];
            _details[i].Text = Captions[i] + "  " + (pet == null ? "—" : value.ToString());
        }
        int petClass = pet?.Class ?? -1;
        if (_class != petClass)
        {
            _class = petClass; _page = 0;
            _skillIds = pet == null ? new List<int>() : PetSkills.BarSkills(SkillData.All.Select(s => (s.Id, s.Tree)), petClass);
        }
        int pages = Math.Max(1, (_skillIds.Count + PetSkills.SlotsPerPage - 1) / PetSkills.SlotsPerPage);
        _page = Math.Clamp(_page, 0, pages - 1);
        _skillPage.Text = $"{_page + 1}/{pages}";
        _previous.Disabled = _page == 0;
        _next.Disabled = _page + 1 >= pages;
        bool dead = SelfDead;
        for (int i = 0; i < _skills.Length; i++)
        {
            int index = _page * PetSkills.SlotsPerPage + i;
            var skill = index < _skillIds.Count ? SkillData.Get(_skillIds[index]) : null;
            bool locked = pet == null || skill == null || !PetSkills.Unlocked(skill.Level, pet.Level);
            var button = _skills[i];
            button.SkillId = skill?.Id ?? 0;
            button.Icon = skill == null ? null : locked ? SkillData.EnigmaIcon() : SkillData.Icon(skill.Id);
            button.Disabled = dead || locked || pet!.Mp < skill!.Msp;
            string tooltip = skill == null ? "" : Native.Call(_world, "SkillTooltip", skill) as string ?? skill.Name;
            button.TooltipText = skill == null ? "" : tooltip + (locked ? $"\nFamiliar level {skill.Level}" : "");
        }
        RefreshCooldowns(Now());
    }

    /// <summary>Shades and disables the skills that are cooling down on the shared familiar timers.</summary>
    public void RefreshCooldowns(double now)
    {
        var pet = Net.I.Pet;
        bool dead = SelfDead;
        foreach (var button in _skills)
        {
            if (button == null) continue;
            var skill = SkillData.Get(button.SkillId);
            float cooldown = button.SkillId == 0 ? 0 : Native.Call(_world, "PetCooldown", button.SkillId, now) is float value ? value : 0;
            button.SetCooldown(cooldown);
            button.Disabled = dead || pet == null || skill == null
                || !PetSkills.Unlocked(skill.Level, pet.Level) || pet.Mp < skill.Msp || cooldown > 0;
        }
    }

    /// <summary>Publishes the summoned familiar's appearance; the model is built only when the cache needs it.</summary>
    public void RefreshPortrait()
    {
        if (Native.Call(_world, "MyPetEntity") is not { } actor) { _portrait.SetAppearance(null); return; }
        int modelId = Native.Get<int>(actor, "ModelId");
        string appearance = "familiar:" + modelId;
        if (_portrait.Appearance?.AppearanceKey == appearance) return;
        var world = _world;
        _portrait.SetAppearance(new GameNpcPortrait(appearance, Native.Get<string>(actor, "Name") ?? "", Native.Get<int>(actor, "Level"),
            () => BuildModel(world, actor, modelId)));
    }

    /// <summary>Builds the familiar model through the client's own mob scene resolution.</summary>
    private static Node3D? BuildModel(World world, object actor, int modelId)
    {
        if (!IsInstanceValid(world)) return null;
        if (Native.Call(world, "ResolveMobScene", modelId) is PackedScene scene)
        {
            var model = scene.Instantiate<Node3D>();
            Native.Call(world, "ForceDoubleSidedOnce", model, scene.ResourcePath);
            return model;
        }
        var body = Native.Get<Node3D>(actor, "Body");
        string modelNode = Native.Get<string>(world, "ModelNodeName") ?? "Model";
        var visual = body != null && IsInstanceValid(body) ? body.GetNodeOrNull<Node3D>(modelNode) : null;
        var source = visual?.GetChildren().OfType<Node3D>().FirstOrDefault();
        var copy = source?.Duplicate((int)DuplicateFlags.UseInstantiation) as Node3D;
        if (copy != null) copy.Transform = Transform3D.Identity;
        return copy;
    }
}
