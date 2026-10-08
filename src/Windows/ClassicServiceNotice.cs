using Godot;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

// Original fee-dialog corners and action artwork surround live server-owned confirmations.
public partial class ClassicServiceNotice : Control
{
    private readonly DialogRequest _request;
    private readonly Control _panel = new() { Size = new Vector2(344, 196) };
    private readonly Label _message;
    private readonly bool _transfer;
    private bool _positioned, _dragging;
    private Vector2 _dragOffset;
    public ClassicServiceNotice(DialogRequest request)
    {
        _request = request; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _transfer = request.Title == "Nation Transfer";
        if (_transfer) _panel.Size = new Vector2(420, 244);
        var blocker = new ColorRect { Color = new Color(0, 0, 0, .35f), MouseFilter = MouseFilterEnum.Stop };
        blocker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(blocker); AddChild(_panel);
        _panel.Draw += DrawFrame;
        var title = new Label { Text = request.Title, Position = new Vector2(16, 14), Size = new Vector2(_panel.Size.X - 32, 20), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddChild(title); ClassicVendorSkin.Font(title); title.AddThemeColorOverride("font_color", new Color("e4c58b"));
        var messageSize = new Vector2(_panel.Size.X - 36, _panel.Size.Y - 95);
        _message = new Label { Text = request.Message, Position = new Vector2(18, 42), Size = messageSize,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _panel.AddChild(_message); ClassicVendorSkin.Font(_message); _message.AddThemeColorOverride("font_color", new Color("e4d9bf"));
        _message.Size = messageSize;
        _message.CustomMaximumSize = messageSize;
        _message.ClipContents = true;
        var art = Plugin.Kit.Layout("co_change_bill_us");
        foreach (bool accept in request.CancelText == null ? new[] { true } : new[] { true, false })
        {
            var button = new Button(); _panel.AddChild(button);
            ClassicMerchantSkin.Button(button, art.Find(accept ? "btn_ok" : "btn_cancel")!, _panel,
                new Rect2(request.CancelText == null ? (_panel.Size.X - 97) / 2 : accept ? 18 : _panel.Size.X - 115, _panel.Size.Y - 43, 97, 29), accept ? request.ConfirmText : request.CancelText ?? "Cancel");
            ClassicVendorSkin.Font(button);
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, new Color("efd9b4"));
            if (accept) button.Pressed += request.Confirm; else button.Pressed += request.Cancel;
        }
        request.MessageChanged += UpdateMessage;
        if (request.Title is "Akara's Altar" or "Nation Transfer" or "Rebirth" or "Clan Cape" or "Familiar Hatching" or "Familiar Transformation")
            foreach (var control in CharacterDetailsSkin.Tree(_panel).OfType<Control>().Where(c => c is Label or Button))
            {
                control.AddThemeFontOverride("font", Plugin.Kit.Bold);
                control.AddThemeFontSizeOverride("font_size", 13);
            }
        if (_transfer || request.Title is "Rebirth" or "Clan Cape" or "Familiar Hatching" or "Familiar Transformation") _panel.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse || mouse.Position.Y > 38) return;
            _dragging = true; _dragOffset = mouse.Position; _positioned = true; _panel.AcceptEvent();
        };
    }
    private void DrawFrame()
    {
        var texture = Plugin.Kit.Texture("ui_message_us.png");
        var size = _panel.Size;
        _panel.DrawRect(new Rect2(4, 4, size.X - 8, size.Y - 8), Colors.Black);
        void Slice(Rect2 dest, Rect2 source) => _panel.DrawTextureRectRegion(texture, dest, source);
        Slice(new Rect2(0, 0, 64, 64), new Rect2(330, 2, 64, 64));
        Slice(new Rect2(size.X - 64, 0, 64, 64), new Rect2(395, 2, 64, 64));
        Slice(new Rect2(0, size.Y - 64, 64, 64), new Rect2(330, 67, 64, 64));
        Slice(new Rect2(size.X - 64, size.Y - 64, 64, 64), new Rect2(395, 67, 64, 64));
        Slice(new Rect2(64, 0, size.X - 128, 64), new Rect2(334, 2, 60, 64));
        Slice(new Rect2(64, size.Y - 64, size.X - 128, 64), new Rect2(334, 67, 60, 64));
        Slice(new Rect2(0, 64, 64, size.Y - 128), new Rect2(330, 6, 64, 60));
        Slice(new Rect2(size.X - 64, 64, 64, size.Y - 128), new Rect2(395, 6, 64, 60));
    }
    private void UpdateMessage(string text) => _message.Text = text;
    public override void _ExitTree() => _request.MessageChanged -= UpdateMessage;
    public override void _Process(double delta)
    {
        var room = GetViewportRect().Size;
        if (!_positioned) _panel.Position = ((room - _panel.Size) / 2).Round();
        else _panel.Position = _panel.Position.Clamp(Vector2.Zero, (room - _panel.Size).Max(Vector2.Zero)).Round();
    }
    public override void _Input(InputEvent ev)
    {
        if (_dragging && ev is InputEventMouseMotion motion)
        {
            _panel.Position = (motion.Position - _dragOffset).Round(); GetViewport().SetInputAsHandled(); return;
        }
        if (_dragging && ev is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        { _dragging = false; GetViewport().SetInputAsHandled(); return; }
        if (ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode is Key.Enter or Key.KpEnter) _request.Confirm();
        else if (key.Keycode == Key.Escape) _request.Cancel(); else return;
        GetViewport().SetInputAsHandled();
    }
}
