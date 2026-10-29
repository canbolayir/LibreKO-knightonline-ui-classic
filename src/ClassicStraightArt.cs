using Godot;
namespace KnightOnlineUiClassic;

/// <summary>
/// Straightens the silver double-line frames of original King and siege list windows. The original pieces are
/// composed once into one image, then each corner that carries curls or a crest is rebuilt from the straight
/// lines next to it: a strip repeats a clean row or column of the same art, and the corner square joins the
/// outer and inner lines as the clean corners do. Lines, fills and proportions stay those of the original art.
/// </summary>
public static class ClassicStraightArt
{
    public abstract record Op;
    /// <summary>Every column of the rectangle repeats column <paramref name="SourceX"/> (horizontal lines run on).</summary>
    public sealed record CopyH(Rect2I Rect, int SourceX) : Op;
    /// <summary>Every row of the rectangle repeats row <paramref name="SourceY"/> (vertical lines run on).</summary>
    public sealed record CopyV(Rect2I Rect, int SourceY) : Op;
    /// <summary>
    /// Square corner of nested double lines at (X, Y), growing by (DirX, DirY). Horizontal lines are read from
    /// column <paramref name="SourceX"/>, vertical lines from row <paramref name="SourceY"/>; the k-th line from the
    /// edge in one direction meets the k-th line in the other.
    /// </summary>
    public sealed record Corner(int X, int Y, int DirX, int DirY, int Size, int SourceX, int SourceY) : Op;

    private static readonly Dictionary<string, ImageTexture> Cache = new();

    internal static void Clear()
    {
        foreach (var texture in Cache.Values) texture.Dispose();
        Cache.Clear();
    }

    /// <summary>Corner repairs per original artwork, in window coordinates.</summary>
    public static Op[]? For(string art) => art switch
    {
        // Election list: curls in the upper-left and lower-right corners; the lower right also crosses the
        // candidate list corner, the footer rails and the last footer cell.
        "co_king_electionlist_us" => new Op[]
        {
            // The art's outer line starts one pixel in (column and row 0 are transparent).
            new CopyV(new(1, 9, 15, 60), 151), new CopyH(new(9, 1, 72, 50), 101), new Corner(1, 1, 1, 1, 8, 101, 151),
            // The list's right edge is three columns (line, highlight, shadow: x 304..306).
            new CopyV(new(307, 263, 15, 60), 201), new CopyH(new(265, 271, 42, 52), 161), new CopyH(new(265, 263, 39, 8), 161),
            new CopyV(new(304, 263, 3, 8), 201), new CopyH(new(307, 296, 6, 6), 161), new CopyH(new(265, 323, 48, 8), 161),
            new Corner(320, 330, -1, -1, 8, 161, 201),
        },
        // Nominee board and nation introduction: curls in the upper-left and lower-right corners.
        "co_king_electionboard_us" => new Op[]
        {
            new CopyV(new(0, 8, 24, 63), 150), new CopyH(new(8, 0, 63, 29), 200), new Corner(0, 0, 1, 1, 8, 200, 150),
            new CopyV(new(385, 225, 22, 62), 150), new CopyV(new(384, 225, 1, 24), 150), new CopyH(new(340, 249, 45, 38), 330),
            new CopyH(new(385, 259, 15, 6), 330),
            new CopyH(new(340, 287, 60, 8), 330), new Corner(406, 294, -1, -1, 8, 330, 150),
        },
        // Siege lists: the crest and curls around the close button in the upper-right corner.
        "co_warfareschedule_us" or "co_warfarepetition_us" or "co_castleunion_us" => new Op[]
        {
            new CopyV(new(270, 0, 16, 49), 150), new CopyH(new(200, 0, 78, 49), 195), new Corner(283, 0, -1, 1, 8, 195, 150),
            // The lowest curl crosses the outer line just below the title rail.
            new CopyV(new(280, 49, 6, 8), 150),
        },
        _ => null,
    };

    /// <summary>Composes the pieces (texture, destination, source) into one straightened texture.</summary>
    public static ImageTexture Compose(string key, Vector2I size, Rect2 backing, IEnumerable<(Texture2D Texture, Rect2 Rect, Rect2 Source)> pieces, Op[] ops)
    {
        if (Cache.TryGetValue(key, out var cached) && GodotObject.IsInstanceValid(cached)) return cached;
        using var image = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
        image.FillRect(new Rect2I((Vector2I)backing.Position, (Vector2I)backing.Size), Colors.Black);
        var sources = new Dictionary<Texture2D, Image>();
        foreach (var (texture, rect, source) in pieces)
        {
            if (!sources.TryGetValue(texture, out var atlas))
            {
                atlas = texture.GetImage();
                if (atlas.IsCompressed()) atlas.Decompress();
                atlas.Convert(Image.Format.Rgba8);
                sources[texture] = atlas;
            }
            var src = new Rect2I((Vector2I)source.Position, (Vector2I)source.Size);
            var dst = new Rect2I((Vector2I)rect.Position, (Vector2I)rect.Size);
            if (src.Size.X <= 0 || src.Size.Y <= 0 || dst.Size.X <= 0 || dst.Size.Y <= 0) continue;
            using var piece = atlas.GetRegion(src);
            // The panels draw with nearest filtering; stretched filler strips scale the same way here.
            if (src.Size != dst.Size) piece.Resize(dst.Size.X, dst.Size.Y, Image.Interpolation.Nearest);
            image.BlendRect(piece, new Rect2I(Vector2I.Zero, dst.Size), dst.Position);
        }
        foreach (var op in ops) Apply(image, op);
        foreach (var atlas in sources.Values) atlas.Dispose();
        var result = ImageTexture.CreateFromImage(image);
        Cache[key] = result;
        return result;
    }

    private static void Apply(Image image, Op op)
    {
        int w = image.GetWidth(), h = image.GetHeight();
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < w && y < h;
        switch (op)
        {
            case CopyH copy:
                for (int y = copy.Rect.Position.Y; y < copy.Rect.End.Y; y++)
                {
                    if (!Inside(copy.SourceX, y)) continue;
                    var colour = image.GetPixel(copy.SourceX, y);
                    for (int x = copy.Rect.Position.X; x < copy.Rect.End.X; x++) if (Inside(x, y)) image.SetPixel(x, y, colour);
                }
                break;
            case CopyV copy:
                for (int x = copy.Rect.Position.X; x < copy.Rect.End.X; x++)
                {
                    if (!Inside(x, copy.SourceY)) continue;
                    var colour = image.GetPixel(x, copy.SourceY);
                    for (int y = copy.Rect.Position.Y; y < copy.Rect.End.Y; y++) if (Inside(x, y)) image.SetPixel(x, y, colour);
                }
                break;
            case Corner corner:
                {
                    // Line groups counted from the edge: rows of horizontal lines and columns of vertical lines.
                    int[] Groups(Func<int, Color> sample)
                    {
                        var index = new int[corner.Size]; int group = -1; bool previous = false;
                        for (int i = 0; i < corner.Size; i++)
                        {
                            bool line = IsLine(sample(i));
                            if (line && !previous) group++;
                            index[i] = line ? group : -1; previous = line;
                        }
                        return index;
                    }
                    Color Row(int j) => image.GetPixel(corner.SourceX, corner.Y + corner.DirY * j);
                    Color Column(int i) => image.GetPixel(corner.X + corner.DirX * i, corner.SourceY);
                    var rows = Groups(Row); var columns = Groups(Column);
                    int Start(int[] groups, int k) { for (int i = 0; i < groups.Length; i++) if (groups[i] == k) return i; return int.MaxValue; }
                    for (int j = 0; j < corner.Size; j++)
                        for (int i = 0; i < corner.Size; i++)
                        {
                            int x = corner.X + corner.DirX * i, y = corner.Y + corner.DirY * j;
                            if (!Inside(x, y)) continue;
                            Color colour;
                            if (rows[j] >= 0 && i >= Start(columns, rows[j])) colour = Row(j);
                            else if (columns[i] >= 0 && j >= Start(rows, columns[i])) colour = Column(i);
                            else colour = rows[j] < 0 ? Row(j) : columns[i] < 0 ? Column(i) : Colors.Black;
                            image.SetPixel(x, y, colour);
                        }
                    break;
                }
        }
    }

    private static bool IsLine(Color colour) => colour.A > .5f && colour.R + colour.G + colour.B > 1.2f;
}
