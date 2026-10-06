using Godot;
using System.Reflection;
using System.Text.Json;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using LibreKO.Network;
using LibreKO.Plugins;

public partial class Preview
{
    private async Task CaptureCharacterAudit(PluginGame game, int nation)
    {
        var window = GetWindow();
        window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        window.Size = new Vector2I(800, 660);
        var backdrop = new ColorRect { Color = new Color("252822"), Size = new Vector2(800, 660), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(backdrop);
        var shell = new Control(); AddChild(shell);
        var host = (WindowHost)Create(typeof(WindowHost), "character_info", "Character", shell, (Action)(() => { }));
        var actual = new CharacterWindow(host) { Position = new Vector2(32, 46) }; AddChild(actual);
        var fixture = (CharacterAuditData)game.Windows.CharacterPanel!;
        var title = new Label { Text = "CURRENT PLUGIN (1:1)                          ORIGINAL IMPORTED UIF (1:1)", Position = new Vector2(32, 12) };
        title.AddThemeFontSizeOverride("font_size", 14); AddChild(title);
        var output = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/character-eight-screen-audit"));
        if (OS.GetCmdlineUserArgs().Contains("audit-d3d12")) output = System.IO.Path.Combine(output, "d3d12-mobile");
        if (OS.GetCmdlineUserArgs().Contains("audit-restored")) output = System.IO.Path.Combine(output, "native-restored");
        if (OS.GetCmdlineUserArgs().Contains("audit-refined")) output = System.IO.Path.Combine(output, "schema-refined");
        if (OS.GetCmdlineUserArgs().Contains("audit-polished")) output = System.IO.Path.Combine(output, "schema-polished");
        if (OS.GetCmdlineUserArgs().Contains("audit-details-regression")) output = System.IO.Path.Combine(output, "details-regression");
        if (OS.GetCmdlineUserArgs().Contains("audit-composed-regression")) output = System.IO.Path.Combine(output, "composed-regression");
        if (OS.GetCmdlineUserArgs().Contains("npc-integration-parent-audit")) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/npc-design-integration-audit/parent"));
        if (OS.GetCmdlineUserArgs().Contains("vendor-parent-audit")) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/npc-vendor-audit/parent"));
        if (OS.GetCmdlineUserArgs().Contains("skill-parent-audit")) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/skill-window-audit/parent"));
        if (OS.GetCmdlineUserArgs().Contains("inventory-parent-audit")) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/inventory-window-audit/parent"));
        bool geometryProbe = OS.GetCmdlineUserArgs().Contains("geometry-probe");
        if (geometryProbe) output = System.IO.Path.Combine(output, "geometry-probe");
        System.IO.Directory.CreateDirectory(output);
        string prefix = nation == 1 ? "karus" : "human";
        string pluginFile = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/KnightOnlineUiClassic.dll");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-environment.json"), JsonSerializer.Serialize(new {
            renderer = RenderingServer.GetCurrentRenderingMethod(), driver = RenderingServer.GetCurrentRenderingDriverName(),
            gpu = RenderingServer.GetVideoAdapterName(), plugin = pluginFile,
            pluginModule = typeof(CharacterWindow).Module.ModuleVersionId,
            pluginHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(pluginFile))),
            viewport = "800 x 660", scale = "1:1", data = "Controlled fixtures; no server login"
        }, new JsonSerializerOptions { WriteIndented = true }));
        var actualPages = (Dictionary<string, LayoutView>)typeof(CharacterWindow).GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actual)!;
        var lists = (Dictionary<string, CharacterList>)typeof(CharacterWindow).GetField("_lists", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actual)!;
        LayoutView? referenceFrame = null, referencePage = null;
        foreach (string page in new[] { "character", "quest", "clan", "friends" })
        {
            referenceFrame?.QueueFree(); referencePage?.QueueFree();
            fixture.SelectPage(page);
            typeof(CharacterWindow).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(actual, null);
            var frameRoot = Plugin.Kit.Layout("{nation}_various_frame_us");
            string source = page == "character" ? "page_state_us" : page == "friends" ? "page_friends_us" : page == "clan" ? "page_clan_us" : "page_quest_us";
            var pageRoot = Plugin.Kit.Layout("{nation}_" + source);
            referenceFrame = new LayoutView(Plugin.Kit, frameRoot) { Position = new Vector2(442, 46) };
            referencePage = new LayoutView(Plugin.Kit, pageRoot) { Position = new Vector2(442 + pageRoot.X, 46 + pageRoot.Y) };
            AddChild(referenceFrame); AddChild(referencePage);
            referenceFrame.Hide("btn_knights");
            if (page == "character")
            {
                var current = actualPages[page];
                foreach (var (node, control) in referencePage.Where(n => n.IsString && n.Id.Length > 0))
                    if (control is Label label && current.Where(n => n.IsString && n.Id.Equals(node.Id, StringComparison.OrdinalIgnoreCase)).FirstOrDefault().Control is Label value)
                        label.Text = value.Text;
                referencePage.Hide("img_dex", "img_int", "img_map");
                foreach (var (node, control) in referencePage.Where(n => n.IsString && n.Text.Trim() is "STR" or "HP" && !n.Id.StartsWith("img_"))) control.Visible = false;
            }
            await Settled();
            if (geometryProbe)
            {
                // Diagnostic only: reapply declared sizes after all font/theme overrides have settled.
                foreach (var view in actual.GetChildren().OfType<LayoutView>())
                    foreach (var pair in view.Where(_ => true)) pair.Control.Size = pair.Node.SizeVec;
                await Settled();
            }
            await SaveAuditImage(output, prefix + "-" + page);
            var records = CaptureControlRecords(actual);
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-" + page + "-bounds.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            var geometry = actualPages[page].Where(n => n.Type is "classic_button" or "classic_select" or "classic_input" or "string")
                .Concat(actual.GetChildren().OfType<LayoutView>().First(v => v.Root.Id == "character_frame").Where(n => n.Type == "classic_tab"))
                .Where(p => p.Control.IsVisibleInTree()).Select(p => new {
                    id = p.Node.Id, text = p.Control is Button b ? b.Text : p.Control is Label l ? l.Text : "",
                    declaredX = p.Node.X, declaredY = p.Node.Y, declaredWidth = p.Node.W, declaredHeight = p.Node.H,
                    actualX = p.Control.GetGlobalRect().Position.X - actual.GlobalPosition.X,
                    actualY = p.Control.GetGlobalRect().Position.Y - actual.GlobalPosition.Y,
                    actualWidth = p.Control.Size.X, actualHeight = p.Control.Size.Y,
                    minimumHeight = p.Control.GetCombinedMinimumSize().Y
                });
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-" + page + "-geometry.json"), JsonSerializer.Serialize(geometry, new JsonSerializerOptions { WriteIndented = true }));
            if (lists.TryGetValue(page, out var list))
            {
                typeof(CharacterList).GetMethod("Choose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { fixture.Rows(page == "quest" ? "quests" : page)[1].Id });
                await Settled();
                await SaveAuditImage(output, prefix + "-" + page + "-selected");
                var button = list.GetChildren().OfType<Button>().ElementAt(1);
                var target = button.GetGlobalRect().GetCenter();
                Input.ParseInputEvent(new InputEventMouseMotion { Position = target, GlobalPosition = target });
                await Settled();
                await SaveAuditImage(output, prefix + "-" + page + "-selected-hover");
            }
            GD.Print("CHARACTER_AUDIT_CAPTURE " + prefix + "-" + page);
        }
        GD.Print("CHARACTER_AUDIT_OK " + output);
    }

    private async Task Settled()
    {
        await ToSignal(GetTree().CreateTimer(.35), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
    private async Task SaveAuditImage(string directory, string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var result = image.SavePng(System.IO.Path.Combine(directory, name + ".png"));
        if (result != Error.Ok) throw new Exception("Capture failed: " + result);
    }
    private static IEnumerable<object> CaptureControlRecords(Control root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is not Control control || !control.IsVisibleInTree()) continue;
            var rect = control.GetGlobalRect();
            var minimum = control.GetCombinedMinimumSize();
            yield return new { path = root.GetPathTo(control).ToString(), type = control.GetType().Name,
                x = rect.Position.X, y = rect.Position.Y, width = rect.Size.X, height = rect.Size.Y,
                minWidth = minimum.X, minHeight = minimum.Y,
                text = control is Label label ? label.Text : control is Button button ? button.Text : "",
                fontSize = control is Label or Button ? control.GetThemeFontSize("font_size") : 0,
                fontColor = control is Label or Button ? control.GetThemeColor("font_color").ToHtml() : "",
                disabled = control is BaseButton b && b.Disabled };
            foreach (var descendant in CaptureControlRecords(control)) yield return descendant;
        }
    }
}

public sealed class CharacterAuditData : IGameCharacterPanel
{
    private readonly int _nation;
    public CharacterAuditData(int nation) { _nation = nation; }
    public string SelectedPage { get; private set; } = "character";
    public string RaceName => _nation == 1 ? "Ark Tuarek" : "Barbarian";
    public string JobName => "Protector";
    public string LevelLabel => "83/5";
    public string TitleName => "Title: Street Smart";
    public byte ClanFlag { get; set; } = ClanTypes.Training;
    public MyClanInfo Clan => new() { InClan = true, ClanId = 11, Name = "canCLAN", Flag = ClanFlag,
        Fame = ClanRanks.Chief, Grade = 3, MaxMembers = 50, Online = 3, PointFund = 125000,
        Notice = "Meet at Moradon. Prepare for the next war." };
    public int QuestFilter => 1;
    public int QuestKind => 0;
    public IReadOnlyList<Window> Dialogs => Array.Empty<Window>();
    public int StatBonus(int row) => row switch { 1 => 13, 4 => 10, 2 => -8, _ => 0 };
    public string Status(string section) => section == "friends" ? "3 friends online." : "";
    public void SelectPage(string page) => SelectedPage = page;
    public void Refresh(string section) { }
    public void Act(string action, string selection = "", string value = "") { }
    public IReadOnlyList<GamePanelRow> Rows(string section) => section switch
    {
        "clan" => new[] {
            new GamePanelRow("Canbo", new[] { "Chief", "Canbo", "83", "Warrior" }, "Chief / online", new Color("fff0c8")),
            new GamePanelRow("LongCharacterName1299", new[] { "Vice-chief", "LongCharacterName1299", "83", "Rogue" }, "Vice-chief / online", new Color("fff0c8")),
            new GamePanelRow("Guardian", new[] { "Member", "Guardian", "80", "Priest" }, "Offline for 12 hours", new Color("b5b5b5"), false),
            new GamePanelRow("Magician", new[] { "Member", "Magician", "82", "Mage" }, "Member / online", new Color("fff0c8")) },
        "friends" => new[] {
            new GamePanelRow("Guardian", new[] { "Guardian" }, "Online in Moradon", new Color("8ff099")),
            new GamePanelRow("LongCharacterName1299", new[] { "LongCharacterName1299" }, "In party", new Color("ff9292")),
            new GamePanelRow("Magician", new[] { "Magician" }, "Offline", new Color("bbbbbb"), false),
            new GamePanelRow("Ranger", new[] { "Ranger" }, "Online", new Color("8ff099")) },
        "quests" => new[] {
            new GamePanelRow("1", new[] { "A New Beginning", "In Progress", "—" }, "Talk to the Inn Hostess", Colors.White, true, true, true, false),
            new GamePanelRow("2", new[] { "Defeat the Monsters of Moradon", "Ready", "—" }, "Return to the quest NPC", new Color("fff080"), true, true, true, true, true),
            new GamePanelRow("3", new[] { "Daily Supply Delivery", "In Progress", "—" }, "Daily reset is not a time limit", Colors.White, true, true, true),
            new GamePanelRow("4", new[] { "Path of the Warrior", "Completed", "—" }, "Completed quest", new Color("bbbbbb")) },
        _ => Array.Empty<GamePanelRow>()
    };
    public object? CharacterValue(string name, Type type, object?[]? args)
    {
        if (type == typeof(void)) return null;
        if (type == typeof(string)) return name switch { "get_Name" => "Canbo", "get_ClassName" => "Warrior", "get_NationName" => _nation == 1 ? "Karus" : "El Morad", _ => "" };
        if (type == typeof(long)) return name == "get_Exp" ? 46400L : 8705986960L;
        if (type == typeof(double)) return .00053297d;
        if (type == typeof(int)) return name switch {
            "get_Nation" => _nation, "get_Class" => _nation == 1 ? 105 : 205, "get_Level" => 83,
            "get_Hp" => 2295, "get_MaxHp" => 7836, "get_Mp" or "get_MaxMp" => 7966,
            "get_Weight" => 877, "get_MaxWeight" => 16900, "get_Str" or "get_Sta" or "get_Intel" or "get_Mag" => 255,
            "get_Dex" => 60, "get_Points" => 37, "get_Ap" => 2103, "get_Ac" => 238, "get_Np" => 2025525525,
            "Resist" => 120 + (int)args![0]! * 10, _ => 0 };
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
