using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>The original inventory destruction artwork with declarative controls and native validation.</summary>
public partial class ClassicInventoryDestroy : CanvasLayer
{
    private readonly LayoutView _view;
    private readonly Action _confirm;
    private bool _finished;
    public ClassicInventoryDestroy(LayoutView inventory,Action confirm)
    {
        Layer=221;_confirm=confirm;
        var blocker=new ColorRect {Color=Colors.Transparent,MouseFilter=Control.MouseFilterEnum.Stop};
        blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);AddChild(blocker);
        var source=Plugin.Kit.Layout("{nation}_inventory_us");
        var original=source.Find("area_samma")!;
        var frame=original.Find("img_Destroy")!;
        var layout=new LayoutNode {Type="base",Id="inventory_destroy",W=frame.W,H=frame.H};
        foreach(var child in original.Children)
        {
            var copy=CharacterLayout.CopyImage(child,-frame.X,-frame.Y);
            if(copy.Type=="string") {copy.Text=child.Text;copy.Color=child.Color;copy.Font=child.Font;copy.Size=child.Size;copy.Bold=child.Bold;}
            layout.Children.Add(copy);
        }
        _view=new LayoutView(Plugin.Kit,layout);AddChild(_view);
        _view.Position=inventory.GlobalPosition+new Vector2(frame.X-source.X,frame.Y-source.Y);
        _view.OnPressed("btn_Destroy_ok",()=>Finish(true));
        _view.OnPressed("btn_Destroy_cancel",()=>Finish(false));
    }
    public override void _Ready()=>_view.ApplyDeclaredBounds();
    private void Finish(bool accept)
    {
        if(_finished) return;_finished=true;Visible=false;
        if(accept) _confirm();QueueFree();
    }
    public override void _Input(InputEvent ev)
    {
        if(!Visible || ev is not InputEventKey {Pressed:true,Echo:false} key) return;
        if(key.Keycode is Key.Enter or Key.KpEnter) Finish(true);
        else if(key.Keycode==Key.Escape) Finish(false);
        else return;
        GetViewport().SetInputAsHandled();
    }
}
