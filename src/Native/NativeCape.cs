using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterCape(PluginContext context) => NativeWindows.Prepare("cape", NativeCape.Prepare);
}

/// <summary>
/// The Classic knights mantle shop on top of the client's cape window: a rear-facing 3D preview, the
/// original paged catalogue (four patterns, six colours per page), custom dye, an eligibility gate that
/// matches the server's refusals, and a cost confirmation that freezes the chosen cape and colour. The
/// client keeps the choice, sliders, purchase request and result; the plugin owns the catalogue pages,
/// the preview and the confirmation. The client's own cape texts are used where it has them: the voucher rule and
/// refusal (<see cref="CapeShop"/>, text 10322), the confirmation wording (10316/10318/10321, Clan CONT and Noah lines)
/// and "Now equipped" (10313). The client's CONT and note lines stay in its hidden column: the Classic price line
/// carries the CONT cost and the cape name's tooltip carries the note.
/// </summary>
public static class NativeCape
{
    public const string Title = "Clan Cape";
    public const int PatternsPerPage = 4;
    public const int ColoursPerPage = 6;
    public const int DyePoints = 36_000;
    private const int NoCape = -1;
    private const int PatternSampleColour = 1;
    private const int SwatchSize = 46;
    private const int TryAgainText = 16810;
    private static readonly int[] UnsoldCapes = { 97, 98, 99 };

    private sealed class State
    {
        public HudWindow Window = null!;
        public LookPreview Look = null!;
        public readonly Button[] Patterns = new Button[PatternsPerPage], Colours = new Button[ColoursPerPage];
        public readonly List<int> PatternIds = new(), ColourIds = new();
        public int PatternPage, ColourPage;
        public Label PatternPageLabel = null!, ColourPageLabel = null!;
        public Button PatternPrevious = null!, PatternNext = null!, ColourPrevious = null!, ColourNext = null!;
        public Notice? Notice;
        public int Revision;
        public bool Awaiting;
        /// <summary>Set by a cape release for the rest of the frame, so a reset of every pending operation can follow it.</summary>
        public bool Released;
        public readonly Dictionary<Button, string> ArtKeys = new();
    }

    private static readonly ConditionalWeakTable<WorldRoot, State> _states = new();

    public static Notice? Confirmation(WorldRoot world) => _states.TryGetValue(world, out var state) ? state.Notice : null;
    public static int PatternPage(WorldRoot world) => _states.TryGetValue(world, out var state) ? state.PatternPage : 0;
    public static int ColourPage(WorldRoot world) => _states.TryGetValue(world, out var state) ? state.ColourPage : 0;

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_cape") || Game.Connection is not { } net) return;
        var sliders = new[] { "CapeWindow.redSlider", "CapeWindow.greenSlider", "CapeWindow.blueSlider" }.Select(f => Native.Get<HSlider>(world, f)).ToArray();
        var values = new[] { "CapeWindow.rVal", "CapeWindow.gVal", "CapeWindow.bVal" }.Select(f => Native.Get<Label>(world, f)).ToArray();
        var ticket = Native.Get<CheckBox>(world, "CapeWindow.ticket");
        var buy = Native.Get<Button>(world, "CapeWindow.buyBtn");
        var hint = Native.Get<Label>(world, "CapeWindow.hint");
        var status = Native.Get<Label>(world, "CapeWindow.status");
        var chosen = Native.Get<Label>(world, "CapeWindow.chosenLbl");
        var requirement = Native.Get<Label>(world, "CapeWindow.reqLbl");
        var price = Native.Get<Label>(world, "CapeWindow.priceLbl");
        if (sliders.Any(s => s == null) || values.Any(v => v == null) || ticket == null || buy == null || hint == null
            || status == null || chosen == null || requirement == null || price == null) return;

        var body = window.Body;
        var state = new State { Window = window };
        state.Look = new LookPreview(165, 217) { Name = "cape_preview" };
        var extra = new VBoxContainer();
        extra.AddThemeConstantOverride("separation", 8);
        body.AddChild(extra);
        body.MoveChild(extra, 0);
        extra.AddChild(state.Look);
        var turn = new HBoxContainer(); extra.AddChild(turn);
        foreach (int direction in new[] { -1, 1 })
        {
            var arrow = new Button { Name = direction < 0 ? "cape_turn_left" : "cape_turn_right", Text = direction < 0 ? "◀" : "▶", FocusMode = Control.FocusModeEnum.None };
            arrow.Pressed += () => state.Look.Turn(direction * 30); turn.AddChild(arrow);
        }
        var patterns = new GridContainer { Columns = 2 }; extra.AddChild(patterns);
        for (int i = 0; i < PatternsPerPage; i++)
        {
            int slot = i;
            var cell = Cell("cape_pattern_" + i); state.Patterns[i] = cell;
            cell.Pressed += () =>
            {
                int index = state.PatternPage * PatternsPerPage + slot;
                if (Editing(world, state) && index < state.PatternIds.Count) { ShowPattern(world, state.PatternIds[index]); Gate(world); }
            };
            patterns.AddChild(cell);
        }
        Pages(world, state, extra, true);
        var colours = new GridContainer { Columns = 3 }; extra.AddChild(colours);
        for (int i = 0; i < ColoursPerPage; i++)
        {
            int slot = i;
            var cell = Cell("cape_colour_" + i); state.Colours[i] = cell;
            cell.Pressed += () =>
            {
                int index = state.ColourPage * ColoursPerPage + slot;
                if (Editing(world, state) && index < state.ColourIds.Count) Select(world, state.ColourIds[index]);
            };
            colours.AddChild(cell);
        }
        Pages(world, state, extra, false);

        chosen.Name = "cape_chosen"; requirement.Name = "cape_requirement"; price.Name = "cape_price";
        string[] channels = { "R", "G", "B" };
        for (int i = 0; i < 3; i++)
        {
            sliders[i]!.Name = "cape_dye_" + channels[i];
            values[i]!.Name = "cape_value_" + channels[i];
            sliders[i]!.ValueChanged += _ => DyeChanged(world);
        }
        ticket.Name = "cape_ticket";
        ticket.Toggled += _ => Gate(world);
        buy.Name = "cape_buy";
        NativeIdentity.Rewire(buy, () => BuyPressed(world));
        var cancel = new Button { Name = "cape_cancel", Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += () => Native.Call(world, "CapeWindow.Close");
        buy.GetParent().AddChild(cancel);
        hint.Name = "cape_hint"; hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.Name = "cape_status"; status.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        _states.AddOrUpdate(world, state);
        LookFraming.Attach(state.Look, () => Game.Session.Self.LastEnter.Race);
        window.VisibilityChanged += () => VisibilityChanged(world, state);
        Action<CapeChangeReply> result = reply => Result(world, state, reply.Succeeded, reply.CapeId, reply.R, reply.G, reply.B);
        NativeHooks.Listen("Net.CapeResultEvent", () => Game.Session.Cape.CapeResultEvent += result, () => Game.Session.Cape.CapeResultEvent -= result, world);
        Action reset = () => Reset(world, state);
        Native.Subscribe(Game.Session.Cape, "CapeResetEvent", reset, world);
        // A lost connection resets every pending operation right after the cape; a cape reply for another
        // clan releases only the cape request.
        Action lost = () => ConnectionReset(world, state);
        NativeHooks.Listen("Net.PetResetEvent", () => Game.Session.Pet.PetResetEvent += lost, () => Game.Session.Pet.PetResetEvent -= lost, world);
        NativeHooks.Listen("Net.RebirthResetEvent", () => Game.Session.Develop.RebirthResetEvent += lost, () => Game.Session.Develop.RebirthResetEvent -= lost, world);
        window.SetMeta("classic_cape_controls", 1);
        NativeWindows.Sync(window, () => Gate(world));
        if (window.Visible) Opened(world, state);
    }

    private static Button Cell(string name) => new()
    {
        Name = name, ToggleMode = true, FocusMode = Control.FocusModeEnum.None,
        CustomMinimumSize = new Vector2(SwatchSize, SwatchSize), ClipContents = true,
    };

    private static void Pages(WorldRoot world, State state, VBoxContainer parent, bool pattern)
    {
        string prefix = pattern ? "cape_pattern_" : "cape_colour_";
        var row = new HBoxContainer(); parent.AddChild(row);
        var previous = new Button { Name = prefix + "previous", Text = "◀", FocusMode = Control.FocusModeEnum.None };
        var label = new Label { Name = prefix + "page", Text = "1", HorizontalAlignment = HorizontalAlignment.Center };
        var next = new Button { Name = prefix + "next", Text = "▶", FocusMode = Control.FocusModeEnum.None };
        row.AddChild(previous); row.AddChild(label); row.AddChild(next);
        previous.Pressed += () => ChangePage(world, state, pattern, -1);
        next.Pressed += () => ChangePage(world, state, pattern, 1);
        if (pattern) { state.PatternPrevious = previous; state.PatternNext = next; state.PatternPageLabel = label; }
        else { state.ColourPrevious = previous; state.ColourNext = next; state.ColourPageLabel = label; }
    }

    /// <summary>Opens or closes the service; a pending purchase keeps it closed.</summary>
    public static void Toggle(WorldRoot world)
    {
        if (!Native.Get<bool>(world, "CapeWindow.shown") && Native.Get<bool>(world, "CapeWindow.requestInFlight")) return;
        Native.Call(world, "CapeWindow.Toggle");
    }

    private static void VisibilityChanged(WorldRoot world, State state)
    {
        if (!state.Window.Visible)
        {
            Dismiss(state);
            LookFraming.Clear(state.Look);
            return;
        }
        // A request still pending keeps the service closed, as reopening could replace its draft.
        if (Native.Get<bool>(world, "CapeWindow.requestInFlight"))
        {
            state.Window.Visible = false;
            Callable.From(() => { if (Native.Get<bool>(world, "CapeWindow.shown")) Native.Call(world, "CapeWindow.Close"); }).CallDeferred();
            return;
        }
        // The client finishes opening (worn cape, dye, its own catalogue) before the Classic view takes over.
        Callable.From(() => { if (GodotObject.IsInstanceValid(state.Window) && state.Window.Visible) Opened(world, state); }).CallDeferred();
    }

    private static void Opened(WorldRoot world, State state)
    {
        if (Game.Connection is not { } net) return;
        var worn = Game.Session.Self.LastEnter;
        Native.Get<CheckBox>(world, "CapeWindow.ticket")!.SetPressedNoSignal(false);
        var gear = Native.Call(world, "EntityLooksSystem.SelfGear") as int[] ?? Array.Empty<int>();
        LookFraming.ShowCape(state.Look, worn.Race, worn.Face, (worn.Hair >> 24) & 0xFF,
            new Color((worn.Hair >> 16 & 255) / 255f, (worn.Hair >> 8 & 255) / 255f, (worn.Hair & 255) / 255f), gear);
        var sold = new SortedSet<int>();
        foreach (var (id, def) in Cape.Catalogue)
            if (SoldHere(id, def)) sold.Add(def.M);
        state.PatternIds.Clear(); state.PatternIds.AddRange(sold);
        state.PatternPage = 0;
        int current = Native.Get<int>(world, "CapeWindow.pattern");
        ShowPattern(world, sold.Count > 0 ? (sold.Contains(current) ? current : sold.Min) : -1);
        Gate(world);
    }

    private static bool SoldHere(int id, Cape.CapeDef def) => !UnsoldCapes.Contains(id) && (def.Price > 0 || def.Points > 0);

    private static bool Editing(WorldRoot world, State state) =>
        Native.Get<bool>(world, "CapeWindow.shown") && !Native.Get<bool>(world, "CapeWindow.requestInFlight") && state.Notice == null;

    private static MyClanInfo Clan => Game.Session.Clan.MyClan;

    private static bool Allowed(Cape.CapeDef def) => ClanTypes.MeetsCapeRank(Clan.Flag, Clan.Grade, def.Ranking, def.Grade);

    private static string NeedName(Cape.CapeDef def) => Native.Call(Native.StaticOwner("LibreKO.Features.Windows.CapeFeature"), "NeedName", def) as string ?? def.Name;

    /// <summary>Shows a pattern's colours; a chosen cape of another pattern is cleared.</summary>
    public static void ShowPattern(WorldRoot world, int pattern)
    {
        if (!_states.TryGetValue(world, out var state)) return;
        Native.Set(world, "CapeWindow.pattern", pattern);
        state.ColourPage = 0;
        state.ColourIds.Clear();
        foreach (var (id, def) in Cape.Catalogue)
            if (def.M == pattern && SoldHere(id, def)) state.ColourIds.Add(id);
        state.ColourIds.Sort();
        int choice = Native.Get<int>(world, "CapeWindow.choice");
        if (choice >= 0 && (!Cape.TryGet(choice, out var chosen) || chosen.M != pattern)) Native.Set(world, "CapeWindow.choice", choice = NoCape);
        RefreshPages(world, state);
        if (choice >= 0) Select(world, choice);
        else
        {
            Native.Get<Label>(world, "CapeWindow.chosenLbl")!.Text = "Select a colour";
            Native.Get<Label>(world, "CapeWindow.reqLbl")!.Text = "";
            Native.Get<Label>(world, "CapeWindow.priceLbl")!.Text = "";
            RefreshPreview(world);
        }
    }

    public static void Select(WorldRoot world, int capeId)
    {
        if (!_states.TryGetValue(world, out var state) || !Cape.TryGet(capeId, out var def)) return;
        Native.Set(world, "CapeWindow.choice", capeId);
        if (Native.Get<int>(world, "CapeWindow.pattern") != def.M) ShowPattern(world, def.M);
        int index = state.ColourIds.IndexOf(capeId);
        if (index >= 0) state.ColourPage = index / ColoursPerPage;
        RefreshPages(world, state);
        Describe(world, def);
        RefreshPreview(world);
        Gate(world);
    }

    private static void DyeChanged(WorldRoot world)
    {
        RefreshPreview(world);
        Gate(world);
    }

    private static void RefreshPreview(WorldRoot world)
    {
        if (!_states.TryGetValue(world, out var state) || Game.Connection is not { } net) return;
        int choice = Native.Get<int>(world, "CapeWindow.choice");
        if (Native.Get<Button>(world, "CapeWindow.buyBtn") is { } buy)
            buy.Text = state.Window.HasMeta(HotNames.ClassicCape) ? (choice >= 0 ? "Buy" : "Apply") : (choice >= 0 ? "Buy cape" : "Apply dye");
        if (!Native.Get<bool>(world, "CapeWindow.shown")) return;
        int previewId = choice >= 0 ? choice : Native.Get<int>(world, "CapeWindow.current");
        var (r, g, b) = Dye(world);
        var race = Game.Session.Self.LastEnter.Race;
        LookFraming.SetCape(state.Look, previewId, new Color(r / 255f, g / 255f, b / 255f), race);
        if (Native.Get<Node3D>(world, "Avatar.Visual") is not { } self) return;
        // DressCape is an instance method since fa3cfc3 (it reads the zone: no capes in the Chaos dungeon);
        // calling it on the world binds both shapes.
        Native.Call(world, "EntityLooksSystem.Dress", self, previewId, r, g, b, false, race, true);
        Native.Set(world, "CapeWindow.previewing", true);
    }

    private static (int R, int G, int B) Dye(WorldRoot world) =>
        ((int)(Native.Get<HSlider>(world, "CapeWindow.redSlider")?.Value ?? 0), (int)(Native.Get<HSlider>(world, "CapeWindow.greenSlider")?.Value ?? 0), (int)(Native.Get<HSlider>(world, "CapeWindow.blueSlider")?.Value ?? 0));

    /// <summary>The fork's UpdateCapeGate: what may be bought now, and why not.</summary>
    public static void Gate(WorldRoot world)
    {
        if (!_states.TryGetValue(world, out var state)) return;
        bool editing = Editing(world, state);
        var ticket = Native.Get<CheckBox>(world, "CapeWindow.ticket")!;
        Native.Get<Button>(world, "CapeWindow.buyBtn")!.Disabled = !editing || !CanApply(world);
        foreach (var field in new[] { "CapeWindow.redSlider", "CapeWindow.greenSlider", "CapeWindow.blueSlider" }) Native.Get<HSlider>(world, field)!.Editable = editing;
        ticket.Disabled = !editing;
        RefreshPages(world, state);
        int choice = Native.Get<int>(world, "CapeWindow.choice");
        // The client rewrites the chosen column when the clan changes (its own SelectCape); the Classic lines win.
        if (choice >= 0 && Cape.TryGet(choice, out var def)) Describe(world, def);
        var clan = Clan;
        Native.Get<Label>(world, "CapeWindow.hint")!.Text =
            !clan.InClan ? "Join a clan to buy a cape."
            : !clan.IsChief ? "Only the clan chief can change the cape."
            : clan.Flag < ClanTypes.Promoted ? "Your clan must be promoted (Official) before buying a cape."
            : Native.Get<bool>(world, "Avatar.Dead") ? "You cannot change a cape while dead."
            : ticket.ButtonPressed && !VoucherAllowed(choice) ? ItemData.Text(CapeShop.VoucherRefusedText, "You cannot purchase this item using exchange voucher")
            : ticket.ButtonPressed ? "A castellan ticket is required. Only eligible capes can be purchased."
            : $"Custom dye costs {DyePoints.ToString("n0", System.Globalization.CultureInfo.InvariantCulture)} clan points.";
    }

    private static bool CanApply(WorldRoot world)
    {
        var clan = Clan;
        if (!clan.IsChief || clan.Flag < ClanTypes.Promoted || Native.Get<bool>(world, "Avatar.Dead")) return false;
        var (r, g, b) = Dye(world);
        bool dye = r != 0 || g != 0 || b != 0;
        bool ticket = Native.Get<CheckBox>(world, "CapeWindow.ticket")!.ButtonPressed;
        int choice = Native.Get<int>(world, "CapeWindow.choice");
        if (choice < 0 && (!dye || !Cape.IsRenderable(Native.Get<int>(world, "CapeWindow.current")))) return false;
        if (choice >= 0)
        {
            if (!Cape.TryGet(choice, out var def)) return false;
            // The client's voucher rule: only castle capes are sold for the exchange voucher.
            if (ticket) return CapeShop.VoucherAllowed(def.Castle);
            if (!Allowed(def)) return false;
        }
        else if (ticket) return false;
        return !dye || ClanTypes.AcceptsDonations(clan.Flag);
    }

    private static bool VoucherAllowed(int capeId) => Cape.TryGet(capeId, out var def) && CapeShop.VoucherAllowed(def.Castle);

    /// <summary>The Classic info box lines of a chosen cape: name (note as tooltip), cost and grade.</summary>
    private static void Describe(WorldRoot world, Cape.CapeDef def)
    {
        var chosen = Native.Get<Label>(world, "CapeWindow.chosenLbl")!;
        string name = def.M > 0 ? $"{def.Name} (pattern {def.M})" : def.Name;
        if (chosen.Text != name) chosen.Text = name;
        // The client's note line: "Now equipped" or the cape's description.
        string note = Native.Has(world, "CapeWindow.noteLbl") ? Native.Get<Label>(world, "CapeWindow.noteLbl")?.Text ?? "" : "";
        chosen.TooltipText = note.Length > 0 ? name + "\n" + note : name;
        var requirement = Native.Get<Label>(world, "CapeWindow.reqLbl")!;
        string need = NeedName(def).Replace(" grade ", "\nGrade ");
        if (requirement.Text != need) requirement.Text = need;
        requirement.AddThemeColorOverride("font_color", Clan.InClan && !Allowed(def) ? UiTheme.Bad : UiTheme.TextLo);
        var price = Native.Get<Label>(world, "CapeWindow.priceLbl")!;
        string cost = CostText(world);
        if (price.Text != cost) price.Text = cost;
    }

    private static string CostText(WorldRoot world)
    {
        int choice = Native.Get<int>(world, "CapeWindow.choice");
        if (Native.Get<CheckBox>(world, "CapeWindow.ticket")!.ButtonPressed && VoucherAllowed(choice)) return "1 castellan ticket";
        Cape.TryGet(choice, out var def);
        var (r, g, b) = Dye(world);
        if (choice >= 0 && choice == Native.Get<int>(world, "CapeWindow.current") && r == 0 && g == 0 && b == 0)
            return ItemData.Text(CapeShop.NowEquippedText, "Now equipped");
        int points = def.Points + (r != 0 || g != 0 || b != 0 ? DyePoints : 0);
        var costs = new List<string>();
        if (def.Price > 0) costs.Add($"{def.Price:n0} Noahs");
        if (points > 0) costs.Add($"{points:n0} clan points");
        return costs.Count == 0 ? "Free" : string.Join("\n", costs);
    }

    private static void ChangePage(WorldRoot world, State state, bool pattern, int direction)
    {
        if (!Editing(world, state)) return;
        int count = pattern ? state.PatternIds.Count : state.ColourIds.Count;
        int capacity = pattern ? PatternsPerPage : ColoursPerPage;
        int page = Math.Clamp((pattern ? state.PatternPage : state.ColourPage) + direction, 0, Math.Max(0, (count - 1) / capacity));
        if (pattern) state.PatternPage = page; else state.ColourPage = page;
        RefreshPages(world, state);
    }

    private static void RefreshPages(WorldRoot world, State state)
    {
        bool editing = Editing(world, state);
        int pattern = Native.Get<int>(world, "CapeWindow.pattern"), choice = Native.Get<int>(world, "CapeWindow.choice");
        for (int slot = 0; slot < PatternsPerPage; slot++)
        {
            int index = state.PatternPage * PatternsPerPage + slot;
            var button = state.Patterns[slot];
            bool exists = index < state.PatternIds.Count;
            button.Disabled = !editing || !exists;
            button.SetPressedNoSignal(exists && state.PatternIds[index] == pattern);
            if (!exists) { ClearArt(state, button); button.TooltipText = ""; continue; }
            int shown = state.PatternIds[index];
            button.TooltipText = shown == 0 ? "Plain" : $"Pattern {shown}";
            FillArt(state, button, PatternSampleColour, shown, false);
        }
        for (int slot = 0; slot < ColoursPerPage; slot++)
        {
            int index = state.ColourPage * ColoursPerPage + slot;
            var button = state.Colours[slot];
            bool exists = index < state.ColourIds.Count && Cape.TryGet(state.ColourIds[index], out _);
            button.Disabled = !editing || !exists;
            button.SetPressedNoSignal(exists && state.ColourIds[index] == choice);
            if (!exists) { ClearArt(state, button); button.TooltipText = ""; continue; }
            Cape.TryGet(state.ColourIds[index], out var def);
            bool locked = Clan.InClan && !Allowed(def);
            FillArt(state, button, def.C, def.M, locked);
            button.TooltipText = Native.Call(world, "CapeWindow.CellTip", def, locked) as string ?? def.Name;
        }
        state.PatternPageLabel.Text = $"{state.PatternPage + 1}/{Math.Max(1, (state.PatternIds.Count + PatternsPerPage - 1) / PatternsPerPage)}";
        state.ColourPageLabel.Text = $"{state.ColourPage + 1}/{Math.Max(1, (state.ColourIds.Count + ColoursPerPage - 1) / ColoursPerPage)}";
        state.PatternPrevious.Disabled = !editing || state.PatternPage == 0;
        state.PatternNext.Disabled = !editing || (state.PatternPage + 1) * PatternsPerPage >= state.PatternIds.Count;
        state.ColourPrevious.Disabled = !editing || state.ColourPage == 0;
        state.ColourNext.Disabled = !editing || (state.ColourPage + 1) * ColoursPerPage >= state.ColourIds.Count;
    }

    private static void ClearArt(State state, Button button)
    {
        state.ArtKeys.Remove(button);
        foreach (var child in button.GetChildren()) { button.RemoveChild(child); child.QueueFree(); }
    }

    private static void FillArt(State state, Button button, int colour, int pattern, bool locked)
    {
        string key = $"{colour}:{pattern}:{locked}";
        if (state.ArtKeys.TryGetValue(button, out var current) && current == key && button.GetChildCount() > 0) return;
        ClearArt(state, button);
        state.ArtKeys[button] = key;
        if (Native.Call(Native.StaticOwner("LibreKO.Features.Windows.CapeFeature"), "Swatch", colour, pattern, Colors.White, locked) is not Control art) return;
        art.CustomMinimumSize = Vector2.Zero;
        art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(art);
    }

    /// <summary>Opens the cost confirmation for the cape and dye chosen at this moment.</summary>
    public static void BuyPressed(WorldRoot world)
    {
        if (!_states.TryGetValue(world, out var state) || !Editing(world, state)) return;
        if (!Clan.IsChief) { Native.Call(world, "CapeWindow.SetStatus", "Only the clan chief can change the cape.", true); return; }
        int capeId = Native.Get<int>(world, "CapeWindow.choice");
        var (r, g, b) = Dye(world);
        if (capeId < 0 && r == 0 && g == 0 && b == 0)
        {
            Native.Call(world, "CapeWindow.SetStatus", "Pick a cape, or choose a dye colour to repaint the one you have.", true);
            return;
        }
        if (!CanApply(world)) { Gate(world); return; }
        var op = Native.Get<CheckBox>(world, "CapeWindow.ticket")!.ButtonPressed ? CapeSubOpcode.TicketPurchase : CapeSubOpcode.NormalPurchase;
        int revision = ++state.Revision;
        var layer = Native.Get<Node>(world, "CapeWindow.layer") ?? world;
        state.Notice = Notice.Confirm(layer, ConfirmText(world, capeId, op == CapeSubOpcode.TicketPurchase), "Buy", "Cancel",
            () => Submit(world, revision, op, capeId, (byte)r, (byte)g, (byte)b), () => Cancel(world, revision), title: Title);
        Gate(world);
    }

    /// <summary>The client's confirmation wording (texts 10316/10318, or 10321 for the voucher) with the cape's cost.</summary>
    public static string ConfirmText(WorldRoot world, int capeId, bool voucher)
    {
        if (voucher) return ItemData.Text(CapeShop.VoucherConfirmText, "Do you wish to purchase with the exchange voucher?");
        var (r, g, b) = Dye(world);
        bool painting = r != 0 || g != 0 || b != 0;
        Cape.TryGet(capeId, out var def);
        int cont = capeId < 0 ? CapeShop.PaintCont : CapeShop.ContCost(def.Points, painting);
        string contLine = CapeShop.Fill(ItemData.Text(CapeShop.ContLineText, "%s Clan CONT"), CapeShop.Amount(cont));
        string noahLine = CapeShop.Fill(ItemData.Text(CapeShop.NoahLineText, "%s Noah"), CapeShop.Amount(capeId < 0 ? 0 : def.Price));
        bool castle = capeId >= 0 && def.Castle;
        string fallback = castle
            ? "If you are not a member of current Castellan clan,\n%s\n%s will be used.\nDo you wish to continue with the purchase?"
            : "Purchase price:\n%s\n%s\nDo you wish to purchase it?";
        string text = CapeShop.Fill(ItemData.Text(CapeShop.ConfirmText(castle), fallback), contLine, noahLine);
        // The retail strings break lines with stray spaces around them.
        return string.Join('\n', text.Split('\n').Select(line => line.Trim()));
    }

    private static void Submit(WorldRoot world, int revision, CapeSubOpcode op, int capeId, byte r, byte g, byte b)
    {
        if (!_states.TryGetValue(world, out var state) || revision != state.Revision || !Native.Get<bool>(world, "CapeWindow.shown")
            || Native.Get<bool>(world, "CapeWindow.requestInFlight") || state.Notice == null || Game.Connection is not { } net) return;
        state.Notice = null;
        if (!CanApply(world)) { Gate(world); return; }
        Native.Set(world, "CapeWindow.requestInFlight", true);
        state.Awaiting = true;
        Native.Call(world, "CapeWindow.SetStatus", "Requesting…", false);
        Gate(world);
        // The fix branch's sender reports a refused send; upstream's returns nothing and always sends.
        if (!Game.Requests.Cape.Buy((byte)op, capeId, r, g, b))
            Native.Call(world, "CapeWindow.OnResult", new CapeChangeReply(Succeeded: false, ClanOrResult: -1, CapeId: 0, R: 0, G: 0, B: 0));
    }

    private static void Cancel(WorldRoot world, int revision)
    {
        if (!_states.TryGetValue(world, out var state) || revision != state.Revision || state.Notice == null
            || Native.Get<bool>(world, "CapeWindow.requestInFlight")) return;
        state.Notice = null;
        Gate(world);
    }

    private static void Dismiss(State state)
    {
        state.Revision++;
        if (state.Notice is { } notice && GodotObject.IsInstanceValid(notice)) notice.Close();
        state.Notice = null;
    }

    /// <summary>Runs after the client applied the result: the Classic view follows the applied cape.</summary>
    private static void Result(WorldRoot world, State state, bool ok, int capeId, int r, int g, int b)
    {
        if (!state.Awaiting || !GodotObject.IsInstanceValid(state.Window)) return;
        state.Awaiting = false;
        if (ok && Native.Get<bool>(world, "CapeWindow.shown"))
        {
            if (capeId >= 0)
            {
                if (Cape.TryGet(capeId, out var selected)) ShowPattern(world, selected.M);
                Select(world, capeId);
            }
            RefreshPreview(world);
        }
        Gate(world);
    }

    /// <summary>
    /// The client released the cape request without a result (a reply for another clan, or a reset). The
    /// draft stays editable and the window says the request did not complete.
    /// </summary>
    private static void Reset(WorldRoot world, State state)
    {
        bool awaiting = state.Awaiting;
        state.Awaiting = false;
        if (!GodotObject.IsInstanceValid(state.Window)) return;
        Native.Set(world, "CapeWindow.requestInFlight", false);
        if (awaiting && Native.Get<bool>(world, "CapeWindow.shown"))
            Native.Call(world, "CapeWindow.SetStatus", LibreKO.Domain.ItemData.Text(TryAgainText, "Try again later"), true);
        Gate(world);
        state.Released = true;
        Callable.From(() => state.Released = false).CallDeferred();
    }

    /// <summary>A lost connection closes the service and forgets the draft.</summary>
    private static void ConnectionReset(WorldRoot world, State state)
    {
        if (!state.Released || !GodotObject.IsInstanceValid(state.Window)) return;
        state.Released = false;
        Native.Call(world, "CapeWindow.Close");
        Dismiss(state);
        Native.Set(world, "CapeWindow.choice", NoCape);
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName ClassicCape = "classic_cape";
}
