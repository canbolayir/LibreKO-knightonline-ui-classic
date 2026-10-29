using System.Text.Json;
using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic;

/// <summary>
/// The 2.x-era window art bundled with the plugin (<c>assets/myko</c>): converted UIF layouts and their textures for
/// the Power-Up Store, the quest NPC windows and the HUD pieces. The root is, in order: the folder named by
/// <see cref="RootVariable"/> (used as given, so an empty folder turns the art off), an <c>assets-local/</c> folder
/// next to the installed plugin (an untracked developer override), or the bundled folder. Windows whose layout or one
/// of its textures is missing keep their Classic design.
/// </summary>
public sealed class LocalAssets
{
    public const string Folder = "assets-local";
    public const string Bundled = "assets/myko";
    public const string RootVariable = "KNIGHTONLINE_UI_CLASSIC_LOCAL_ASSETS";
    private readonly Dictionary<string, LayoutNode?> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image?> _images = new(StringComparer.OrdinalIgnoreCase);
    public string Root { get; }

    public LocalAssets(string pluginRoot)
    {
        string? overridden = System.Environment.GetEnvironmentVariable(RootVariable);
        string developer = Path.Combine(pluginRoot, Folder);
        Root = Path.GetFullPath(!string.IsNullOrWhiteSpace(overridden) ? overridden
            : Directory.Exists(developer) ? developer
            : Path.Combine(pluginRoot, Bundled));
    }

    /// <summary>The layout, or null when it (or one of its textures) is missing.</summary>
    public LayoutNode? Layout(string name)
    {
        if (_layouts.TryGetValue(name, out var cached)) return cached;
        LayoutNode? node = null;
        string path = Path.Combine(Root, "layouts", name + ".json");
        try
        {
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                node = LayoutNode.Parse(doc.RootElement);
                if (node.All().Any(n => n.Texture != null && !File.Exists(TexturePath(n.Texture)))) node = null;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { Plugin.Kit.Log.Warn($"local layout {name}: {e.Message}"); node = null; }
        return _layouts[name] = node;
    }

    public Texture2D? Texture(string png)
    {
        if (_textures.TryGetValue(png, out var cached)) return cached;
        var image = Image(png);
        return _textures[png] = image == null ? null : ImageTexture.CreateFromImage(image);
    }

    public Image? Image(string png)
    {
        if (_images.TryGetValue(png, out var cached)) return cached;
        Image? image = null;
        string path = TexturePath(png);
        if (File.Exists(path))
        {
            image = new Image();
            if (image.LoadPngFromBuffer(File.ReadAllBytes(path)) != Error.Ok) { image.Dispose(); image = null; }
        }
        return _images[png] = image;
    }

    public void Release()
    {
        foreach (var texture in _textures.Values) texture?.Dispose();
        foreach (var image in _images.Values) image?.Dispose();
        _textures.Clear();
        _images.Clear();
        _layouts.Clear();
    }

    private string TexturePath(string png) => Path.Combine(Root, "textures", Path.GetFileName(png));
}
