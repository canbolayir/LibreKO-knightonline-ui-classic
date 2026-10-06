using Godot;

namespace KnightOnlineUiClassic.Layout;

public partial class DrawnText : Control
{
    private static readonly Color ShadowColor = new(0, 0, 0, 0.75f);
    private static readonly Vector2 ShadowOffset = new(1, 1);

    private readonly Font _font;
    private readonly int _size;
    private readonly Color _color;
    private string _text = "";

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            QueueRedraw();
        }
    }

    public DrawnText(Font font, int size, Color color)
    {
        _font = font;
        _size = size;
        _color = color;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (_text.Length == 0) return;
        var extent = _font.GetStringSize(_text, HorizontalAlignment.Left, -1, _size);
        var at = new Vector2(Mathf.Round((Size.X - extent.X) * 0.5f), Mathf.Round((Size.Y - extent.Y) * 0.5f + _font.GetAscent(_size)));
        DrawString(_font, at + ShadowOffset, _text, HorizontalAlignment.Left, -1, _size, ShadowColor);
        DrawString(_font, at, _text, HorizontalAlignment.Left, -1, _size, _color);
    }
}
