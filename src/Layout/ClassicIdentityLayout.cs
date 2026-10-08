using Godot;

namespace KnightOnlineUiClassic.Layout;

public static class ClassicIdentityLayout
{
    public static string Artwork(string id) => id switch
    {
        "creat_clan" => "co_creat_clan_us",
        "namechange" => "co_idchange_us",
        "disguise" => "co_disguisering_us",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
    public static Vector2 Size(string id) => id switch
    {
        "creat_clan" => new(327, 156),
        "namechange" => new(370, 175),
        "disguise" => new(176, 562),
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
    public static Rect2 Field(string id, string name)
    {
        var layout = Plugin.Kit.Layout(Artwork(id));
        var field = layout.Find(name) ?? throw new InvalidOperationException("Missing original identity field: " + name);
        return new Rect2(field.Position - layout.Position, field.SizeVec);
    }
}
