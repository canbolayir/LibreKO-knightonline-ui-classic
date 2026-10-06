using Godot;
using LibreKO.Domain;
using KnightOnlineUiClassic;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

public partial class Preview
{
    private async Task CaptureHuntIconOptions(int nation)
    {
        string output = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/hunt-icon-options"));
        System.IO.Directory.CreateDirectory(output);
        foreach (string pack in new[] { "build/client/LibreKO.pck", "build/client/source-content/knightonline.pck" })
            if (!ProjectSettings.LoadResourcePack(System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../" + pack)), false)) throw new Exception("Missing artwork resource pack: " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size = new Vector2I(1000, 760);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 760), MouseFilter = MouseFilterEnum.Ignore });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        SkillData.EnsureLoaded();
        ItemData.EnsureLoaded();
        if (OS.GetCmdlineUserArgs().Contains("icon-catalog"))
        {
            var catalog = new List<(string Key, string Name, string Path, Texture2D Texture)>();
            foreach (var skill in SkillData.All.Where(s => Regex.IsMatch(s.Name, "slash|scream|howling|cleave|sword|attack|strike|stab|pierc|shot|hunt|curse|target|soul|thrust", RegexOptions.IgnoreCase)).GroupBy(s => s.Name).Select(g => g.First()).Take(54))
                if (SkillData.Icon(skill.Id) is { } icon) catalog.Add(("S" + skill.Id, skill.Name, $"res://assets/skills/icons/{skill.Id}.png", icon));
            var items = (Dictionary<int, ItemData.Item>)typeof(ItemData).GetField("_items", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            foreach (var item in items.Values.Where(i => Regex.IsMatch(i.Name, "monster stone|skull|fang|claw|bone", RegexOptions.IgnoreCase)).GroupBy(i => i.Icon).Select(g => g.First()).Take(14))
                if (ResourceLoader.Exists($"res://assets/items/icons/{item.Icon}.png")) catalog.Add(("I" + item.Id, item.Name, $"res://assets/items/icons/{item.Icon}.png", ItemData.Icon(item.Id)));
            for (int i = 0; i < catalog.Count; i++)
            {
                var part = catalog[i]; int x = 20 + i % 10 * 96, y = 10 + i / 10 * 100;
                AddChild(new TextureRect { Position = new Vector2(x, y), Size = new Vector2(48, 48), Texture = part.Texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest });
                DesignText(this, "asset_" + i, part.Key, x, y + 51, 90, 18, size: 10);
                DesignText(this, "name_" + i, part.Name, x, y + 70, 90, 25, wrap: true, size: 10);
            }
            for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(output, "source-catalog.png"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "source-catalog.json"), JsonSerializer.Serialize(catalog.Select(p => new { key = p.Key, name = p.Name, path = p.Path }), new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("HUNT_ICON_CATALOG_OK: " + catalog.Count); return;
        }
        await RenderHuntIconCandidates(output, nation);
    }
    private async Task RenderHuntIconCandidates(string output, int nation)
    {
        GetWindow().Size = new Vector2I(1160, 640);
        foreach (var background in GetChildren().OfType<ColorRect>()) background.Size = new Vector2(1160, 640);
        var choices = new (int Number, string Name, string Description, int Id, bool Skill)[] {
            (1, "Crossed Blades", "Clear combat symbol with strong gold contrast.", 106570, true),
            (2, "Hunter Silhouette", "An attacking silhouette with a warm orange glow.", 105545, true),
            (3, "Blue Target", "A blue targeting mark that stays distinct at 32 px.", 108562, true),
            (4, "Skull", "A gold-edged skull with clear dark eye sockets.", 392012000, false),
            (5, "Monster Claw", "A pale monster claw; quieter and less colorful.", 379008000, false),
            (6, "Metal Blade", "A simple metal blade with a neutral background.", 102005, true),
        };
        _designBounds.Clear();
        var panels = new List<(Control Root, int Number)>();
        var sourceList = new List<object>();
        foreach (var candidate in choices)
        {
            Texture2D? texture = candidate.Skill ? SkillData.Icon(candidate.Id) : ItemData.Icon(candidate.Id);
            if (texture == null) throw new Exception("Missing candidate artwork: " + candidate.Name);
            int iconId = candidate.Skill ? candidate.Id : ItemData.Get(candidate.Id)!.Icon;
            string source = $"res://assets/{(candidate.Skill ? "skills" : "items")}/icons/{iconId}.png";
            if (!ResourceLoader.Exists(source)) throw new Exception("A candidate resolved to placeholder artwork: " + source);
            int index = candidate.Number - 1;
            var root = new Control { Name = "candidate_" + candidate.Number, Position = new Vector2(12 + index % 3 * 380, 12 + index / 3 * 310), Size = new Vector2(363, 292) };
            AddChild(root); panels.Add((root, candidate.Number));
            DesignPlace(root, new ClassicFrame { BackgroundColor = Colors.Black, BackgroundAlpha = 1 }, "frame", 0, 0, 363, 292);
            DesignButton(root, "a", "name_plate", candidate.Number + ".  " + candidate.Name, 18, 22, 327, 32).MouseFilter = MouseFilterEnum.Ignore;
            DesignPlace(root, new TextureRect { Texture = texture, TextureFilter = TextureFilterEnum.Nearest, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore }, "enlarged_icon", 24, 72, 64, 64);
            DesignText(root, "description", candidate.Description, 106, 72, 235, 64, wrap: true, size: 12);
            DesignText(root, "size_label", "Hunt cards · Actual icon size: 32 px", 20, 139, 320, 18, true, ClassicReportDesign.Caption, size: 11);
            string[] targets = { "Paramun", "Doom Soldier", "Troll Berserker", "Giant Golem" };
            for (int i = 0; i < targets.Length; i++)
            {
                int width = i % 2 == 0 ? 160 : 161;
                var card = new Control { MouseFilter = MouseFilterEnum.Ignore };
                DesignPlace(root, card, "monster_" + i, 18 + i % 2 * 166, 162 + i / 2 * 48, width, 42);
                DesignInset(card, "a", "border", 0, 0, width, 42);
                DesignPlace(card, new TextureRect { Texture = texture, TextureFilter = TextureFilterEnum.Nearest,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore }, "icon", 6, 5, 32, 32);
                DesignText(card, "monster_name", targets[i], 43, 5, width - 50, 17, size: 11);
                DesignText(card, "progress", "0 / 10", 43, 23, width - 50, 14, true, DesignGold, size: 11);
            }
            sourceList.Add(new { candidate.Number, candidate.Name, candidate.Description, source, originalUse = candidate.Skill ? "Skill artwork" : "Item artwork", sourceId = candidate.Id });
        }
        for (int frame = 0; frame < 8; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        foreach (var pair in _designBounds) { pair.Control.Position = pair.Bounds.Position; pair.Control.Size = pair.Bounds.Size; }
        for (int frame = 0; frame < 2; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var checks = new List<object>();
        foreach (var pair in _designBounds)
        {
            var actual = pair.Control.GetRect();
            bool exact = (actual.Position - pair.Bounds.Position).Length() < .1f && (actual.Size - pair.Bounds.Size).Length() < .1f;
            bool enclosed = pair.Control.GetParent() is not Control parent || new Rect2(Vector2.Zero, parent.Size).Grow(.1f).Encloses(actual);
            bool fits = pair.Control is not Label label || label.GetMinimumSize().Y <= actual.Size.Y + .1f;
            if (pair.Control is Label single && single.AutowrapMode == TextServer.AutowrapMode.Off)
                fits &= single.GetThemeFont("font").GetStringSize(single.Text, HorizontalAlignment.Left, -1, single.GetThemeFontSize("font_size")).X <= actual.Size.X + .1f;
            if (!exact || !enclosed || !fits) throw new Exception("Icon option geometry failed: " + pair.Control.Name + " " + actual);
            checks.Add(new { name = pair.Control.GetPath().ToString(), actual = actual.ToString(), exact, enclosed, textFits = fits });
        }
        string prefix = nation == 1 ? "karus" : "human";
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(output, prefix + "-options.png"));
        foreach (var panel in panels)
        {
            var rect = panel.Root.GetGlobalRect();
            using var cropped = image.GetRegion(new Rect2I((int)rect.Position.X - 8, (int)rect.Position.Y - 8, 379, 308));
            cropped.SavePng(System.IO.Path.Combine(output, prefix + "-option-" + panel.Number + ".png"));
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-verification.json"), JsonSerializer.Serialize(new { nation = prefix, previewOnly = true, candidates = sourceList, controls = checks }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("HUNT_ICON_OPTIONS_OK: " + prefix + ", 6 alternatives, original artwork, actual 32px card context, bounds and text fit");
    }
}
