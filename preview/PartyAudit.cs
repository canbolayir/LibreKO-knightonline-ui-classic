using Godot;
using LibreKO;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private static IEnumerable<Node> PartyTree(Node root)
    { foreach(var child in root.GetChildren()) { yield return child; foreach(var nested in PartyTree(child)) yield return nested; } }
    private async Task CapturePartyAudit(int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/party-window-audit");System.IO.Directory.CreateDirectory(output);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(620,600);
        AddChild(new ColorRect {Color=new Color("35382f"),Size=new Vector2(620,600),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        var world=new World();
        void Call(string method,params object[] args)=>typeof(World).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(world,args);
        T Field<T>(string name)=>(T)typeof(World).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(world)!;
        Call("BuildPartyPanel");
        Field<CanvasLayer>("_partyLayer").Reparent(this);
        Field<CanvasLayer>("_partyDialogLayer").Reparent(this);
        var roster=new HudWindow("party","Party",new Vector2(26,30),252);AddChild(roster);
        roster.Body.AddChild(Field<VBoxContainer>("_partyContent"));
        Call("BuildSeekPartyPanel");Field<CanvasLayer>("_seekLayer").Reparent(this);
        var board=Field<Control>("_seekPanel");board.Position=new Vector2(222,30);board.Visible=true;
        typeof(World).GetField("_seekShown",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(world,true);
        ClassicPartySkin.Apply(roster.Body); ClassicPartySkin.Apply(((HudWindow)board).Body);
        await Settled();
        var partyPanel=roster.GetNode<ClassicPartyRoster>("classic_party_roster");
        var boardPanel=board.GetNode<ClassicPartyBoard>("classic_party_board");
        var checks=new List<string>();var captures=new List<object>();string prefix=nation==1?"karus":"human";
        void Check(bool ok,string text){if(!ok)throw new Exception("PARTY_AUDIT: "+text);checks.Add(text);}
        async Task Capture(string state)
        {
            await Settled();
            roster.Position=new Vector2(26,30); board.Position=new Vector2(222,30);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            foreach(var parent in new Control[]{partyPanel,boardPanel})
            {
                foreach(var button in PartyTree(parent).OfType<Button>().Where(b=>b.IsVisibleInTree()))
                {
                    var rect=new Rect2(button.GlobalPosition-parent.GlobalPosition,button.Size);
                    Check(new Rect2(Vector2.Zero,parent.Size).Encloses(rect),state+": button inside "+button.Text+" "+rect);
                    if(button.HasMeta("party_expected_rect")) Check(new Rect2(button.Position,button.Size)==button.GetMeta("party_expected_rect").AsRect2(),state+": exact source button bounds "+button.Text+" actual="+new Rect2(button.Position,button.Size));
                    Check(button.Size==button.Size.Round() && button.GlobalPosition==button.GlobalPosition.Round(),state+": integer button geometry "+button.Text);
                }
            }
            Check(boardPanel.Size==new Vector2(320,481),state+": original BBS outer size");
            foreach(var row in Field<VBoxContainer>("_partyMembersBox").GetChildren().OfType<Control>().Where(c=>c.HasMeta("party_id")))
                Check(row.Size==new Vector2(131,45),state+": compact member bounds "+row.Size);
            using var image=GetViewport().GetTexture().GetImage();image.SavePng(output+"/"+prefix+"-"+state+".png");
            captures.Add(new{state,file=prefix+"-"+state+".png",rosterSize=partyPanel.Size.ToString(),boardSize=boardPanel.Size.ToString()});
        }
        var members=Enumerable.Range(0,8).Select(i=>new PartyMember(300+i,1,i==7?"LongCharacterName1234":new[]{"Knight","Priest","Rogue","Mage","Warrior","Healer","Archer","Scout"}[i],3400,3400-i*400,70+i,nation*100+1+i%4,2800,2800-i*300)).ToArray();
        net.SeedPartyPreview(members);Call("RefreshPartyUI");
        Call("OnBbsList",0,24,Enumerable.Range(0,10).Select(i=>new PartyBbsEntry(i==9?"LongPlayerName123456":"Player"+(i+1),nation*100+1+i%4,70+i,0,"",21,1,nation)).ToList());
        await Capture("full-party");
        var list=Field<VBoxContainer>("_partyMembersBox");list.GetChildren().OfType<Control>().ElementAt(3).EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left});
        Check(list.GetMeta("party_selected").AsInt32()==303,"left click selects an unavailable member without targeting a different entity");
        var statuses=(Dictionary<int,HashSet<byte>>)typeof(Net).GetField("_partyStatus",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(net)!;
        statuses[302]=new HashSet<byte>{2,4};Call("RefreshPartyUI");
        var seekRows=Field<VBoxContainer>("_seekListBox").GetChildren().OfType<Control>().ToArray();
        seekRows[2].EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left});
        await Capture("selected-status");
        typeof(World).GetField("_seeking",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(world,true);Call("UpdateSeekStatus");
        await Capture("registered");
        Check(PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Register").Disabled,"listed: duplicate register disabled");
        Check(!PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Delete").Disabled,"listed: delete enabled");
        Call("OnBbsList",1,24,new List<PartyBbsEntry>{new("PartyLeader",4,83,3,"Need a priest for Dark Stone hunting. All levels welcome.",71,5,nation)});
        await Settled();Field<VBoxContainer>("_seekListBox").GetChildren().OfType<Control>().First().EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left});
        await Capture("recruiting");
        PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Recruit").EmitSignal(BaseButton.SignalName.Pressed);
        await Capture("recruit-form");
        PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Recruit").EmitSignal(BaseButton.SignalName.Pressed);
        net.SeedPartyPreview(members.Take(2).ToArray());Call("RefreshPartyUI");await Capture("two-members");
        net.SeedPartyPreview();Call("RefreshPartyUI");Call("OnBbsList",0,0,new List<PartyBbsEntry>());await Capture("empty");
        Check(PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Invite").Disabled,"empty: stale invite disabled");
        System.IO.File.WriteAllText(output+"/"+prefix+"-verification.json",JsonSerializer.Serialize(new{nation,checks,captures,pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://../bin/KnightOnlineUiClassic.dll"))))},new JsonSerializerOptions{WriteIndented=true}));
        world.Free(); net.Free();
        GD.Print("PARTY_AUDIT_OK "+prefix+" "+checks.Count);
    }
}
