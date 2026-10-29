using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using KnightOnlineUiClassic.Layout;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private static IEnumerable<Node> PartyTree(Node root)
    { foreach(var child in root.GetChildren()) { yield return child; foreach(var nested in PartyTree(child)) yield return nested; } }
    private static IEnumerable<Node> PartyEverything(Node root)
    { yield return root; foreach(var child in root.GetChildren(true)) foreach(var nested in PartyEverything(child)) yield return nested; }
    private async Task CapturePartyAudit(int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/party-window-audit");System.IO.Directory.CreateDirectory(output);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(620,600);
        var backdrop=new Color("35382f");
        AddChild(new ColorRect {Color=backdrop,Size=new Vector2(620,600),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        // ff78eef: the row meter is off by default (the client keeps it off unless a plugin replaces its party HUD).
        Config.SetPartyRowMeter(PartyBoardMeterMode.Off);
        var world=PreviewFixtures.Prepared(new WorldRoot());
        void Call(string method,params object[] args)=>DetailCall(world,method,args);
        T Field<T>(string name)=>(T)Native.Get<object>(world, name)!;
        void SetField(string name,object? value)=>Native.Set(world, name, value);
        Call("PartyInvites.BuildPrompts");
        Field<CanvasLayer>("PartyInvites.dialogLayer").Reparent(this);
        // The party window is gone upstream: the Classic party list replaces the client's party HUD and is
        // driven by the plugin party API the client's bridge implements.
        SetField("MyId",300);
        Call("Party.BuildHud"); var social=Native.Get<object>(Native.Call(world,"CreatePluginAdapters")!,"Social")!;Native.Call(LibreKO.Plugins.PluginHost.Game,"AttachSocial",social,social,social);Native.Call(LibreKO.Plugins.PluginHost.Game,"AttachWarp",social); Game.Session.Self.Vitals.Seed(3100,3400,2500,2800);
        var hudLayer=new CanvasLayer{Layer=64};AddChild(hudLayer);
        var partyPanel=new ClassicPartyHud(LibreKO.Plugins.PluginHost.Game.Party,()=>world);hudLayer.AddChild(partyPanel);
        Call("SeekParty.BuildSeekPanel");Field<CanvasLayer>("SeekParty.seekLayer").Reparent(this);
        var board=Field<Control>("SeekParty.SeekPanel");board.Position=new Vector2(222,30);board.Visible=true;
        SetField("SeekParty.SeekShown",true);
        NativeCharacterNpc.Prepare((HudWindow)board,world);
        ClassicPartySkin.Apply(((HudWindow)board).Body);
        await Settled();
        var boardPanel=board.GetNode<ClassicPartyBoard>("classic_party_board");
        var party=LibreKO.Plugins.PluginHost.Game.Party;
        var checks=new List<string>();var captures=new List<object>();string prefix=nation==1?"karus":"human";
        void Check(bool ok,string text){if(!ok)throw new Exception("PARTY_AUDIT: "+text);checks.Add(text);}
        // Rendered pixels in a control's area of the last frame (the window is not scaled).
        bool AnyPixel(Image image,Rect2 rect,Func<Color,bool> test)
        {
            for(int y=(int)rect.Position.Y;y<(int)rect.End.Y;y++) for(int x=(int)rect.Position.X;x<(int)rect.End.X;x++)
                if(x>=0&&y>=0&&x<image.GetWidth()&&y<image.GetHeight()&&test(image.GetPixel(x,y))) return true;
            return false;
        }
        bool Near(Color a,Color b,float tolerance=.12f)=>Math.Abs(a.R-b.R)<=tolerance&&Math.Abs(a.G-b.G)<=tolerance&&Math.Abs(a.B-b.B)<=tolerance;
        bool SelectionGreen(Color c)=>c.G>.8f&&c.R<.35f&&c.B<.35f;
        Rect2 InRow(ClassicPartyHudRow row,Rect2 local)=>new(row.GlobalPosition+local.Position,local.Size);
        // Tooltip captures skip the pixel checks of the cells the popup covers.
        async Task<Image> Capture(string state,bool pixels=true)
        {
            await Settled();
            partyPanel.Position=new Vector2(26,30); board.Position=new Vector2(222,30); partyPanel.Refresh();
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
            // Every party action caption fits its button art (the re-invite tip is long since ff78eef, the caption is not).
            foreach(var action in partyPanel.GetNode<Control>("party_actions").GetChildren().OfType<Button>().Where(b=>b.IsVisibleInTree()))
            {
                float captionWidth=action.GetThemeFont("font").GetStringSize(action.Text,HorizontalAlignment.Left,-1,action.GetThemeFontSize("font_size")).X;
                Check(captionWidth<=action.Size.X-4,state+": caption fits its action "+action.Text+" ("+captionWidth+" of "+action.Size.X+")");
            }
            Check(boardPanel.Size==new Vector2(320,481),state+": original BBS outer size");
            var shown=partyPanel.GetChildren().OfType<ClassicPartyHudRow>().Where(r=>r.Visible).ToArray();
            Check(shown.Length==Game.Session.Party.Members.Count && partyPanel.Visible==(Game.Session.Party.Members.Count>0),state+": one original member cell per member, hidden without a party");
            for(int i=0;i<shown.Length;i++)
                Check(shown[i].GetRect()==ClassicPartyHudLayout.Row(i) && shown[i].Member.Name==party.Members[i].Name,state+": member cell bounds "+shown[i].GetRect());
            if(partyPanel.Visible) Check(partyPanel.Size==new Vector2(131,ClassicPartyHudLayout.Height(shown.Length,party.CommanderBarHidden)),state+": party list size "+partyPanel.Size);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var image=GetViewport().GetTexture().GetImage();
            foreach(var row in shown)
            {
                var member=row.Member;
                bool covered=!pixels;
                var up=row.GetNode<Button>("party_move_up");var down=row.GetNode<Button>("party_move_down");
                Check(up.Visible==(shown.Length>1) && down.Visible==(shown.Length>1),state+": move arrows while the party has more than one member");
                Check(!up.GetRect().Intersects(ClassicPartyHudLayout.Selection) && new Rect2(Vector2.Zero,row.Size).Encloses(down.GetRect()),state+": move arrows beside the bars inside the cell");
                // ff78eef: the member's connection in the arrow column, with the client's ping text.
                int ping=party.PingMs(member.Id);
                var signal=row.GetNode<ClassicPartySignal>("party_signal");
                Check(signal.Visible && signal.GetRect()==ClassicPartyHudLayout.Signal && new Rect2(Vector2.Zero,row.Size).Encloses(signal.GetRect())
                    && !signal.GetRect().Intersects(up.GetRect()) && !signal.GetRect().Intersects(down.GetRect()) && !signal.GetRect().Intersects(ClassicPartyHudLayout.Selection),
                    state+": signal glyph in the arrow column beside the name line "+signal.GetRect());
                Check(signal.Ms==ping && signal.TooltipText==PartySignal.Tooltip(ping),state+": "+member.Name+" signal follows the client's ping ("+signal.TooltipText+")");
                var tint=ClassicPartySignal.Tint(signal.Level);
                Check(covered||AnyPixel(image,signal.GetGlobalRect(),c=>Near(c,tint,.15f)),state+": "+member.Name+" signal rendered in the client's "+signal.Level+" colour");
                // The client's member tooltip, then the level progress and the ping.
                double level=party.LevelPercent(member.Id);
                Check(row.TooltipText==ClassicPartyHudRow.Tip(member.Tooltip,level,ping) && row.TooltipText.StartsWith(member.Tooltip)
                    && row.TooltipText.Contains("\nLevel progress "+level.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"%\n") && row.TooltipText.EndsWith(PartySignal.Tooltip(ping)),
                    state+": "+member.Name+" tooltip with level progress and ping");
                // ff78eef: the member's buffs on the name line, ending under the bars; the name gives way down to two.
                var buffs=party.MemberBuffs(member.Id);
                var name=row.GetNode<Label>("party_member_name");
                float natural=name.GetThemeFont("font").GetStringSize(member.Name,HorizontalAlignment.Left,-1,name.GetThemeFontSize("font_size")).X;
                var icons=row.GetChildren().OfType<ClassicPartyBuff>().Where(b=>b.Visible).ToArray();
                int expected=Math.Min(buffs.Count,ClassicPartyHudLayout.BuffsShown(natural));
                Check(icons.Length==expected && row.BuffsShown==expected,state+": "+member.Name+" shows "+expected+" of "+buffs.Count+" buffs");
                Check(name.GetRect()==new Rect2(ClassicPartyHudLayout.Name.Position,ClassicPartyHudLayout.NameWidth(icons.Length),ClassicPartyHudLayout.Name.Size.Y),state+": "+member.Name+" name width "+name.GetRect());
                if(icons.Length<buffs.Count && icons.Length>0) Check(natural>name.Size.X || icons.Length==ClassicPartyHudLayout.BuffMax || ClassicPartyHudLayout.BuffsShown(natural)==icons.Length,state+": "+member.Name+" further buffs only where the name needs the room");
                for(int i=0;i<icons.Length;i++)
                {
                    var rect=icons[i].GetRect();
                    Check(rect==ClassicPartyHudLayout.Buff(i,icons.Length) && rect.Size==new Vector2(ClassicPartyHudLayout.BuffIcon,ClassicPartyHudLayout.BuffIcon),state+": "+member.Name+" buff "+i+" bounds "+rect);
                    Check(icons[i].SkillId==buffs[i].SkillId && icons[i].TooltipText==buffs[i].Tooltip && icons[i].HasIcon,state+": "+member.Name+" buff "+i+" is the client's skill "+buffs[i].SkillId+" with its art and tooltip");
                    Check(ClassicPartyHudLayout.Selection.Encloses(rect) && rect.Position.X>=name.GetRect().End.X+ClassicPartyHudLayout.BuffGap && rect.Position.Y>=ClassicPartyHudLayout.Mp.End.Y,
                        state+": "+member.Name+" buff "+i+" under the MP bar, right of the name, inside the cell");
                    Check(covered||AnyPixel(image,icons[i].GetGlobalRect().Grow(-3),c=>!Near(c,backdrop,.04f)),state+": "+member.Name+" buff "+i+" icon rendered");
                }
                if(icons.Length>0) Check(icons[^1].GetRect().End.X==ClassicPartyHudLayout.BuffEnd && Mathf.IsEqualApprox(icons[^1].GetRect().End.X,ClassicPartyHudLayout.Hp.End.X),state+": "+member.Name+" buff row ends under the bars' right end");
                // The selection frame is drawn exactly for the member the client highlights.
                Check(covered||AnyPixel(image,InRow(row,new Rect2(ClassicPartyHudLayout.Selection.Position.X-1,24,3,8)),SelectionGreen)==member.Picked,state+": "+member.Name+" selection frame "+(member.Picked?"drawn":"not drawn"));
                // The row meter strip shows only while the client's row meter is on.
                Check(covered||AnyPixel(image,InRow(row,new Rect2(ClassicPartyHudLayout.Meter.Position,1,ClassicPartyHudLayout.Meter.Size.Y)),c=>Near(c,backdrop,.03f))==(party.RowMeterMode==PartyBoardMeterMode.Off),state+": "+member.Name+" meter strip only with the row meter on");
            }
            image.SavePng(output+"/"+prefix+"-"+state+".png");
            captures.Add(new{state,file=prefix+"-"+state+".png",rosterSize=partyPanel.Size.ToString(),boardSize=boardPanel.Size.ToString()});
            return image;
        }
        // Hovers a control like the pointer does and returns the client's tooltip popup once it shows.
        async Task<(PopupPanel Popup,Label Label)> Tooltip(Control owner,Vector2? local=null)
        {
            var at=local is { } point?owner.GlobalPosition+point:owner.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion{Position=at,GlobalPosition=at});
            PopupPanel? popup=null;
            // Godot opens a tooltip after its delay in seconds; at a high frame rate 120 frames pass sooner.
            ulong until=Time.GetTicksMsec()+3000;
            for(int i=0;(i<120||Time.GetTicksMsec()<until)&&popup==null;i++)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                popup=PartyEverything(GetTree().Root).OfType<PopupPanel>().FirstOrDefault(p=>p.Visible&&owner.IsAncestorOf(p));
            }
            Check(popup!=null,"tooltip opens on hovering "+owner.Name);
            for(int i=0;i<4;i++) await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var label=PartyEverything(popup!).OfType<Label>().First();
            var ground=popup!.GetThemeStylebox("panel","TooltipPanel") as StyleBoxFlat;
            Check(ground!=null && ground.BgColor.IsEqualApprox(new Color(0,0,0,187f/255f)) && label.GetThemeFont("font")==Plugin.Kit.Bold && label.GetThemeColor("font_color")==Colors.White,
                owner.Name+": original tooltip look (translucent black, white Arial Bold)");
            var rect=new Rect2(popup.Position,popup.Size);
            Check(GetViewportRect().Encloses(rect),owner.Name+": tooltip inside the window "+rect);
            Check(label.GetLineCount()==label.Text.Split('\n').Length && label.GetVisibleLineCount()>=label.GetLineCount(),owner.Name+": every tooltip line shows");
            return (popup,label);
        }
        async Task Unhover()
        {
            var away=new Vector2(600,590);
            GetViewport().PushInput(new InputEventMouseMotion{Position=away,GlobalPosition=away});
            for(int i=0;i<6;i++) await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        var members=Enumerable.Range(0,8).Select(i=>new PartyMember(300+i,1,i==7?"LongCharacterName1234":new[]{"Knight","Priest","Rogue","Mage","Warrior","Healer","Archer","Scout"}[i],3400,3400-i*400,70+i,nation*100+1+i%4,2800,2800-i*300)).ToArray();
        Game.Session.Party.SeedPreview(members);
        // ff78eef member data, as upstream's party HUD preview seeds it: every signal level, level progress and
        // buffs (none, a few, more than fit, a long name). The own ping and level progress are the player's.
        var buffIds=PreviewFixtures.SeedPartyMembers(net,
            (300,0,0,0,3),(301,84,625,0,2),(302,172,310,2,2),(303,290,880,1,6),(304,420,150,0,0),
            (305,PartySignal.NoSignal,50,3,1),(307,95,999,0,6));
        Check(buffIds.Count>=9,"installed skill table offers buff skills with art ("+buffIds.Count+")");
        typeof(Net).GetProperty("PingMs")!.SetValue(net,42);
        Game.Session.Self.Sheet.SeedProgress(70,4520,10000);
        Call("Party.RefreshUI");await Settled();
        Check(PartyTree(partyPanel).OfType<Button>().Single(b=>b.Name=="party_leave").Text=="Disband","leader: Leave reads Disband");
        Check(party.PingMs(300)==42 && party.PingMs(306)==PartySignal.Unknown && party.PingMs(305)==PartySignal.NoSignal && Math.Abs(party.LevelPercent(300)-45.2)<.001 && Math.Abs(party.LevelPercent(303)-88)<.001,
            "party API: the client's pings (own, unknown, no signal) and level progress");
        Call("OnBbsList",0,24,Enumerable.Range(0,10).Select(i=>new PartyBbsEntry(i==9?"LongPlayerName123456":"Player"+(i+1),nation*100+1+i%4,70+i,0,"",21,1,nation)).ToList());
        await Capture("full-party");
        // 3277df5: the client's own row order (Move up / Move down), automatic re-invite and commander bar restore.
        var firstRow=partyPanel.GetNode<ClassicPartyHudRow>("party_member_0");var lastRow=partyPanel.GetNode<ClassicPartyHudRow>("party_member_7");
        Check(firstRow.GetNode<Button>("party_move_up").Disabled && !firstRow.GetNode<Button>("party_move_down").Disabled && lastRow.GetNode<Button>("party_move_down").Disabled,"move arrows: the ends cannot move further");
        Check(lastRow.BuffsShown==ClassicPartyHudLayout.BuffMinShown && lastRow.GetNode<Label>("party_member_name").Size.X<ClassicPartyHudLayout.Name.Size.X,"long name: two buffs, the name ends in an ellipsis");
        firstRow.GetNode<Button>("party_move_down").EmitSignal(BaseButton.SignalName.Pressed);await Settled();
        Check(party.Members[0].Id==301 && party.Members[1].Id==300 && partyPanel.GetNode<ClassicPartyHudRow>("party_member_1").Member.Id==300,"Move down runs the client's row order");
        Check(partyPanel.GetNode<ClassicPartyHudRow>("party_member_0").BuffsShown==2 && partyPanel.GetNode<ClassicPartyHudRow>("party_member_1").BuffsShown==3,"buffs move with their member");
        await Capture("moved");
        partyPanel.GetNode<ClassicPartyHudRow>("party_member_1").GetNode<Button>("party_move_up").EmitSignal(BaseButton.SignalName.Pressed);await Settled();
        Check(party.Members[0].Id==300,"Move up restores the order");
        var rejoin=PartyTree(partyPanel).OfType<Button>().Single(b=>b.Name=="party_re-invite");
        // ff78eef: the re-invite tips are the long official texts; the Classic tooltip wraps them as the client wraps buff tooltips.
        Check(rejoin.Text=="Re-invite: On" && party.AutoRejoin && party.AutoRejoinTip.Length>ClassicPartyHudLayout.TipLetters
            && rejoin.TooltipText==ClassicPartyHud.WrapTip(party.AutoRejoinTip) && rejoin.TooltipText.Replace("\n"," ")==party.AutoRejoinTip
            && rejoin.TooltipText.Split('\n').All(line=>line.Length<=ClassicPartyHudLayout.TipLetters),"automatic re-invite starts on, with the client's tip wrapped ("+rejoin.TooltipText.Split('\n').Length+" lines)");
        var (rejoinTip,rejoinLabel)=await Tooltip(rejoin);
        Check(rejoinLabel.Text==rejoin.TooltipText && rejoinLabel.GetLineCount()>=3 && rejoinTip.Size.X<=380,"re-invite tooltip: the wrapped official text in "+rejoinLabel.GetLineCount()+" lines, "+rejoinTip.Size.X+" px wide");
        await Capture("reinvite-tooltip",false);
        await Unhover();
        rejoin.EmitSignal(BaseButton.SignalName.Pressed);await Settled();
        Check(rejoin.Text=="Re-invite: Off" && !party.AutoRejoin && rejoin.TooltipText==ClassicPartyHud.WrapTip(party.AutoRejoinTip),"Re-invite toggles the client's automatic re-invite, with the off tip");
        rejoin.EmitSignal(BaseButton.SignalName.Pressed);await Settled();
        Check(party.AutoRejoin,"Re-invite turns it back on");
        // The member tooltip (level progress and ping) and a buff's own tooltip, opened by hovering.
        var mageRow=partyPanel.GetNode<ClassicPartyHudRow>("party_member_3");
        // Over the HP bar, which belongs to the cell itself (the name line holds the buff icons).
        var (_,memberLabel)=await Tooltip(mageRow,new Vector2(40,6));
        Check(memberLabel.Text==mageRow.TooltipText && memberLabel.Text.Contains("Level progress 88.0%") && memberLabel.Text.EndsWith("Ping: 290 ms"),"member tooltip: the client's lines, level progress and ping");
        await Capture("member-tooltip",false);
        await Unhover();
        var buffIcon=mageRow.GetNode<ClassicPartyBuff>("party_buff_0");
        var (_,buffLabel)=await Tooltip(buffIcon);
        Check(buffLabel.Text==party.MemberBuffs(303)[0].Tooltip,"buff tooltip: the client's buff tooltip");
        await Capture("buff-tooltip",false);
        await Unhover();
        // A buff skill without art: its initial on a dark Classic cell (the client draws the bare letter).
        {
            var probe=mageRow.GetNode<ClassicPartyBuff>("party_buff_1");
            var real=party.MemberBuffs(303)[1];
            probe.Bind(new LibreKO.Plugins.GamePartyBuff(real.SkillId,null,real.Tooltip));
            for(int i=0;i<3;i++) await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using var image=GetViewport().GetTexture().GetImage();
            var cell=probe.GetGlobalRect();
            Check(!probe.HasIcon && probe.Visible && probe.TooltipText==real.Tooltip && AnyPixel(image,cell.Grow(-2),c=>c.R<.06f&&c.G<.06f&&c.B<.06f)
                && AnyPixel(image,cell.Grow(-3),c=>c.R>.9f&&c.G>.9f&&c.B>.9f),"buff without art: the skill's initial in white on a dark cell, with the client's buff tooltip");
            var crop=new Rect2I((int)mageRow.GlobalPosition.X-2,(int)mageRow.GlobalPosition.Y-2,(int)mageRow.Size.X+4,(int)mageRow.Size.Y+4);
            using var region=image.GetRegion(crop);region.Resize(region.GetWidth()*4,region.GetHeight()*4,Image.Interpolation.Nearest);
            region.SavePng(output+"/"+prefix+"-buff-no-art.png");
            captures.Add(new{state="buff-no-art",file=prefix+"-buff-no-art.png"});
            probe.Bind(real);
            for(int i=0;i<2;i++) await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            Check(probe.HasIcon,"buff art restored after the no-art check");
        }
        var commander=PartyTree(partyPanel).OfType<Button>().Single(b=>b.Name=="party_commander_bar");
        Check(!commander.Visible,"commander restore hidden while the bar shows");
        Native.Set(Game.Session.Party, "PartyCommanderId",300);
        SetField("PartyCommander.commanderUserHidden",true);
        Call("Party.RefreshUI");await Settled();
        Check(party.CommanderBarHidden && commander.Visible,"commander: the restore action shows while the commander hid the bar");
        await Capture("commander-restore");
        commander.EmitSignal(BaseButton.SignalName.Pressed);await Settled();
        Check(!party.CommanderBarHidden && !commander.Visible,"commander: the action shows the client's commander bar again");
        Native.Set(Game.Session.Party, "PartyCommanderId",-1);Call("Party.RefreshUI");await Settled();
        partyPanel.GetNode<ClassicPartyHudRow>("party_member_3").EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left});
        await Settled();
        Check(Field<int>("Party.Pick") == 303,"left click picks the member natively without targeting a different entity");
        Check(partyPanel.GetNode<ClassicPartyHudRow>("party_member_3").Member.Picked,"picked member carries the selection");
        // ff78eef: the client lights the row of the party member who is the world target (PartyHighlight) and
        // notifies the change; targeting someone outside the party clears it.
        SetField("Selection.SelectedId",302);Call("Party.FollowHighlight");await Settled();
        Check(partyPanel.GetChildren().OfType<ClassicPartyHudRow>().Where(r=>r.Visible&&r.Member.Picked).Select(r=>r.Member.Id).SequenceEqual(new[]{302}),"the selection follows the world target onto a party member");
        await Capture("target-member");
        SetField("Selection.SelectedId",9001);Call("Party.FollowHighlight");await Settled();
        Check(!partyPanel.GetChildren().OfType<ClassicPartyHudRow>().Any(r=>r.Visible&&r.Member.Picked),"targeting someone outside the party clears the selection");
        await Capture("target-outside");
        SetField("Selection.SelectedId",-1);Call("Party.FollowHighlight");
        partyPanel.GetNode<ClassicPartyHudRow>("party_member_3").EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left});
        await Settled();
        Check(partyPanel.GetNode<ClassicPartyHudRow>("party_member_3").Member.Picked,"a click picks the member again");
        // The row meter (kept in the Classic list: the client leaves it to a plugin that replaces its party HUD):
        // Off -> Damage -> Healing -> Off through the client's toggle.
        var meter=PartyTree(partyPanel).OfType<Button>().Single(b=>b.Name=="party_meter");
        Check(meter.Text=="Meter: Off" && party.RowMeterMode==PartyBoardMeterMode.Off,"row meter starts off");
        void PressMeter(){SetField("WarBoard.meterToggleSentAt",double.NegativeInfinity);meter.EmitSignal(BaseButton.SignalName.Pressed);}
        PressMeter();await Settled();
        Check(Config.PartyRowMeter==PartyBoardMeterMode.Damage && meter.Text=="Meter: Damage","Meter runs the native row meter toggle: damage dealt");
        Call("WarBoard.OnWarBoard",new WarBoardReply(PartyWarBoardTarget.Rows,PartyBoardMeterMode.Damage,members.Select((m,i)=>new WarBoardShare(m.CharId,(8-i)*60)).ToList()));
        await Settled();
        Check(party.RowMeterShare(300)==480,"the client's row shares reach the cells");
        var meterShot=await Capture("meter-damage");
        Check(AnyPixel(meterShot,InRow(firstRow,new Rect2(ClassicPartyHudLayout.Meter.Position+new Vector2(1,1),4,2)),c=>Near(c,ClassicPartyHudLayout.MeterDamage,.08f)),"damage strip in the damage colour");
        PressMeter();await Settled();
        Check(Config.PartyRowMeter==PartyBoardMeterMode.Healing && meter.Text=="Meter: Healing","Meter: healing next");
        PressMeter();await Settled();
        Check(Config.PartyRowMeter==PartyBoardMeterMode.Off && meter.Text=="Meter: Off","Meter: off again");
        var statuses=(Dictionary<int,HashSet<byte>>)Native.Get<object>(Game.Session.Party, "status")!;
        statuses[302]=new HashSet<byte>{2,4};Call("Party.RefreshUI");await Settled();
        Check(partyPanel.GetNode<ClassicPartyHudRow>("party_member_2").Member.HpColour!=partyPanel.GetNode<ClassicPartyHudRow>("party_member_1").Member.HpColour,"status members show the native HP look");
        var seekRows=Field<VBoxContainer>("SeekParty.seekListBox").GetChildren().OfType<Control>().ToArray();
        seekRows[2].EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left});
        await Capture("selected-status");
        SetField("SeekParty.seeking",true);Call("SeekParty.UpdateSeekStatus");
        await Capture("registered");
        Check(PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Register").Disabled,"listed: duplicate register disabled");
        Check(!PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Delete").Disabled,"listed: delete enabled");
        Call("OnBbsList",1,24,new List<PartyBbsEntry>{new("PartyLeader",4,83,3,"Need a priest for Dark Stone hunting. All levels welcome.",71,5,nation)});
        await Settled();Field<VBoxContainer>("SeekParty.seekListBox").GetChildren().OfType<Control>().First().EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left});
        await Capture("recruiting");
        PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Recruit").EmitSignal(BaseButton.SignalName.Pressed);
        await Capture("recruit-form");
        PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Recruit").EmitSignal(BaseButton.SignalName.Pressed);
        PartyTree(boardPanel).OfType<Button>().Single(b=>b.Name=="seek_filter_toggle").EmitSignal(BaseButton.SignalName.Pressed);
        await Capture("filters");
        var filterPanel=PartyTree(boardPanel).OfType<Control>().Single(c=>c.Name=="seek_filter_panel");
        Check(filterPanel.Visible && PartyTree(filterPanel).OfType<OptionButton>().Single().IsVisibleInTree() && PartyTree(filterPanel).OfType<CheckBox>().Count(c=>c.IsVisibleInTree())==2,"filter panel shows the native board filters");
        var sameZone=PartyTree(filterPanel).OfType<CheckBox>().First();
        sameZone.ButtonPressed=true;await Settled();
        Check(((LibreKO.Domain.PartyBoardFilter)Native.Get<object>(world, "SeekParty.seekFilter")!).SameZone,"filter check runs the native board filter");
        sameZone.ButtonPressed=false;
        PartyTree(boardPanel).OfType<Button>().Single(b=>b.Name=="seek_filter_toggle").EmitSignal(BaseButton.SignalName.Pressed);
        Game.Session.Party.SeedPreview(members.Take(2).ToArray());Call("Party.RefreshUI");await Capture("two-members");
        Game.Session.Party.SeedPreview();Call("Party.RefreshUI");Call("OnBbsList",0,0,new List<PartyBbsEntry>());await Capture("empty");
        Check(PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Private").Disabled,"empty: stale private message disabled");
        Check(!PartyTree(boardPanel).OfType<Button>().Single(b=>b.Text=="Invite").Disabled,"empty: Invite stays available for invite by name");
        // Full Moon Rift board (3277df5 Auto Entry): the client's actions in the filter panel.
        SetField("SeekParty.SeekBoardKind",PartyBoardType.FullMoon);
        SetField("ZoneId",71);
        Game.Session.Self.Sheet.SeedProgress(80,0,100);
        Call("SeekParty.ApplySeekBoard");
        Call("OnBbsList",0,1,new List<PartyBbsEntry>{new("MoonLeader",0,80,3,"Full Moon Rift run",71,5,nation)});
        PartyTree(boardPanel).OfType<Button>().Single(b=>b.Name=="seek_filter_toggle").EmitSignal(BaseButton.SignalName.Pressed);
        await Capture("full-moon");
        var moonPanel=PartyTree(boardPanel).OfType<Control>().Single(c=>c.Name=="seek_filter_panel");
        var auto=PartyTree(moonPanel).OfType<Button>().Single(b=>b.Name=="seek_full_moon_auto");
        Check(auto.IsVisibleInTree() && auto.Text=="Auto Entry" && moonPanel.GetGlobalRect().Encloses(auto.GetGlobalRect()),"full moon: Auto Entry sits in the board's lower panel "+auto.GetGlobalRect());
        Check(PartyTree(moonPanel).OfType<Button>().Where(b=>b.IsVisibleInTree()).Select(b=>b.Name.ToString()).SequenceEqual(new[]{"seek_full_moon_enter","seek_full_moon_shout","seek_full_moon_auto"}),"full moon: Enter, Party Board and Auto Entry, named");
        var moonTitle=Field<Label>("SeekParty.seekHeading").Text;
        Check(moonTitle.Length>0 && PartyTree(boardPanel).OfType<Label>().Any(l=>l.IsVisibleInTree() && l.Text==moonTitle),"full moon: the board takes the client's Full Moon title");
        auto.EmitSignal(BaseButton.SignalName.Pressed);
        Check((bool)Native.Get<object>(world, "SeekParty.fullMoonAutoEntry")!,"full moon: Auto Entry starts the client's automatic entry for a fitting party");
        SetField("SeekParty.fullMoonAutoEntry",false);
        Config.SetPartyRowMeter(PartyBoardMeterMode.Off);
        System.IO.File.WriteAllText(output+"/"+prefix+"-verification.json",JsonSerializer.Serialize(new{nation,checks,captures,pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://../bin/KnightOnlineUiClassic.dll"))))},new JsonSerializerOptions{WriteIndented=true}));
        world.Free(); net.Free();
        GD.Print("PARTY_AUDIT_OK "+prefix+" "+checks.Count);
    }
}
