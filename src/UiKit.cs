using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic;

public sealed class UiKit
{
    public const string LayoutDir = "assets/layouts";
    public const string TextureDir = "assets/textures";
    public const string IndexFile = "assets/index.json";
    public const int FontSizeBoost = 0;
    public const float BoldStrength = 0.6f;

    public PluginContext Context { get; }
    public PluginGame Game => Context.Game;
    public int Nation => Game.Available ? Game.Character.Nation : LibreKO.Network.Net.I?.LastEnter.Nation ?? 2;
    public PluginLog Log => Context.Log;
    public int LayoutCount { get; }
    public int TextureCount { get; }

    private readonly Dictionary<string, LayoutNode> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private Font? _regular;
    private Font? _bold;
    private readonly Dictionary<string,Font> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,float> _metrics = new();

    public UiKit(PluginContext context)
    {
        Context = context;
        using var metrics=context.Assets.Json("assets/theme.json");
        if(metrics != null)
            foreach(var field in metrics.RootElement.EnumerateObject())
                if(field.Value.ValueKind==JsonValueKind.Number) _metrics[field.Name]=field.Value.GetSingle();
        using var index = context.Assets.Json(IndexFile);
        if (index != null)
        {
            var root = index.RootElement;
            if (root.TryGetProperty("layouts", out var layouts)) LayoutCount = layouts.EnumerateObject().Count();
            if (root.TryGetProperty("textures", out var textures)) TextureCount = textures.EnumerateObject().Count();
        }
    }

    public LayoutNode Layout(string name)
    {
        name = name.Replace("{nation}", Nation == 1 ? "ka" : "el");
        if (_layouts.TryGetValue(name, out var cached)) return cached;
        using var doc = Context.Assets.Json($"{LayoutDir}/{name}.json")
                        ?? throw new FileNotFoundException($"layout {name} is missing from {LayoutDir}");
        var json = JsonNode.Parse(doc.RootElement.GetRawText())!;
        // Patches target stable IDs; generated source remains untouched.
        using var edits = Context.Assets.Json($"assets/overrides/{name}.json");
        if (edits != null)
            foreach (var patch in edits.RootElement.EnumerateArray())
            {
                string id = patch.GetProperty("id").GetString() ?? "";
                void Apply(JsonNode current)
                {
                    if (current["id"]?.GetValue<string>() == id &&
                        (!patch.TryGetProperty("type", out var type) || current["type"]?.GetValue<string>() == type.GetString()))
                        foreach (var property in patch.GetProperty("set").EnumerateObject())
                            current[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                    if (current["children"] is JsonArray children)
                        foreach (var child in children) if (child != null) Apply(child);
                }
                Apply(json);
            }
        using var merged = JsonDocument.Parse(json.ToJsonString());
        var node = LayoutNode.Parse(merged.RootElement);
        _layouts[name] = node;
        return node;
    }

    public Texture2D? Texture(string png) => Context.Assets.Texture($"{TextureDir}/{png}");
    public float Metric(string name,float fallback) => _metrics.TryGetValue(name,out var value) && float.IsFinite(value) ? value : fallback;

    public Font ChatFont => ThemeDB.FallbackFont;
    public Font ChatStrong => LibreKO.UiTheme.Strong;

    public Font Regular => _regular ??= SystemTypeface("Arial",false,false);

    public Font Bold => _bold ??= SystemTypeface("Arial",true,false);

    private Font SystemTypeface(string family,bool bold,bool italic)
    {
        string key=$"{family}:{bold}:{italic}";
        if (!_fonts.TryGetValue(key,out var font)) {
            font=new SystemFont { FontNames=new[]{family,"Arial"}, FontWeight=bold ? 700:400, FontItalic=italic };
            _fonts[key]=font;
        }
        return font;
    }
    public Font FontFor(LayoutNode node) => SystemTypeface(string.IsNullOrWhiteSpace(node.Font) ? "Arial" : node.Font,node.Bold,node.Italic);

    public static int FontSize(LayoutNode node) => Math.Max(8, (int)Math.Round(node.Size * 96.0 / 72.0, MidpointRounding.AwayFromZero) + (int)Plugin.Kit.Metric("fontSizeBoost", FontSizeBoost));
}
