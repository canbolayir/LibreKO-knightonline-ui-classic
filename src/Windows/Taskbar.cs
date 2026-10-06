using Godot;
using LibreKO;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

public partial class Taskbar : Control
{
    public const float BarHeight=62;
    private readonly LayoutView _view;
    private readonly LayoutNode _layout;
    private readonly PluginGame _game=Plugin.Kit.Game;
    private (bool Running,bool Sitting,bool Attacking)? _commandState;

    public Taskbar()
    {
        var kit=Plugin.Kit;
        _layout=kit.Layout("{nation}_cmd_us");
        _view=new LayoutView(kit,_layout);
        // Native frame images extend below the UIF root rectangle.
        Size=new Vector2(_layout.W,BarHeight);
        MouseFilter=MouseFilterEnum.Ignore;
        TextureFilter=TextureFilterEnum.Nearest;
        AddChild(_view);
        _view.Hide("btn_disband");
        foreach(var (id,window) in new[]{("btn_inventory","inventory"),("btn_character","character_info"),("btn_skill","skills"),("btn_map","zonemap"),("btn_invite","party")})
        {
            string target=window;
            _view.OnPressed(id,()=>_game.Windows.Toggle(target));
        }
        _view.OnPressed("btn_option",()=>SettingsPanel.Open(GetTree().Root));
        _view.OnPressed("btn_control",_game.Commands.TradeWithTarget);
        _view.OnPressed("btn_exit",_game.Commands.OpenGameMenu);
        foreach(var (id,text) in new[]{("btn_map","Map"),("btn_inventory","Inventory"),("btn_character","Character"),("btn_skill","Skill"),
            ("btn_control","Trade"),("btn_invite","Invite"),("btn_option","Command"),("btn_exit","Exit")})
            Caption(id,text);
        _view.Get("btn_control")!.TooltipText="Trade with the selected player";
        _view.Get("btn_exit")!.TooltipText="Open the game menu (Esc)";

        Command("btn_attack",_game.Commands.ToggleAttack);
        Command("btn_walk",_game.Commands.ToggleRun);
        Command("btn_run",_game.Commands.ToggleRun);
        Command("btn_sit",_game.Commands.ToggleSit);
        Command("btn_stand",_game.Commands.ToggleSit);
        Command("btn_camera",_game.Commands.TurnCamera);
        if(_view.Get("btn_camera") is {} camera) camera.TooltipText="Turn the camera";

        int iconY=_layout.Find("btn_attack")!.Y;
        int startX=(int)Mathf.Round((Size.X-(42*4+3))/2);
        // Alternate state icons share exactly the same cells.
        foreach(var (id,column) in new[]{("btn_attack",0),("btn_walk",1),("btn_run",1),("btn_sit",2),("btn_stand",2),("btn_camera",3)})
            if(_view.Get(id) is {} button) button.Position=new Vector2(startX+column*43,iconY);
        if(_view.Get<BaseButton>("btn_attack") is {} attack) attack.ToggleMode=true;
        RefreshCommands();
    }

    private void Command(string id,Action action) => _view.OnPressed(id,()=> {
        action(); _commandState=null; RefreshCommands();
    });

    private void Caption(string id,string text)
    {
        if(_view.Get<BaseButton>(id) is not {} button) return;
        foreach(var old in button.GetChildren().OfType<Label>()) old.Visible=false;
        var inventory=_layout.Find("btn_inventory")!;
        var reference=inventory.Images.First(n=>n.Tag==ButtonState.Normal).Strings.First();
        var native=_layout.Find(id)!;
        var textArea=native.Images.First(n=>n.Tag==ButtonState.Normal).Strings.First();
        bool karus=Plugin.Kit.Nation==1;
        var normal=karus ? new Color("f4f1e6"):reference.Color;
        var label=new Label {
            Name="classic_caption",Text=text,
            Position=new Vector2(textArea.X-native.X,reference.Y-native.Y),
            Size=new Vector2(textArea.W,reference.H),
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center,
            MouseFilter=MouseFilterEnum.Ignore,ClipText=true,
            AutowrapMode=TextServer.AutowrapMode.Off,
        };
        label.AddThemeFontOverride("font",Plugin.Kit.FontFor(reference));
        label.AddThemeFontSizeOverride("font_size",UiKit.FontSize(reference));
        label.AddThemeColorOverride("font_color",normal);
        label.AddThemeColorOverride("font_outline_color",Colors.Black);
        label.AddThemeConstantOverride("outline_size",karus ? 2:1);
        label.AddThemeColorOverride("font_shadow_color",Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x",1);
        label.AddThemeConstantOverride("shadow_offset_y",1);
        button.AddChild(label);
        button.TooltipText=id=="btn_option" ? "Command / Settings":text;
        void RefreshInk() => label.AddThemeColorOverride("font_color",button.ButtonPressed ? Colors.White:normal);
        button.ButtonDown+=RefreshInk;
        button.ButtonUp+=RefreshInk;
        button.MouseEntered+=RefreshInk;
        button.MouseExited+=RefreshInk;
    }

    private void RefreshCommands()
    {
        var commands=_game.Commands;
        var state=(commands.Running,commands.Sitting,commands.AutoAttacking);
        if(_commandState==state) return;
        _commandState=state;
        _view.Get("btn_run")!.Visible=state.Running;
        _view.Get("btn_walk")!.Visible=!state.Running;
        _view.Get("btn_sit")!.Visible=!state.Sitting;
        _view.Get("btn_stand")!.Visible=state.Sitting;
        _view.Get("btn_run")!.TooltipText="Switch to walking";
        _view.Get("btn_walk")!.TooltipText="Switch to running";
        _view.Get("btn_sit")!.TooltipText="Sit down";
        _view.Get("btn_stand")!.TooltipText="Stand up";
        var attack=_view.Get<BaseButton>("btn_attack")!;
        attack.SetPressedNoSignal(state.AutoAttacking);
        attack.TooltipText=state.AutoAttacking ? "Stop attacking":"Attack the selected target";
    }

    public override void _Ready() { GetViewport().SizeChanged+=Place; Place(); }
    public override void _ExitTree() { GetViewport().SizeChanged-=Place; }
    public override void _Process(double delta) => RefreshCommands();
    private void Place()
    {
        var room=GetViewport().GetVisibleRect().Size;
        Position=new Vector2(Mathf.Max(0,Mathf.Round((room.X-Size.X)/2)),Mathf.Round(room.Y-BarHeight));
    }
}
