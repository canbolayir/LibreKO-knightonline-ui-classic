using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The read-only equipment inspection window: fourteen worn slots and nine costume slots as item cells,
/// fifteen statistic rows, and one outstanding request whose reply must carry the requested name. The
/// client's row list stays in the window, hidden; the plugin builds the controls the Classic skin places.
/// </summary>
public static class NativeEquipView
{
    public const string Title = "Equipment View";

    private static readonly string[] GearLabels =
    {
        "Right earring", "Helmet", "Left earring", "Necklace", "Pauldron", "Pet",
        "Right hand", "Belt", "Left hand", "Right ring", "Pants", "Left ring", "Gloves", "Boots",
    };

    private static readonly (int Slot, string Label)[] CostumeLabels =
    {
        (InventoryConstants.CosTattoo, "Tattoo"), (InventoryConstants.CosHelmet, "Costume helmet"),
        (InventoryConstants.CosFairy, "Fairy"), (InventoryConstants.CosGloveRight, "Pathos right"),
        (InventoryConstants.CosPauldron, "Outfit"), (InventoryConstants.CosGloveLeft, "Pathos left"),
        (InventoryConstants.CosTalisman, "Talisman"), (InventoryConstants.CosWing, "Wings"),
        (InventoryConstants.CosEmblem, "Emblem"),
    };

    private sealed class State
    {
        public HudWindow Window = null!;
        public Label Name = null!, Summary = null!, Status = null!;
        public VBoxContainer Stats = null!;
        public readonly Dictionary<int, ItemSlotView> Cells = new();
        public readonly Dictionary<int, string> Captions = new();
        public bool InFlight;
        public string Pending = "";
    }

    private static readonly ConditionalWeakTable<WorldRoot, State> _states = new();

    public static bool InFlight(WorldRoot world) => _states.TryGetValue(world, out var state) && state.InFlight;

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_equipview") || Game.Connection is not { } net) return;
        var root = window.Body;
        var state = new State { Window = window };
        foreach (var child in root.GetChildren().OfType<Control>()) child.Visible = false;

        state.Name = UiTheme.Text("", 14, UiTheme.TextHi); state.Name.Name = "inspect_name"; root.AddChild(state.Name);
        state.Summary = UiTheme.Text("", 12, UiTheme.TextLo); state.Summary.Name = "inspect_summary"; root.AddChild(state.Summary);
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 16); root.AddChild(cols);
        GridContainer Grid(string name, string caption)
        {
            var column = new VBoxContainer(); column.AddChild(UiTheme.SectionTitle(caption));
            var grid = new GridContainer { Name = name, Columns = 3 };
            grid.AddThemeConstantOverride("h_separation", 4); grid.AddThemeConstantOverride("v_separation", 4);
            column.AddChild(grid); cols.AddChild(column); return grid;
        }
        var gear = Grid("inspect_gear", "Equipment");
        for (int slot = 0; slot < InventoryConstants.SlotMax; slot++) gear.AddChild(Cell(world, state, slot, GearLabels[slot]));
        var costume = Grid("inspect_costume", "Costume");
        foreach (var (slot, label) in CostumeLabels) costume.AddChild(Cell(world, state, slot, label));
        var stats = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0) };
        stats.AddChild(UiTheme.SectionTitle("State"));
        state.Stats = new VBoxContainer { Name = "inspect_stats" }; state.Stats.AddThemeConstantOverride("separation", 3);
        stats.AddChild(state.Stats); cols.AddChild(stats);
        state.Status = UiTheme.Text("", 12, UiTheme.TextLo); state.Status.Name = "inspect_status";
        state.Status.AutowrapMode = TextServer.AutowrapMode.WordSmart; root.AddChild(state.Status);

        _states.AddOrUpdate(world, state);
        window.Title = Title;
        window.SetMeta("classic_equipview_controls", 1);
        Action<EquipmentViewResult, EquipmentView> received = (result, view) => Received(world, state, result, view);
        NativeHooks.Listen("Net.EquipmentViewEvent", () => Game.Session.PlayerInspect.EquipmentViewEvent += received, () => Game.Session.PlayerInspect.EquipmentViewEvent -= received, world);
        NativeWindows.Sync(window, () => Sync(world, state));
    }

    /// <summary>Starts an inspection unless one is outstanding; the client sends the request.</summary>
    public static void Request(WorldRoot world, string name)
    {
        if (!_states.TryGetValue(world, out var state)) { Native.Call(world, "EquipView.RequestEquipmentView", name); return; }
        if (state.InFlight || string.IsNullOrWhiteSpace(name)) return;
        Native.Call(world, "EquipView.RequestEquipmentView", name);
        Begin(world, state, name);
    }

    /// <summary>Adopts a request the client started itself, e.g. from the player menu.</summary>
    private static void Sync(WorldRoot world, State state)
    {
        state.Window.Title = Title;
        var status = Native.Get<Label>(world, "EquipView.status");
        string pending = Native.Get<string>(world, "EquipView.pending") ?? "";
        if (status?.Text == "Requesting…" && (!state.InFlight || pending != state.Pending)) Begin(world, state, pending);
    }

    private static void Begin(WorldRoot world, State state, string name)
    {
        state.InFlight = true;
        state.Pending = name;
        state.Window.Title = Title;
        Native.Call(world, "ItemTooltip.Hide");
        Clear(state);
        state.Name.Text = name;
        state.Status.Text = "Requesting...";
        state.Status.AddThemeColorOverride("font_color", UiTheme.TextLo);
    }

    private static ItemSlotView Cell(WorldRoot world, State state, int slot, string caption)
    {
        var cell = new ItemSlotView(45) { Name = "inspect_slot_" + slot, Index = slot, TooltipText = caption };
        state.Captions[slot] = caption;
        cell.Hovered += current => Native.Call(world, "ItemTooltip.Show", current.Index, current.Item, "");
        cell.Unhovered += _ => Native.Call(world, "ItemTooltip.Hide");
        state.Cells[slot] = cell;
        return cell;
    }

    private static void Clear(State state)
    {
        foreach (var (slot, cell) in state.Cells) { cell.Clear(); cell.TooltipText = state.Captions[slot]; }
        foreach (var child in state.Stats.GetChildren()) { state.Stats.RemoveChild(child); child.QueueFree(); }
        state.Name.Text = ""; state.Name.TooltipText = ""; state.Summary.Text = ""; state.Summary.TooltipText = "";
    }

    /// <summary>Applies an inspection reply; the network event delivers it after the client's own handler.</summary>
    public static void Received(WorldRoot world, EquipmentViewResult result, EquipmentView view)
    {
        if (_states.TryGetValue(world, out var state)) Received(world, state, result, view);
    }

    private static void Received(WorldRoot world, State state, EquipmentViewResult result, EquipmentView view)
    {
        if (!GodotObject.IsInstanceValid(state.Window) || !state.InFlight) return;
        if (result == EquipmentViewResult.Accepted && !string.Equals(view.Name, state.Pending, StringComparison.OrdinalIgnoreCase)) return;
        state.InFlight = false;
        state.Window.Title = Title;
        if (!Native.Get<bool>(world, "EquipView.shown")) return;
        Clear(state);
        if (result != EquipmentViewResult.Accepted)
        {
            // The client's own refusal (its text table: 44785, 44787, 44789) when it has written one.
            string native = Native.Get<Label>(world, "EquipView.status")?.Text ?? "";
            state.Status.Text = native.Length > 0 && native != "Requesting…" ? native : result switch
            {
                EquipmentViewResult.NotInSameRegion => $"{state.Pending} is not in this region.",
                EquipmentViewResult.CannotChooseYourself => "You cannot inspect yourself.",
                EquipmentViewResult.NoViewEquipmentItem => "You need a View Equipment item.",
                _ => $"{state.Pending} could not be found.",
            };
            state.Status.AddThemeColorOverride("font_color", UiTheme.Bad);
            return;
        }
        state.Status.Text = "";
        state.Name.Text = view.Name; state.Name.TooltipText = view.Name;
        string level = view.RebirthLevel > 0 ? $"{view.Level}/{view.RebirthLevel}" : view.Level.ToString();
        string nation = Native.Call(Native.StaticOwner("LibreKO.Features.Windows.GlobalMapFeature"), "NationName", (byte)view.Nation) as string ?? Nations.Name(view.Nation);
        state.Summary.Text = $"Lv. {level} | {CharacterClassCatalog.DisplayName(view.Class)} | {nation}";
        state.Summary.TooltipText = state.Summary.Text;
        foreach (var worn in view.Worn)
            if (state.Cells.TryGetValue(worn.Slot, out var cell))
            {
                cell.Set(new ItemSlot { ItemId = worn.ItemId, Durability = worn.Durability, Count = 1, Flag = worn.Flag });
                cell.TooltipText = worn.ItemId == 0 ? state.Captions[worn.Slot] : "";
            }
        Stat(state, "Max HP", view.MaxHp.ToString("N0"), new Color("ff8080"));
        Stat(state, "Max MP", view.MaxMp.ToString("N0"), new Color("80c8ff"));
        state.Stats.AddChild(new HSeparator());
        Stat(state, "Strength", WithBonus(view.Str, view.StrBonus));
        Stat(state, "Stamina", WithBonus(view.Sta, view.StaBonus));
        Stat(state, "Dexterity", WithBonus(view.Dex, view.DexBonus));
        Stat(state, "Intelligence", WithBonus(view.Intel, view.IntelBonus));
        Stat(state, "Magic attack", WithBonus(view.Magic, view.MagicBonus));
        state.Stats.AddChild(new HSeparator());
        Stat(state, "Attack", view.Attack.ToString());
        Stat(state, "Defence", view.Defence.ToString());
        state.Stats.AddChild(new HSeparator());
        Stat(state, "Fire resist", view.FireR.ToString());
        Stat(state, "Ice resist", view.IceR.ToString());
        Stat(state, "Lightning resist", view.LightningR.ToString());
        Stat(state, "Magic resist", view.MagicR.ToString());
        Stat(state, "Curse resist", view.CurseR.ToString());
        Stat(state, "Poison resist", view.PoisonR.ToString());
    }

    private static string WithBonus(int stat, int bonus) => bonus > 0 ? $"{stat}  (+{bonus})" : stat.ToString();

    private static void Stat(State state, string label, string value, Color? color = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var key = UiTheme.Text(label, 12, UiTheme.TextLo);
        key.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(key);
        row.AddChild(UiTheme.Text(value, 12, color ?? UiTheme.TextHi));
        state.Stats.AddChild(row);
    }
}
