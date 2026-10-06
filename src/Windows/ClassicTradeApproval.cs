using Godot;
using LibreKO.Domain;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

/// <summary>A final trade approval; native callbacks revalidate before sending the transaction.</summary>
public partial class ClassicTradeApproval : CanvasLayer
{
    private readonly Control _shop, _panel;
    private readonly Callable _accept, _cancel;
    private readonly ulong _openedFrame = Engine.GetProcessFrames();
    private bool _finished;
    public Rect2 PanelBounds => _panel.GetGlobalRect();

    public ClassicTradeApproval(Control shop, Godot.Collections.Dictionary details, Callable accept, Callable cancel)
    {
        Layer = 220; _shop = shop; _accept = accept; _cancel = cancel;
        var blocker = new ColorRect { Color = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Stop };
        blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(blocker);
        _panel = new Control { Name = "trade_approval_panel", Size = ClassicTradeApprovalLayout.Size, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_panel); _panel.AddChild(new ClassicTradeApprovalFrame { Size = _panel.Size });
        bool buy = details["buy"].AsBool();
        string itemName = details["item_name"].AsString(), currency = details["currency"].AsString();
        Label Text(string name, string text, Rect2 bounds, int size = 12, Color? ink = null, bool right = false)
        {
            var label = ClassicNpcLayout.Text(text, size, true, ink ?? ClassicDesign.Text);
            label.Name = name; label.AddThemeFontOverride("font", Plugin.Kit.Bold); label.ClipText = true;
            label.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _panel.AddChild(label); ClassicNpcLayout.Place(label, bounds);
            Callable.From(() => label.Size = bounds.Size).CallDeferred(); return label;
        }
        Text("trade_title", buy ? "Confirm purchase" : "Confirm sale", ClassicTradeApprovalLayout.Title, 13, new Color("c0c0c0")).HorizontalAlignment = HorizontalAlignment.Center;
        var label = Text("trade_item_name", itemName, ClassicTradeApprovalLayout.ItemName);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.MaxLinesVisible = 2;
        label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.TooltipText = itemName; label.MouseFilter = Control.MouseFilterEnum.Pass;
        var icon = new TextureRect { Name = "trade_item_icon", Texture = ItemData.Icon(details["item_id"].AsInt32()),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Pass, TooltipText = itemName };
        _panel.AddChild(icon); ClassicNpcLayout.Place(icon, ClassicTradeApprovalLayout.Icon);
        Text("trade_quantity_caption", "Quantity", ClassicTradeApprovalLayout.QuantityCaption, 12, ClassicDesign.Muted);
        Text("trade_quantity", details["quantity"].AsInt32().ToString("n0"), ClassicTradeApprovalLayout.QuantityValue, right: true);
        Text("trade_total_caption", buy ? "You pay" : "You receive", ClassicTradeApprovalLayout.TotalCaption, 12, ClassicDesign.Muted);
        Text("trade_total", $"{(buy ? "−" : "+")}{details["total"].AsInt64():n0} {currency}", ClassicTradeApprovalLayout.TotalValue,
            18, new Color("f2ce75"), right: true);
        if (currency == "gold")
        {
            var coinArt = Plugin.Kit.Layout("{nation}_transaction_us").Find("btn_gold")!.Images.First(n => n.Tag == 0);
            var coin = new TextureRect { Name = "trade_total_coin", Texture = new AtlasTexture { Atlas = Plugin.Kit.Texture(coinArt.Texture!),
                Region = new Rect2(coinArt.SrcX, coinArt.SrcY, coinArt.SrcW, coinArt.SrcH) },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore };
            _panel.AddChild(coin); ClassicNpcLayout.Place(coin, ClassicTradeApprovalLayout.Coin);
        }
        void Button(string name, string text, Rect2 bounds, System.Action action)
        {
            var button = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None };
            _panel.AddChild(button); ClassicTradeQuantity.StyleButton(button, name == "btn_ok" ? "ok" : "cancel");
            ClassicNpcLayout.Place(button, bounds);
            Callable.From(() => button.Size = bounds.Size).CallDeferred(); button.Pressed += action;
        }
        Button("btn_ok", ClassicDesign.Karus ? "O K" : "O  K", ClassicTradeApprovalLayout.Confirm, () => Finish(true));
        Button("btn_cancel", "Cancel", ClassicTradeApprovalLayout.Cancel, Cancel);
    }

    public void Cancel() => Finish(false);
    private void Finish(bool accepted)
    {
        if (_finished) return;
        _finished = true; Visible = false; QueueFree();
        if (accepted) _accept.Call(); else _cancel.Call();
    }
    public override void _Process(double delta)
    {
        if (!_shop.IsVisibleInTree()) { Cancel(); return; }
        _panel.Position = (_shop.GetGlobalRect().GetCenter() - _panel.Size / 2).Round()
            .Clamp(Vector2.Zero, (GetViewport().GetVisibleRect().Size - _panel.Size).Max(Vector2.Zero));
    }
    public override void _ExitTree() { if (!_finished) { _finished = true; _cancel.Call(); } }
    public override void _Input(InputEvent ev)
    {
        if (_finished || Engine.GetProcessFrames() <= _openedFrame || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode is Key.Enter or Key.KpEnter) { GetViewport().SetInputAsHandled(); Finish(true); }
        else if (key.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); Cancel(); }
    }
}

public partial class ClassicTradeApprovalFrame : ClassicQuantityFrame
{
    public ClassicTradeApprovalFrame() { DrawAmountField = false; }
    public override void _Draw()
    {
        base._Draw();
        DrawRect(ClassicTradeApprovalLayout.Separator, new Color("81796c"));
    }
}
