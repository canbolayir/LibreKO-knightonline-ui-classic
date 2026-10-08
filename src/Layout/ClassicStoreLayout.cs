using Godot;
namespace KnightOnlineUiClassic.Layout;

public static class ClassicStoreLayout
{
    public const int HeaderHeight = 39;
    public const int Inset = 16;
    public const int BodyTop = 50;
    public static readonly Vector2 Chrome = new(32, 66);
    public static Rect2 Close(float width) => new(width - 147, 12, 125, 22);
    public static Rect2 CloseBacking(float width) => new(width - 151, 8, 133, 30);
    public static Rect2 Body(Vector2 size) => new(new Vector2(Inset, BodyTop), size - Chrome);
    public static StyleBoxFlat Box(int margin = 8, bool active = false)
    {
        var box = new StyleBoxFlat { BgColor = active ? new Color("211b11") : Colors.Black, BorderColor = new Color(active ? "cfb575" : "7e6b48") };
        box.SetBorderWidthAll(1); box.SetContentMarginAll(margin); return box;
    }
    public static StyleBoxTexture IconBox() => new()
    {
        Texture = Plugin.Kit.Texture("ui_message_us.png"), RegionRect = new Rect2(15, 193, 50, 50),
        TextureMarginLeft = 1, TextureMarginRight = 1, TextureMarginTop = 1, TextureMarginBottom = 1,
        ContentMarginLeft = 3, ContentMarginRight = 3, ContentMarginTop = 3, ContentMarginBottom = 3, DrawCenter = false,
    };
}
