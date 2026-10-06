using Godot;
using System.Reflection;
using LibreKO;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;

public partial class Preview
{
    private async Task AuditChatDock(ChatWindow chat,LogWindow info,Control infoTitle,Func<string,Task> shot)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var chatLayout=(HudLayout)typeof(ChatWindow).GetField("_layout",flags)!.GetValue(chat)!;
        var infoLayout=(HudLayout)typeof(LogWindow).GetField("_layout",flags)!.GetValue(info)!;
        void Begin(HudLayout layout,string method,Vector2 mouse) => typeof(HudLayout).GetMethod(method,flags)!.Invoke(layout,new object[]{mouse});
        void Motion(HudLayout layout,Vector2 mouse) => layout._Input(new InputEventMouseMotion {GlobalPosition=mouse});
        void Release(HudLayout layout) => layout._Input(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false});
        var tabs=(List<Panel>)typeof(ChatWindow).GetField("_tabs",flags)!.GetValue(chat)!;
        var tabBounds=tabs.Select(tab=>(tab.Position.X,tab.Size.X)).ToArray();
        var footer=(Control)typeof(ChatWindow).GetField("_footer",flags)!.GetValue(chat)!;
        void AssertFixedTabs()
        {
            if(!tabs.Select(tab=>(tab.Position.X,tab.Size.X)).SequenceEqual(tabBounds)
                || footer.Size.X!=620 || footer.Size.Y!=24 || footer.Position.Y!=chat.Size.Y-24
                || tabs.Last().GetRect().End.X>chat.Size.X)
                throw new Exception("Resizing Chat stretches or clips its fixed footer buttons");
        }
        void AssertJoined()
        {
            AssertFixedTabs();
            if(chat.GetGlobalRect().End.X!=info.GlobalPosition.X || chat.GlobalPosition.Y!=info.GlobalPosition.Y
                || chat.Size.Y!=info.Size.Y || chat.GetGlobalRect().End.Y!=614 || info.GetGlobalRect().End.X!=1080)
                throw new Exception("Joined chat/info loses its shared edge, height or outer anchors");
        }
        var transparency=typeof(LogWindow).GetField("_transparency",flags)!.GetValue(info)!;
        var step=transparency.GetType().GetField("_step",flags)!;
        int oldStep=(int)step.GetValue(transparency)!;
        infoTitle.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,GlobalPosition=infoTitle.GlobalPosition});
        Release(infoLayout);
        if((int)step.GetValue(transparency)! != (oldStep+1)%4) throw new Exception("Right Info click does not cycle opacity");
        for(int i=0;i<3;i++) typeof(HudLayout).GetMethod("CycleBackgroundOpacity",flags)!.Invoke(infoLayout,null);
        chat.Size=new Vector2(820,190);AssertFixedTabs();info.Visible=false;
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await shot("fixed-footer");
        info.Visible=true;
        chat.Size=new Vector2(620,190);chat.Position=new Vector2(20,424);
        Begin(chatLayout,"BeginResize",new Vector2(640,424));
        Motion(chatLayout,new Vector2(650,404));AssertJoined();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await shot("joined");
        Motion(chatLayout,new Vector2(680,384));AssertJoined();Release(chatLayout);
        Begin(infoLayout,"BeginResize",info.GlobalPosition);
        Motion(infoLayout,info.GlobalPosition+new Vector2(-30,-10));AssertJoined();Release(infoLayout);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await shot("joined-resize");
        var joinedChat=chat.GetRect();var joinedInfo=info.GetRect();
        Begin(chatLayout,"BeginResize",chat.GlobalPosition+new Vector2(chat.Size.X,0));
        Motion(chatLayout,chat.GlobalPosition+new Vector2(chat.Size.X+1000,-1000));Release(chatLayout);AssertJoined();
        if(info.Size.X<290 || chat.Size.Y<135 || info.GlobalPosition.Y<0) throw new Exception("Linked resize exceeds partner minimum or viewport");
        chat.Size=joinedChat.Size;chat.Position=joinedChat.Position;info.Size=joinedInfo.Size;info.Position=joinedInfo.Position;
        var peerBefore=info.GetGlobalRect();
        Begin(chatLayout,"BeginDrag",chat.Position);Motion(chatLayout,chat.Position+new Vector2(-20,0));Release(chatLayout);
        if(info.GetGlobalRect()!=peerBefore || typeof(HudLayout).GetField("_resizePartner",flags)!.GetValue(chatLayout)!=null)
            throw new Exception("Dragging Chat does not detach Info");
        AssertFixedTabs();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await shot("detached");
        Begin(chatLayout,"BeginResize",chat.GlobalPosition+new Vector2(chat.Size.X,0));
        Motion(chatLayout,chat.GlobalPosition+new Vector2(chat.Size.X+15,0));Release(chatLayout);AssertJoined();
        peerBefore=chat.GetGlobalRect();
        Begin(infoLayout,"BeginDrag",infoTitle.GlobalPosition);Motion(infoLayout,infoTitle.GlobalPosition+new Vector2(0,-30));Release(infoLayout);
        if(chat.GetGlobalRect()!=peerBefore || typeof(HudLayout).GetField("_resizePartner",flags)!.GetValue(infoLayout)!=null)
            throw new Exception("Dragging Info does not detach Chat");
        foreach(float offset in new[]{-90f,70f,-30f})
        {
            Begin(chatLayout,"BeginResize",chat.GlobalPosition+new Vector2(chat.Size.X,0));
            Motion(chatLayout,chat.GlobalPosition+new Vector2(chat.Size.X,0));Release(chatLayout);
            Begin(infoLayout,"BeginDrag",infoTitle.GlobalPosition);
            Motion(infoLayout,infoTitle.GlobalPosition+new Vector2(0,offset));Release(infoLayout);
            if(typeof(HudLayout).GetField("_resizePartner",flags)!.GetValue(infoLayout)!=null)
                throw new Exception("Moving the misaligned Info leaves a stale link");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(offset==-30) await shot("misaligned");
            Begin(chatLayout,"BeginResize",chat.GlobalPosition+new Vector2(chat.Size.X,0));
            Motion(chatLayout,chat.GlobalPosition+new Vector2(chat.Size.X,-5));Release(chatLayout);AssertJoined();
        }
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await shot("rejoined");
        Begin(chatLayout,"BeginDrag",chat.Position);Motion(chatLayout,chat.Position+new Vector2(0,-60));Release(chatLayout);
        Begin(infoLayout,"BeginResize",info.GlobalPosition);Motion(infoLayout,info.GlobalPosition+new Vector2(0,-5));Release(infoLayout);AssertJoined();
        chat.Position=new Vector2(20,424);chat.Size=new Vector2(620,190);
        info.Position=new Vector2(660,409);info.Size=new Vector2(420,205);
        var pmLayer=new CanvasLayer();AddChild(pmLayer);
        HudWindow Fixture(string name)
        {
            var fixture=new HudWindow("whisper_"+name,name,new Vector2(1,1),persistLayout:false,minimizable:true);
            var area=new ScrollContainer();var lines=new VBoxContainer();area.AddChild(lines);fixture.Body.AddChild(area);
            lines.AddChild(ClassicWhisperSkin.Line(name,false,false,"A new private conversation."));
            var row=new HBoxContainer();row.AddChild(new LineEdit());row.AddChild(new Button {Text="Send"});fixture.Body.AddChild(row);
            pmLayer.AddChild(fixture);ClassicWhisperSkin.Apply(fixture);return fixture;
        }
        var first=Fixture("Merchant");var latest=Fixture("Sentinel");
        first.SetMinimized(true);latest.SetMinimized(true);latest.AttentionStyler?.Invoke(true);
        await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
        if(first.Position!=latest.Position || latest.Position.Y!=96 || latest.Position.X!=1120-Plugin.Kit.Layout("co_whisper_open_us").W-12)
            throw new Exception("New Classic PMs do not share their standard origin");
        await shot("pm-stack");latest.SetMinimized(false);
        await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);await shot("pm-stack-open");
        first.SetMinimized(false);
        var world=new World();var worldType=typeof(World);
        worldType.GetField("_whisperLayer",flags)!.SetValue(world,pmLayer);
        var chats=worldType.GetField("_whispers",flags)!.GetValue(world)!;
        var chatType=worldType.GetNestedType("WhisperChat",BindingFlags.NonPublic)!;
        foreach(var fixture in new[]{first,latest})
        {
            var conversation=Activator.CreateInstance(chatType,true)!;
            chatType.GetField("Name")!.SetValue(conversation,fixture.Id);
            chatType.GetField("Window")!.SetValue(conversation,fixture);
            chats.GetType().GetMethod("Add")!.Invoke(chats,new object[]{fixture.Id,conversation});
        }
        worldType.GetMethod("FocusWhisperAt",flags)!.Invoke(world,new object[]{latest.Position+new Vector2(20,40)});
        if((string?)worldType.GetField("_whisperComposeTarget",flags)!.GetValue(world)!=latest.Id)
            throw new Exception("Overlapping PM hit testing selects a hidden conversation");
        world.Free();pmLayer.QueueFree();
        GD.Print("CHAT_DOCK_OK: right Info plate/opacity/drag, 12px resize snap, bidirectional resize, both drag detach paths, fixed PM origin");
    }
}
