using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Shows a <see cref="MoneyEdit"/> amount without thousands separators, as the original edit boxes do.
/// The client formats with separators both when the player types and when a value is assigned.
/// </summary>
public partial class PlainDigits : Node
{
    private readonly MoneyEdit _edit;

    public PlainDigits() { _edit = null!; }

    private PlainDigits(MoneyEdit edit)
    {
        _edit = edit;
        Name = "plain_digits";
        ProcessPriority = int.MinValue;
    }

    public static void Attach(MoneyEdit edit)
    {
        if (edit.HasNode("plain_digits")) return;
        var plain = new PlainDigits(edit);
        edit.AddChild(plain);
        edit.TextChanged += _ => plain.Strip();
        plain.Strip();
    }

    public override void _Process(double delta)
    {
        if (_edit != null && _edit.IsVisibleInTree()) Strip();
    }

    private void Strip()
    {
        string text = _edit.Text;
        if (text.All(char.IsDigit)) return;
        int caret = 0;
        for (int i = 0; i < _edit.CaretColumn && i < text.Length; i++)
            if (char.IsDigit(text[i])) caret++;
        bool selectedAll = _edit.HasSelection() && _edit.GetSelectedText() == text;
        _edit.Text = new string(text.Where(char.IsDigit).ToArray());
        _edit.CaretColumn = caret;
        if (selectedAll) _edit.SelectAll();
    }
}
