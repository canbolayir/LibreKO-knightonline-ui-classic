using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicStoragePin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static Control? Apply(Control body)
    {
        Node? owner = body;
        while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_storage_pin")) return null;
        window.SetMeta("classic_storage_pin", true);
        var nodes = CharacterDetailsSkin.Tree(body).ToArray();
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var panel = new Control { Name = "classic_storage_pin", Size = ClassicQuantityLayout.Size, CustomMinimumSize = ClassicQuantityLayout.Size };
        window.AddChild(panel); panel.AddChild(new ClassicStoragePinFrame { Size = panel.Size, DrawAmountField = false });
        var message = nodes.OfType<Label>().Single();
        ClassicVendorSkin.Move(message, panel, ClassicQuantityLayout.Message); ClassicMerchantSkin.Font(message);
        message.HorizontalAlignment = HorizontalAlignment.Center; message.AutowrapMode = TextServer.AutowrapMode.Off;
        message.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var input = nodes.OfType<LineEdit>().Single();
        var inputRect = ClassicQuantityLayout.Amount;
        inputRect.Position += new Vector2(ClassicStoragePinFrame.FieldX - ClassicQuantityLayout.InputFrame.Position.X, 0);
        ClassicVendorSkin.Move(input, panel, inputRect); ClassicMerchantSkin.Font(input);
        input.Alignment = HorizontalAlignment.Center; input.AddThemeColorOverride("font_color", new Color("ffff00"));
        foreach (string state in new[] { "normal", "focus", "read_only" }) input.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        foreach (var (name, rect, art) in new[] {
            ("vault_pin_confirm", ClassicQuantityLayout.Confirm, "ok"),
            ("vault_pin_cancel", ClassicQuantityLayout.Cancel, "cancel") })
        {
            var button = nodes.OfType<Button>().Single(b => b.Name == name);
            ClassicVendorSkin.Move(button, panel, rect); ClassicTradeQuantity.StyleButton(button, art);
            if (art == "ok") button.Text = "OK";
        }
        if (window.GetParent() is VipVaultPinPrompt prompt)
        {
            prompt.GetChildren().OfType<ColorRect>().Single().Color = Colors.Transparent;
            prompt.VisibilityChanged += () => { if (prompt.Visible) window.Position = ((window.GetViewportRect().Size - panel.Size) / 2).Round(); };
        }
        window.ResetSize(); return panel;
    }
}

public partial class ClassicStoragePinFrame : ClassicQuantityFrame
{
    public static float FieldX => Mathf.Round((ClassicQuantityLayout.Size.X - ClassicQuantityLayout.InputFrame.Size.X) / 2);
    public override void _Draw()
    {
        base._Draw();
        var image = Plugin.Kit.Layout("{nation}_personaltradeedit_us").Images.ElementAt(6);
        var rect = ClassicQuantityLayout.InputFrame; rect.Position = new Vector2(FieldX, rect.Position.Y);
        DrawTextureRectRegion(Plugin.Kit.Texture(image.Texture!), rect, new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH));
    }
}
