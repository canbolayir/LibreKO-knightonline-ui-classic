using Godot;
using System.Reflection;
using System.Text.Json;
using LibreKO;
using LibreKO.Network;
using LibreKO.Domain;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;

public partial class Preview
{
    private static object? DetailField(WorldRoot world,string name) => PreviewFixtures.Field<object>(world, name);
    private static object? DetailCall(WorldRoot world,string name,params object?[] args)
    {
        var fixtureArgs=args.Prepend(world).ToArray();
        var fixture=typeof(PreviewFixtures).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)
            .SingleOrDefault(m=>m.Name==name && m.GetParameters().Length==fixtureArgs.Length && m.GetParameters().Select((p,i)=>fixtureArgs[i]==null || p.ParameterType.IsInstanceOfType(fixtureArgs[i])).All(valid=>valid));
        if(fixture!=null) return fixture.Invoke(null,fixtureArgs);
        bool Fits(MethodInfo m,bool exact)=>m.Name==name && (exact?m.GetParameters().Length==args.Length
            : m.GetParameters().Length>args.Length && m.GetParameters().Skip(args.Length).All(p=>p.HasDefaultValue))
            && m.GetParameters().Take(args.Length).Select((p,i)=>args[i]==null || p.ParameterType.IsInstanceOfType(args[i])).All(valid=>valid);
        object root=PreviewFixtures.Target(world,ref name);
        int dot=name.LastIndexOf('.');
        object target=dot<0?root:Native.Get<object>(root,name[..dot]) ?? throw new Exception("No client object at "+name[..dot]);
        name=name[(dot+1)..];
        var methods=target.GetType().GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);
        // A method that gained trailing optional parameters (ff78eef: OfferSlot's amount) still answers the old call.
        var method=methods.SingleOrDefault(m=>Fits(m,true)) ?? methods.Single(m=>Fits(m,false));
        var full=method.GetParameters().Length==args.Length?args
            :args.Concat(method.GetParameters().Skip(args.Length).Select(p=>p.DefaultValue)).ToArray();
        return method.Invoke(method.IsStatic?null:target,full);
    }
    private async Task CaptureDetails(int nation)
    {
        var clientPack=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../build/client/LibreKO.pck"));
        if(!ProjectSettings.LoadResourcePack(clientPack,false)) throw new Exception("Built client resource pack could not be mounted");
        var installedContent=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"));
        if(System.IO.File.Exists(installedContent) && !ProjectSettings.LoadResourcePack(installedContent,false)) throw new Exception("Installed item artwork could not be mounted");
        var window=GetWindow();window.ContentScaleMode=Window.ContentScaleModeEnum.Disabled;window.Size=new Vector2I(1000,700);
        window.GuiEmbedSubwindows=true;
        AddChild(new ColorRect { Color=new Color("252822"),Size=new Vector2(1000,700),MouseFilter=MouseFilterEnum.Ignore });
        var offline=new Net();typeof(Net).GetProperty("I")!.SetValue(null,offline);
        Native.Set(Game.Session.Clan, "MyClan",new MyClanInfo { InClan=true,ClanId=1,AllianceId=1,Fame=ClanRanks.Chief,Name="canCLAN",Notice="Meet at Moradon",Flag=ClanTypes.Accredited5,MaxMembers=50 });
        var output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/character-composed-audit"));System.IO.Directory.CreateDirectory(output);
        if(OS.GetCmdlineUserArgs().Contains("quest-polish-audit"))
        {
            output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/quest-polish-audit"));System.IO.Directory.CreateDirectory(output);
        }
        if(OS.GetCmdlineUserArgs().Contains("hud-npc-response-audit"))
        {
            output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/hud-npc-response-audit"));System.IO.Directory.CreateDirectory(output);
        }
        if(OS.GetCmdlineUserArgs().Contains("npc-live-mail-audit"))
        {
            output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/npc-live-mail-audit"));System.IO.Directory.CreateDirectory(output);
        }
        if(OS.GetCmdlineUserArgs().Contains("npc-portrait-fit-audit") || OS.GetCmdlineUserArgs().Contains("npc-portrait-width-audit"))
        {
            output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/"+(OS.GetCmdlineUserArgs().Contains("npc-portrait-width-audit")?"npc-portrait-width-audit":"npc-portrait-fit-audit")));System.IO.Directory.CreateDirectory(output);
        }
        else if(OS.GetCmdlineUserArgs().Contains("npc-portraits-all-audit"))
        {
            output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/npc-portraits-all-audit"));System.IO.Directory.CreateDirectory(output);
        }
        else if(OS.GetCmdlineUserArgs().Contains("npc-portrait-pilot-audit"))
        {
            output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/npc-portrait-pilot-audit"));System.IO.Directory.CreateDirectory(output);
        }
        if(OS.GetCmdlineUserArgs().Contains("quest-negative-check"))
        {
            output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/quest-details-selection-audit/negative"));System.IO.Directory.CreateDirectory(output);
        }
        bool presetAudit=OS.GetCmdlineUserArgs().Contains("stat-preset-base-audit");
        if(presetAudit)
        {
            output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/stat-preset-base-audit"));System.IO.Directory.CreateDirectory(output);
        }
        var shell=new Control();AddChild(shell);
        var host=(LibreKO.Plugins.WindowHost)Create(typeof(LibreKO.Plugins.WindowHost),"character_info","Character",shell,(Action)(()=>{}));
        var character=new CharacterWindow(host) { Position=new Vector2(40,40) };AddChild(character);
        string prefix=nation==1?"karus":"human";
        // The client attaches its user card bridge to its global game API; the Classic card reads the plugin's.
        PreviewFixtures.LinkClientFacades(KnightOnlineUiClassic.Plugin.Kit.Game);
        var measurements=new List<object>();
        foreach(string id in CharacterDetailsSkin.WindowIds)
        {
            if(OS.GetCmdlineUserArgs().Contains("showcase-details") && id=="npc_dialog") continue;
            // The NPC page is the Classic NPC dialogue, the fallback of the bundled 2.x quest windows (myko-npc-audit's).
            UseArt(id!="npc_dialog");
            var model=PreviewFixtures.Prepared(new WorldRoot());
            string caption=id switch { "presets"=>"Presets","titles"=>"Titles","quests"=>"Quests","character_clan_details"=>"Knights Management","clanpoint"=>"Clan Contribution","quest_target"=>"Target","quest_receipt"=>"Reward","npc_dialog"=>"Quest","userinfo"=>"Character Info",_=>id };
            var panel=new HudWindow(id,caption,new Vector2(40,40),persistLayout:false);
            var body=panel.Body;
            character.Visible=CharacterPageRouter.Supports(id);
            if(id=="presets")
            {
                DetailCall(model,"SkillPreset.BuildPresetSlotBar",body);DetailCall(model,"SkillPreset.BuildPresetStatBlock",body);DetailCall(model,"SkillPreset.BuildPresetSkillBlock",body);
                var plans=(PresetPlan[])DetailField(model,"SkillPreset.presetPlans")!;
                for(int i=0;i<plans.Length;i++) plans[i]=new PresetPlan();
                plans[0].SetStats([185,55,12,0,0]);plans[0].SetTrees([60,0,0,10]);
                Native.Set(Game.Session, "Self.Class", nation==1?106:206);
                Game.Session.Self.Sheet.SeedStats(250,120,72,50,50,50);Game.Session.Self.Sheet.SeedProgress(83,0,100);
                Game.Session.Self.Mastery.ResetTrees(148);
                DetailCall(model,"SkillPreset.RefreshPresetUI");
            }
            else if(id=="titles")
            {
                DetailCall(model,"TitlePicker.BuildPanel");var built=(HudWindow)DetailField(model,"TitlePicker.panel")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                ((Label)DetailField(model,"TitlePicker.hint")!).Text="Every earned title bonus already counts. Choose the name shown above your character.";
                var list=(VBoxContainer)DetailField(model,"TitlePicker.rows")!;
                foreach(var entry in new[]{(0,"No title",""),(1,"Street Smart","Defense +10"),(2,"Protector of El Morad","HP +150, Resistance +5"),(3,"Champion of the Border Defense War","Attack +3%, National points +5%")})
                    list.AddChild((Control)DetailCall(model,"TitlePicker.BuildRow",entry.Item1,entry.Item2,entry.Item3)!);
            }
            else if(id=="quests")
            {
                DetailCall(model,"Quest.BuildLog");body.AddChild((Control)DetailField(model,"Quest.QuestsContent")!);
                foreach(var tabs in Descendants(body).OfType<HBoxContainer>())
                    if(tabs.GetChildren().OfType<Button>().FirstOrDefault(b=>b.ToggleMode) is {} tab) tab.SetPressedNoSignal(true);
                ((Control)DetailField(model,"Quest.detailEmpty")!).Visible=false;((Control)DetailField(model,"Quest.detailBody")!).Visible=true;
                ((Label)DetailField(model,"Quest.detailTitle")!).Text="Defeat the Monsters of Moradon";
                ((Label)DetailField(model,"Quest.detailSub")!).Text="Hunt / Level 10 / In Progress";
                ((RichTextLabel)DetailField(model,"Quest.detailDesc")!).Text="Help the guards protect Moradon. Defeat the creatures near the village, then return to the quest NPC for your reward.";
                var goals=(VBoxContainer)DetailField(model,"Quest.objectiveBox")!;
                goals.AddChild(LibreKO.UiKit.ValueRow("Worm","7 / 10",new Color("fff080")));
                goals.AddChild(LibreKO.UiKit.ValueRow("Bandicoot","5 / 5",new Color("8ff099")));
                var list=(VBoxContainer)DetailField(model,"Quest.listBox")!;
                foreach(var child in list.GetChildren()) child.QueueFree();
                int questId=500;
                foreach(string title in new[]{"A New Beginning","Defeat the Monsters of Moradon","Daily Supply Delivery","Path of the Warrior"})
                {
                    var objectives=new QuestObjectives(questId,false,new[]{new QuestKillGroup(10,new[]{1},"Worm")});
                    ((Dictionary<int,QuestObjectives>)DetailField(model,"Session.QuestBook.Objectives")!)[questId]=objectives;
                    QuestTransfer[] payouts=questId==501?[new(false,2,0,25000,0),new(false,1,0,10000,0),new(false,0,810418000,2,0)]:questId==502?[new(false,2,0,850,0)]:[];
                    var fixture=new QuestView(questId,1,21,false,false,false,false,QuestViewState.InProgress,0,title,"Help the guards.","",objectives,new ushort[]{7},payouts,Array.Empty<string>());
                    if(questId==503) fixture=fixture with { Transfers=[new(true,1,0,500,0),new(false,2,0,1000,0)],Options=[new(false,0,810418000,2,0),new(false,1,0,3500,0)] };
                    ((Dictionary<int,QuestView>)DetailField(model,"Session.QuestBook.Views")!)[questId]=fixture;
                    ((List<QuestEntry>)DetailField(model,"Session.QuestBook.Entries")!).Add(new QuestEntry(questId,1));
                    ((Dictionary<int,ushort[]>)DetailField(model,"Session.QuestBook.Kills")!)[questId]=new ushort[]{7};
                    list.AddChild((Control)DetailCall(model,"Quest.BuildRow",questId,1,"Canbo")!);questId++;
                }
                ((Control)DetailField(model,"Quest.detailBody")!).Visible=false;
                ((Control)DetailField(model,"Quest.detailEmpty")!).Visible=true;
                ((Dictionary<string,HudWindow>)DetailField(model,"MainWindows.Windows")!)["Quests"]=panel;
                CharacterPanelBridge.For(model).SelectPage("quest");
                panel.Visible=false;
            }
            else if(id=="character_clan_details")
            {
                var nativeRoot=new VBoxContainer { CustomMinimumSize=new Vector2(560,0) };body.AddChild(nativeRoot);
                DetailCall(model,"Clan.BuildMineView",nativeRoot);
                var status=new Label { Text="Clan notice and contribution are up to date." };nativeRoot.AddChild(status);
                Native.Set(model, "Clan.status", status);
                ((Control)DetailField(model,"Clan.mineBox")!).Visible=true;
                foreach(var entry in new[]{("Clan.nameLbl","canCLAN"),("Clan.standingLbl","Accredited Knights / Grade 3"),("Clan.dutyLbl","Chief / 4 members online"),("Clan.fundLbl","Contribution: 125,000"),("Clan.noticeLbl","Meet at Moradon. Prepare for the next war.")}) ((Label)DetailField(model,entry.Item1)!).Text=entry.Item2;
                ((Button)DetailField(model,"Clan.leaveBtn")!).Text="Disband";
                var list=(VBoxContainer)DetailField(model,"CommandList.clan")!;
                var members=(List<ClanMember>)DetailField(model,"Clan.members")!;
                members.AddRange(new[]{new ClanMember { Name="Canbo",Fame=ClanRanks.Chief,Level=83,Class=205,IsOnline=true,Memo="" },new ClanMember { Name="Guardian",Fame=ClanRanks.ViceChief,Level=80,Class=201,IsOnline=true,Memo="" },new ClanMember { Name="Magician",Fame=3,Level=78,Class=203,IsOnline=false,HoursSinceLogin=48,Memo="" }});
                DetailCall(model,"Clan.RenderMembers");
                foreach(var tabs in Descendants(body).OfType<HBoxContainer>())
                    if(tabs.GetChildren().OfType<Button>().FirstOrDefault(b=>b.ToggleMode) is {} tab) tab.SetPressedNoSignal(true);
            }
            else if(id=="clanpoint")
            {
                try { DetailCall(model,"ClanPoints.Build"); } catch(TargetInvocationException e) when(e.InnerException is NullReferenceException) { }
                var built=(HudWindow)DetailField(model,"ClanPoints.PointsPanel")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                ((Label)DetailField(model,"ClanPoints.pointsMineLbl")!).Text="2,025,525,525";
                ((Label)DetailField(model,"ClanPoints.pointsFundLbl")!).Text="125,000 (12 points)";
                ((CheckBox)DetailField(model,"ClanPoints.pointsAuto")!).ButtonPressed=true;
                ((Label)DetailField(model,"ClanPoints.pointsStatus")!).Text="Only accredited Knights can save Contribution.";
            }
            else if(id=="quest_target")
            {
                DetailCall(model,"QuestTarget.EnsureTargetWindow");var built=(HudWindow)DetailField(model,"QuestTarget.TargetWindow")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                ((VBoxContainer)DetailField(model,"QuestTarget.targetText")!).AddChild(new Label { Text="Bandicoot\nLook near the fields outside Moradon.",AutowrapMode=TextServer.AutowrapMode.WordSmart });
                ((Label)DetailField(model,"QuestTarget.targetCaption")!).Text="Moradon";
            }
            else if(id=="userinfo")
            {
                DetailCall(model,"UserInfo.Build");var built=(HudWindow)DetailField(model,"UserInfo.Panel")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                // 3277df5: the card comes from the server reply through the client's own handler (rank boards for the row titles).
                DetailCall(model,"DailyRankWindow.LoadBoards");
                DetailCall(model,"UserInfo.OnInformation",UserInfoFixture(nation,true));
            }
            else if(id=="quest_receipt")
            {
                QuestReceived(model,new QuestReceipt(62,new[]{new QuestReceiptEntry(900001000,25000),new QuestReceiptEntry(900000000,10000)}));
                var built=(HudWindow)DetailField(model,"QuestReceipt.ReceiptWindow")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                Native.Set(model, "QuestReceipt.ReceiptWindow", panel);
            }
            else if(id=="npc_dialog")
            {
                panel.Title="Defeat the Monsters of Moradon";
                DetailCall(model,"NpcDialogWindow.BuildNpcDialog");
                var built=(HudWindow)DetailField(model,"NpcDialogWindow.Panel")!;
                DetailCall(model,"ShowQuestView",new QuestView(62,1,21,true,false,true,false,QuestViewState.Claimable,0,
                    "Defeat the Monsters of Moradon","Return to the guard.","You have protected Moradon. Choose your reward before confirming the quest.",
                    new QuestObjectives(62,false,[new QuestKillGroup(10,[1],"Worm")]),[10],[new QuestTransfer(false,2,0,25000,0)],[])
                    { Options=[new QuestTransfer(false,1,0,10000,0),new QuestTransfer(false,2,0,30000,0)] });
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                Native.Set(model, "NpcDialogWindow.Panel", panel);
            }
            var originalActions=Descendants(body).OfType<BaseButton>().Select(b=>b.GetInstanceId()).ToHashSet();
            if(id is "titles" or "presets")
            {
                string field=id=="titles"?"TitlePicker.panel":"SkillPreset.presetPanel";
                string shown=id=="titles"?"TitlePicker.Shown":"SkillPreset.presetShown";
                string close=id=="titles"?"CloseTitlePicker":"ClosePreset";
                Native.Set(model, field, panel);
                Native.Set(model, shown, true);
                panel.Closed+=()=>DetailCall(model,close);
            }
            int beforeButtons=originalActions.Count;
            AddChild(panel);await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
            NativeCharacterNpc.Prepare(panel,model);
            var composed=CharacterDetailsSkin.Apply(body)!;
            if(id=="quests")
            {
                var nativePanel=CharacterPanelBridge.For(model);
                _windowData!.LiveCharacterPanel=nativePanel;
                var connField=typeof(Net).GetField("conn",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var connection=connField.GetValue(offline);
                // Any redundant request must fail in this isolated offline check.
                connField.SetValue(offline,null);
                try
                {
                    nativePanel.Act("quest_details","501");
                    CharacterPageRouter.Present("quests");
                    await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
                    if((int)DetailField(model,"Quest.Selected")! !=501 || !((Control)DetailField(model,"Quest.detailBody")!).IsVisibleInTree() || ((Control)DetailField(model,"Quest.detailEmpty")!).IsVisibleInTree()) throw new Exception("Native Details lost the selected quest");
                    if(((Label)DetailField(model,"Quest.detailTitle")!).Text!="Defeat the Monsters of Moradon") throw new Exception("Native Details opened the wrong quest");
                    CheckRewardRows(model,[(900001000,25000),(900000000,10000),(810418000,2)]);
                    nativePanel.Act("quest_details","502");
                    if((int)DetailField(model,"Quest.Selected")! !=502 || ((Label)DetailField(model,"Quest.detailTitle")!).Text!="Daily Supply Delivery") throw new Exception("Native Details failed to change the selected quest");
                    CheckRewardRows(model,[(900001000,850)]);
                    nativePanel.Act("quest_details","501");
                    GD.Print("NATIVE_QUEST_DETAILS_SELECTION_OK: cached native selection, no list reload, second quest, return to first");
                }
                finally { connField.SetValue(offline,connection);_windowData.LiveCharacterPanel=null; }
            }
            if(id=="titles") ((Label)DetailField(model,"TitlePicker.hint")!).Text="3 earned. Every claimed title's bonus already counts — this only picks the name shown above your character.";
            if(id=="character_clan_details") ((Label)DetailField(model,"Clan.noticeLbl")!).Text=string.Join(" ",Enumerable.Repeat("Prepare for the next war. Meet at Moradon.",5));
            panel.Position=new Vector2(40,40);
            await ToSignal(GetTree().CreateTimer(.5),SceneTreeTimer.SignalName.Timeout);
            if(composed is ClassicDetailPanel && id is "npc_dialog" or "quest_receipt")
            {
                var expected=((GetViewportRect().Size-panel.Size)/2).Floor();
                if(panel.Position!=expected) throw new Exception(id+": content window did not open at the screen centre");
                var opened=(ClassicDetailPanel)composed;
                int frames=opened.GetMeta("classic_open_frames").AsInt32();
                if(frames>6) throw new Exception(id+": opening waited more than six layout frames");
                GD.Print($"QUEST_OPEN_SETTLED_OK: {id}, frames={frames}, ms={opened.GetMeta("classic_open_ms").AsInt64()}");
                GD.Print("QUEST_CENTER_OK "+id+" "+panel.Position+" "+panel.Size);
            }
            if(composed is ClassicNpcDialogue dialogue)
            {
                if(panel.Position!=((GetViewportRect().Size-panel.Size)/2).Floor()) throw new Exception(id+": NPC dialogue did not open at the screen centre");
                if(dialogue.Modulate.A<1) throw new Exception(id+": NPC dialogue opening did not settle");
                GD.Print("QUEST_CENTER_OK "+id+" "+panel.Position+" "+panel.Size);
            }
            var presentedActions=Descendants(body).Concat(Descendants(composed)).OfType<BaseButton>().Select(b=>b.GetInstanceId()).Distinct().Where(originalActions.Contains).ToHashSet();
            int afterButtons=presentedActions.Count;
            if(beforeButtons!=afterButtons) throw new Exception(id+": a live action control was lost");
            if(!originalActions.SetEquals(presentedActions)) throw new Exception(id+": native action instances were replaced");
            ScrollContainer viewport=composed switch { CharacterEmbeddedPage embedded=>embedded.ContentScroll,ClassicNpcDialogue npc=>npc.QuestScroll,_=>((ClassicDetailPanel)composed).ContentScroll };
            var fixedNodes=Descendants(body).Concat(Descendants(composed)).OfType<Control>().Distinct().Where(c=>c.HasMeta("classic_fixed_rect") || c.HasMeta("embedded_rect")).ToArray();
            var failures=fixedNodes.Where(c=> { var r=c.GetMeta(c.HasMeta("embedded_rect")?"embedded_rect":"classic_fixed_rect").AsRect2();return c.Position!=r.Position || c.Size!=r.Size; }).Select(c=>c.Name.ToString()).ToArray();
            if(failures.Length>0)
            {
                foreach(var c in fixedNodes.Where(c=>failures.Contains(c.Name.ToString())))
                {
                    GD.Print($"BOUND_FAIL {c.Name} actual={c.Position}/{c.Size} min={c.GetCombinedMinimumSize()}");
                    foreach(var ch in c.GetChildren().OfType<Control>()) GD.Print($"CHILD_MIN {ch.Name} {ch.GetCombinedMinimumSize()}");
                }
                throw new Exception(id+": rendered control bounds differ from the composed layout: "+string.Join(", ",failures));
            }
            if(id is "userinfo" or "quest_target" && composed is ClassicDetailPanel floating)
            {
                float end=body.GetChildren().OfType<Control>().Where(c=>c.Visible && !(c is Label { Text.Length:0 })).Max(c=>c.Position.Y+c.Size.Y);
                float gap=floating.Size.Y-(floating.ContentScroll.Position.Y+end);
                if(gap<20 || gap>26) throw new Exception(id+": bottom margin below the content is "+gap);
                if(floating.ContentScroll.GetVScrollBar().Visible) throw new Exception(id+": fitted content still scrolls: viewport="+floating.ContentScroll.Size+" body="+body.GetCombinedMinimumSize()+" "+string.Join(",",body.GetChildren().OfType<Control>().Select(c=>c.GetType().Name+":"+c.Visible+":"+c.GetRect()+":"+c.GetCombinedMinimumSize())));
                var title=Descendants(floating).OfType<Label>().First(l=>l.IsVisibleInTree() && l.Text==caption);
                float centre=title.GetGlobalRect().GetCenter().X-floating.GlobalPosition.X;
                if(title.HorizontalAlignment!=HorizontalAlignment.Center || Mathf.Abs(centre-floating.Size.X/2)>1) throw new Exception(id+": caption is not centred: "+centre);
                GD.Print($"DETAIL_FIT_OK {id} height={floating.Size.Y} gap={gap}");
            }
            if(id=="userinfo") CheckUserInfoCard(body,true);
            var overflow=Descendants(composed).OfType<Control>().Where(c=>c.IsVisibleInTree() && c.GetParent()==composed && (c.Position.X<0 || c.Position.Y<0 || c.Position.X+c.Size.X>composed.Size.X+1 || c.Position.Y+c.Size.Y>composed.Size.Y+1)).Select(c=>c.Name.ToString()).ToArray();
            if(overflow.Length>0) throw new Exception(id+": controls escape the shell: "+string.Join(", ",overflow));
            measurements.Add(new { id,buttons=afterButtons,fixedControls=fixedNodes.Length,fixedMismatch=failures,overflow,composition=composed.GetType().Name,bodyWidth=body.Size.X,viewportWidth=viewport.Size.X,shellWidth=composed.Size.X,shellHeight=composed.Size.Y });
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-"+id+".png"));
            GD.Print("DETAIL_CAPTURE "+prefix+"-"+id);
            if(id=="quests")
            {
                var nativePanel=CharacterPanelBridge.For(model);
                foreach(int selectedQuest in new[]{502,503,500})
                {
                    nativePanel.Act("quest_details",selectedQuest.ToString());
                    await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
                    var expected=selectedQuest==502?new[]{(900001000,850)}:selectedQuest==503?new[]{(900001000,1000),(810418000,2),(900000000,3500)}:Array.Empty<(int,int)>();
                    CheckRewardRows(model,expected);
                    if(selectedQuest==503 && !Descendants(QuestRewardRows(model)).OfType<Label>().Any(l=>l.Text=="Reward options")) throw new Exception("Reward choices appear as guaranteed payouts");
                    foreach(var icon in Descendants(composed).OfType<TextureRect>())
                        if(icon.IsVisibleInTree() && (icon.Size.X<32 || icon.Size.Y<32)) throw new Exception("A rendered quest reward icon collapsed");
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    string state=selectedQuest==502?"one":selectedQuest==503?"choices":"none";
                    GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-quests-rewards-"+state+".png"));
                }
                GD.Print("QUEST_REWARD_SELECTION_OK: three payouts, one payout, alternatives, excluded hand-in cost, no rewards");
                await CaptureLongQuest(model,(CharacterEmbeddedPage)composed,nativePanel,prefix,output);
            }
            if(id=="npc_dialog")
            {
                var confirm=Descendants(body).OfType<Button>().Single(b=>b.Text=="Confirm");
                if(!confirm.Disabled) throw new Exception("Reward confirmation must wait for a choice");
                var choice=Descendants(body).OfType<Control>().First(c=>c.TooltipText=="Pick this reward");
                choice.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=true });
                await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
                if(confirm.Disabled || (int)DetailField(model,"NpcDialogWindow.questRewardChoice")! !=0) throw new Exception("Native reward selection callback was lost");
                var picked=QuestRewardChoices(model);
                if(!picked.Chosen(62,out var reward) || reward != new QuestTransfer(false,1,0,10000,0)) throw new Exception("Reward choice was not retained for its quest");
                DetailCall(model,"OnQuestView",new QuestView(99,1,21,false,false,false,false,QuestViewState.InProgress,0,"Unrelated quest","","",new QuestObjectives(99,false,[]),[],[],[]));
                if((int)DetailField(model,"NpcDialogWindow.questRewardChoice")! !=0 || !picked.Chosen(62,out var kept) || kept!=reward) throw new Exception("Unrelated quest refresh erased the reward choice");
                GD.Print("QUEST_PENDING_CHOICE_OK");
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-npc-reward-selected.png"));
                var rewardView=new QuestView(62,1,21,true,false,true,false,QuestViewState.Claimable,0,
                    "Defeat the Monsters of Moradon","Return to the guard.","You have protected Moradon. Choose your reward before confirming the quest.",
                    new QuestObjectives(62,false,[new QuestKillGroup(10,[1],"Worm")]),[10],[new QuestTransfer(false,2,0,25000,0)],[])
                    { Options=[new(false,1,0,10000,0),new(false,2,0,30000,0)] };
                DetailCall(model,"ShowQuestView",rewardView);
                if((int)DetailField(model,"NpcDialogWindow.questRewardChoice")! !=0 || Descendants(body).OfType<Button>().Single(b=>b.Text=="Confirm").Disabled)
                    throw new Exception("Reopening the quest erased its pending reward choice");
                DetailCall(model,"OnQuestView",rewardView with { State=QuestViewState.Available,CanClaim=false,CanAccept=true,Counts=[0],Dialogue="Help the guards protect Moradon. Defeat the creatures, then return for your reward." });
                // Rebuilt native rows are freed at the end of the frame.
                var liveRows=Descendants(body).OfType<Control>().Where(c=>!Ancestry(c).Any(n=>n.IsQueuedForDeletion())).ToArray();
                // Upstream previews the alternatives on the offer page; only pickable rows are forbidden there.
                if(liveRows.Any(c=>c.TooltipText=="Pick this reward"))
                    throw new Exception("Quest acceptance exposes an uncommitted reward selection");
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                var cards=Descendants(composed).OfType<ClassicNpcCard>().ToArray();
                if(cards.Any(c=>!GodotObject.IsInstanceValid(c.Source))) throw new Exception("Composed quest kept cards bound to freed native rows");
                if(cards.Any(c=>c.Source.HasMeta("quest_reward_selected")))
                    throw new Exception("Composed quest acceptance exposes a reward picker");
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-npc-reward-offer.png"));
                GD.Print("QUEST_REWARD_STAGE_OK: restored claim selection; acceptance has no reward picker");
                foreach(var state in new[]{QuestViewState.InProgress,QuestViewState.Completed})
                {
                    if(state==QuestViewState.Completed) QuestReceived(model,new QuestReceipt(62,[new(900001000,25000),new(900000000,10000)]));
                    DetailCall(model,"ShowQuestView",rewardView with { State=state,CanClaim=false,Counts=[state==QuestViewState.Completed?(ushort)10:(ushort)5],Dialogue=state==QuestViewState.Completed?"You have protected Moradon. Your reward has been granted.":"Defeat the remaining creatures, then return to the guard." });
                    await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
                    var status=Descendants(body).OfType<Label>().Single(l=>l.HasMeta("quest_status"));
                    // The native caption stays hidden; the composed header carries the state ink.
                    if(((ClassicNpcDialogue)composed).Status is not {} shown || shown.GetThemeColor("font_color")!=ClassicQuestStatus.Ink(state) || shown.Text!=status.Text) throw new Exception("Composed NPC status is missing or uses the wrong ink");
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-npc-status-"+state.ToString().ToLowerInvariant()+".png"));
                }
                GD.Print("NPC_STATUS_INK_OK: available, in progress, ready, completed");
                await CaptureNpcFixtures(model,(ClassicNpcDialogue)composed,prefix,output,measurements);
            }
            if(id=="userinfo" && composed is ClassicDetailPanel card)
            {
                // A clanless character without rank marks: the card shrinks to the facts and the clan line.
                DetailCall(model,"UserInfo.OnInformation",UserInfoFixture(nation,false));
                await ToSignal(GetTree().CreateTimer(.5),SceneTreeTimer.SignalName.Timeout);
                CheckUserInfoCard(body,false);
                float end=body.GetChildren().OfType<Control>().Where(c=>c.Visible).Max(c=>c.Position.Y+c.Size.Y);
                float gap=card.Size.Y-(card.ContentScroll.Position.Y+end);
                if(gap<20 || gap>26) throw new Exception("userinfo clanless: bottom margin below the content is "+gap);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-userinfo-clanless.png"));
                GD.Print($"DETAIL_FIT_OK userinfo-clanless height={card.Size.Y} gap={gap}");
                var confirm=Descendants(body).OfType<Button>().Single(b=>b.Text=="Confirm" && b.IsVisibleInTree());
                confirm.EmitSignal(BaseButton.SignalName.Pressed);
                if((bool)DetailField(model,"UserInfo.shown")!) throw new Exception("userinfo: Confirm did not close the card through the client");
                GD.Print("USER_INFO_CONFIRM_OK");
            }
            if(id=="character_clan_details")
            {
                var memo=Descendants(composed).OfType<Button>().SingleOrDefault(b=>b.Text=="Memo" && b.IsVisibleInTree())??throw new Exception("Knights Management: the clan memo action is not placed");
                if(memo.GetRect()!=CharacterEmbeddedLayout.Slots["clan_memo"]) throw new Exception("Knights Management: memo bounds "+memo.GetRect());
                foreach(var slot in new[]{"clan_refresh","clan_memo","clan_save","clan_board","clan_handover","clan_leave"})
                    if(Descendants(composed).OfType<Button>().FirstOrDefault(b=>b.IsVisibleInTree() && b.GetRect()==CharacterEmbeddedLayout.Slots[slot]) is {} placed
                        && Plugin.Kit.Bold.GetStringSize(placed.Text,HorizontalAlignment.Left,-1,placed.GetThemeFontSize("font_size")).X>placed.Size.X-4)
                        throw new Exception("Knights Management: caption does not fit "+placed.Text+" "+placed.Size);
                GD.Print("CLAN_MEMO_ACTION_OK "+memo.GetRect());
                var tabType=Native.MemberType(model,"Clan.tab")!;
                Native.Set(model,"Clan.tab",Enum.Parse(tabType,"Points"));
                foreach(var button in Descendants(composed).OfType<Button>().Where(b=>b.ToggleMode)) button.SetPressedNoSignal(button.Text=="Contribution");
                DetailCall(model,"Clan.OnDonationList",new List<(string,int)> { ("Canbo",125000),("Guardian",65000),("Magician",12000) });
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-clan-contribution.png"));
                Native.Set(model,"Clan.tab",Enum.Parse(tabType,"Union"));
                foreach(var button in Descendants(composed).OfType<Button>().Where(b=>b.ToggleMode)) button.SetPressedNoSignal(button.Text=="Union");
                DetailCall(model,"Clan.OnAllianceList","Prepare for the next war.",new List<AllianceClanEntry> { new AllianceClanEntry { Id=1,Name="canCLAN",InAlliance=true,Officers=new List<AllianceOfficer> { new() { Fame=ClanRanks.Chief,Name="Canbo" } } },new AllianceClanEntry { Id=2,Name="Guardians",InAlliance=true,Officers=new List<AllianceOfficer> { new() { Fame=ClanRanks.Chief,Name="Guardian" } } } });
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-clan-confederacy.png"));
                try { DetailCall(model,"ClanPoints.Build"); } catch(TargetInvocationException e) when(e.InnerException is NullReferenceException) { }
                var donation=(HudWindow)DetailField(model,"ClanPoints.PointsPanel")!;
                if(donation.GetParent()!=null) donation.Reparent(this);else AddChild(donation);
                var nested=CharacterDetailsSkin.Apply(donation.Body) as CharacterEmbeddedPage ?? throw new Exception("Donation subpage was not composed");
                Native.Set(model, "ClanPoints.PointsShown", true);
                donation.Visible=true;CharacterPageRouter.Present("clanpoint");
                await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                if(composed.Visible || !nested.Visible) throw new Exception("Nested donation did not replace management content");
                nested.Back?.Invoke();
                if(!composed.Visible || nested.Visible || (bool)DetailField(model,"ClanPoints.PointsShown")!) throw new Exception("Nested Back did not restore management and close the native model");
                GD.Print("NESTED_DONATION_BACK_OK");
                nested.QueueFree();donation.QueueFree();
            }
            if(composed is CharacterEmbeddedPage routed)
            {
                if(presetAudit && id=="presets") await CapturePresetStates(model,routed,output,prefix,nation);
                int closed=0;panel.Closed+=()=>closed++;
                routed.Back?.Invoke();
                if(routed.Visible || closed!=1) throw new Exception("Embedded Back did not close its native lifecycle");
                if(id is "titles" or "presets")
                    if((bool)DetailField(model,id=="titles"?"TitlePicker.Shown":"SkillPreset.presetShown")!) throw new Exception("Native model stayed open after Back");
                if(id=="titles")
                {
                    panel.Visible=true;CharacterPageRouter.Present(id);
                    if(!routed.Visible || !panel.Visible) throw new Exception("Native reopen did not restore the embedded page");
                    panel.Visible=false;
                    await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                    if(routed.Visible || closed!=1) throw new Exception("External native hide did not dismiss the page cleanly");
                    panel.Visible=true;CharacterPageRouter.Present(id);
                    var frame=(KnightOnlineUiClassic.Layout.LayoutView)typeof(CharacterWindow).GetField("_frame",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(character)!;
                    frame.Where(n=>n.Id=="btn_friends").First().Control.EmitSignal(BaseButton.SignalName.Pressed);
                    if(routed.Visible || panel.Visible || closed!=2) throw new Exception("Upper tab did not exit the native subpage lifecycle");
                    GD.Print("EMBEDDED_REOPEN_HIDE_TAB_OK");
                }
                routed.QueueFree();
            }
            panel.QueueFree();model.Free();await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        }
        character.Visible=false;
        var confirmation=new ConfirmationDialog { Title="Hand over leadership",DialogText="Hand the clan over to Guardian? You will no longer be its chief.",Exclusive=false };
        AddChild(confirmation);CharacterDetailsSkin.StyleDialog(confirmation);confirmation.PopupCentered();
        await ToSignal(GetTree().CreateTimer(.5),SceneTreeTimer.SignalName.Timeout);
        GD.Print("CONFIRM_OK "+confirmation.GetOkButton().Text+" size="+confirmation.GetOkButton().Size+" min="+confirmation.GetOkButton().CustomMinimumSize+" parent="+confirmation.GetOkButton().GetParent<Control>().Size);
        {
            // Like the native clan prompts (PopupCentered without a size), the box fits its message: no empty band.
            var label=confirmation.GetLabel();var ok=confirmation.GetOkButton();
            float gap=ok.GetGlobalRect().Position.Y-label.GetGlobalRect().End.Y;
            if(confirmation.Size.X!=360 || label.Position.Y!=102 || Mathf.Abs(label.Size.Y-label.GetMinimumSize().Y)>1 || gap!=16)
                throw new Exception($"Clan confirmation must fit its message: size={confirmation.Size} label={label.GetRect()} gap={gap}");
            GD.Print("CONFIRM_FIT size="+confirmation.Size+" label="+label.GetRect()+" gap="+gap);
        }
        GD.Print("CONFIRM_CANCEL "+confirmation.GetCancelButton().Text+" size="+confirmation.GetCancelButton().Size+" min="+confirmation.GetCancelButton().CustomMinimumSize);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-confirmation.png"));
        int accepted=0,canceled=0;confirmation.Confirmed+=()=>accepted++;confirmation.Canceled+=()=>canceled++;
        CharacterDetailsSkin.StyleDialog(confirmation);
        confirmation.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        if(accepted!=1 || confirmation.Visible) throw new Exception("Native confirmation callback failed");
        confirmation.PopupCentered();confirmation.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        if(canceled!=1 || confirmation.Visible) throw new Exception("Native cancel callback failed");
        confirmation.QueueFree();
        var file=ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/KnightOnlineUiClassic.dll");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,prefix+"-measurements.json"),JsonSerializer.Serialize(new { measurements,
            pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(file))),
            clientHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!,"LibreKO.dll")))) },new JsonSerializerOptions { WriteIndented=true }));
        GD.Print("DETAIL_AUDIT_OK");
        offline.Free();
    }
    private async Task CaptureLongQuest(WorldRoot model,CharacterEmbeddedPage page,IGameCharacterPanel native,string prefix,string output)
    {
        var view=new QuestView(65,1,21,false,false,false,false,QuestViewState.InProgress,0,"Kecoon hunting",
            "[Sentinel] Patrick asked me to hunt five Kecoons for him.","",new QuestObjectives(65,false,[new QuestKillGroup(5,[1],"Kecoon")]),[0],
            [new(false,2,0,1875,0),new(false,1,0,2000,0)],[])
            { Options=new[]{330150005,330150015,330150025,330150035,330150075}.Select(id=>new QuestTransfer(false,0,id,1,0)).ToArray() };
        var views=(Dictionary<int,QuestView>)DetailField(model,"Session.QuestBook.Views")!;
        var entries=(List<QuestEntry>)DetailField(model,"Session.QuestBook.Entries")!;
        views[65]=view;entries.Add(new QuestEntry(65,1));
        ((Dictionary<int,QuestObjectives>)DetailField(model,"Session.QuestBook.Objectives")!)[65]=view.Objectives;
        ((Dictionary<int,ushort[]>)DetailField(model,"Session.QuestBook.Kills")!)[65]=[0];
        native.Act("quest_details","65");page.ContentScroll.ScrollVertical=0;
        async Task Capture(string name)
        {
            await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var actions=Descendants(page).OfType<Button>().Where(b=>b.Text is "Turn in" or "Abandon" or "Track").ToArray();
            if(actions.Any(b=>!b.IsVisibleInTree() || !page.GetGlobalRect().Encloses(b.GetGlobalRect()))) throw new Exception("Quest actions escaped the page");
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-"+name+".png"));
        }
        await Capture("quests-kecoon-top");
        var rail=page.QuestScrollRail!;
        if(!rail.Visible || rail.Bar.MaxValue<=rail.Bar.Page) throw new Exception("Long quest has no usable scrollbar");
        var arrows=rail.GetChildren().OfType<TextureButton>().ToArray();
        arrows[1].EmitSignal(BaseButton.SignalName.Pressed);
        if(rail.Bar.Value!=32) throw new Exception("Quest down arrow failed");
        var track=rail.GetChildren().OfType<KnightOnlineUiClassic.Layout.ClassicScrollTrack>().Single();
        track._GuiInput(new InputEventMouseButton { Pressed=true,ButtonIndex=MouseButton.WheelDown });
        if(rail.Bar.Value!=64) throw new Exception("Quest rail wheel failed");
        rail.Bar.Value=0;
        track._GuiInput(new InputEventMouseButton { Pressed=true,ButtonIndex=MouseButton.Left,Position=new Vector2(9,track.Size.Y-1) });
        if(rail.Bar.Value<=0) throw new Exception("Quest track paging failed");
        rail.Bar.Value=rail.Bar.MaxValue-rail.Bar.Page;
        await Capture("quests-kecoon-bottom");
        var last=Descendants(QuestRewardRows(model)).OfType<Control>().Last(c=>c.HasMeta("quest_reward_item_id"));
        if(!page.ContentScroll.GetGlobalRect().Encloses(last.GetGlobalRect())) throw new Exception("Last reward cannot be fully scrolled into view");
        GD.Print("QUEST_SCROLL_BOUNDS_OK: native range, arrows, wheel, track paging, last option, fixed actions");
        var pending=QuestRewardChoices(model);
        pending.Choose(65,view.Options[2]);views[65]=view with { State=QuestViewState.Claimable,CanClaim=true,Counts=[5] };entries[entries.FindIndex(q=>q.QuestId==65)]=new QuestEntry(65,3);
        ((Dictionary<int,ushort[]>)DetailField(model,"Session.QuestBook.Kills")!)[65]=[5];
        DetailCall(model,"Quest.RefreshDetail");page.ContentScroll.ScrollVertical=0;
        CheckRewardRows(model,[(900001000,1875),(900000000,2000),(330150025,1)]);
        if(!Descendants(QuestRewardRows(model)).OfType<Label>().Any(l=>l.Text=="Selected reward")) throw new Exception("Pending choice is not identified");
        await Capture("quests-selected-reward");
        entries[entries.FindIndex(q=>q.QuestId==65)]=new QuestEntry(65,2);views[65]=view with { State=QuestViewState.Completed };
        QuestReceived(model,new QuestReceipt(65,[new(900001000,1875),new(900000000,2000),new(330150025,1)]));
        CheckRewardRows(model,[(900001000,1875),(900000000,2000),(330150025,1)]);
        if(pending.Chosen(65,out _) || ((Label)DetailField(model,"Quest.rewardTitle")!).Text!="Received rewards") throw new Exception("Granted rewards were confused with pending options");
        await Capture("quests-received-rewards");
        GD.Print("QUEST_GRANTED_REWARDS_OK");
    }
    private static Control QuestRewardRows(WorldRoot model) => (Control)((Node)DetailField(model,"Quest.detailBody")!).FindChild(NativeQuestLog.RewardBoxName,true,false)!;
    private static QuestRewardSelection<QuestTransfer> QuestRewardChoices(WorldRoot model) => (QuestRewardSelection<QuestTransfer>)DetailField(model,"Session.QuestBook.RewardSelection")!;
    private static void QuestReceived(WorldRoot model,QuestReceipt receipt) { NativeQuestLog.Record(receipt);DetailCall(model,"QuestReceipt.OnReceipt",receipt); }
    private static IEnumerable<Node> Ancestry(Node? node) { for(;node!=null;node=node.GetParent()) yield return node; }
    /// <summary>A user card reply as the server sends it: a clan member with rank marks, or a clanless character.</summary>
    private static UserInfoReply UserInfoFixture(int nation,bool full) => full
        ? new UserInfoReply("Guardian",83,nation==1?106:206,2_025_525,120_000,17,3,1,4,"NightWatch","Cassandra",2,5,3,
            new[]{4,255,37,0,120,0,1},new[]{new RankMark(0,2,1,0),new RankMark(1,3,0,0),new RankMark(2,0,0,1),new RankMark(51,0,0,2)})
        : new UserInfoReply("Ranger7",45,nation==1?107:207,18_400,900,0,0,0,0,"","",0,0,UserInfoCard.NoParty,new int[UserInfoCard.RankPlaces],Array.Empty<RankMark>());

    /// <summary>The Classic card's lines at their declared bounds, inside the content width, with the client's card values.</summary>
    private static void CheckUserInfoCard(Control body,bool full)
    {
        var card=body.GetChildren().OfType<ClassicUserInfoCard>().Single();
        if(!card.Visible || body.GetChildren().OfType<Control>().Count(c=>c.Visible)!=2) throw new Exception("userinfo: the card and the Confirm row are the visible content");
        var info=KnightOnlineUiClassic.Plugin.Kit.Game.UserInfo.Card??throw new Exception("userinfo: the user-card API has no card");
        foreach(var part in card.GetChildren().OfType<Control>())
        {
            var rect=part.GetMeta("user_info_rect").AsRect2();
            if(part.GetRect()!=rect) throw new Exception("userinfo: actual bounds of "+part.Name+" "+part.GetRect()+" != "+rect);
            if(part.GetRect().End.X>card.Size.X+.5f || part.GetRect().End.Y>card.Size.Y+.5f) throw new Exception("userinfo: "+part.Name+" leaves the card");
        }
        if(card.GetNode<Label>("fact_value_0").Text!=info.Name || card.GetNode<Label>("fact_value_1").Text!=info.Level) throw new Exception("userinfo: the facts are not the client's card");
        foreach(var label in card.GetChildren().OfType<Label>().Where(l=>l.Text.Length>0))
            if(label.GetThemeFont("font").GetStringSize(label.Text,HorizontalAlignment.Left,-1,label.GetThemeFontSize("font_size")).X>label.Size.X)
                throw new Exception("userinfo: "+label.Name+" is clipped: "+label.Text);
        int ribbons=card.GetChildren().OfType<ClassicRankRibbon>().Count();
        if(full && (card.GetNode<Label>("clan_value_0").Text!="NightWatch" || ribbons!=info.Summary.Count+info.Rows.Count*3)) throw new Exception("userinfo: clan block or rank marks missing ("+ribbons+")");
        if(!full && (!card.HasNode("clan_none") || card.HasNode("clan_mark") || ribbons!=info.Rows.Count*3)) throw new Exception("userinfo: a clanless card shows the empty clan line and the client's unranked rows ("+ribbons+")");
        // The installed content carries the client's ribbon art (simple_rank_mark_*); every count sits on it.
        var counted=card.GetChildren().OfType<ClassicRankRibbon>().Where(r=>!string.IsNullOrEmpty(r.Ribbon.Count)).ToArray();
        bool ribbonArt=ResourceLoader.Exists("res://assets/ui/daily_rank/marks/simple_rank_mark_0_0.png");
        if(ribbonArt && counted.Any(r=>r.Ribbon.Ribbon==null)) throw new Exception("userinfo: a rank count without the client's ribbon art");
        GD.Print($"USER_INFO_CARD_OK full={full} size={card.Size} ribbons={ribbons} art={ribbonArt} counted={counted.Length}");
    }

    private static IEnumerable<Node> Descendants(Node node) { yield return node;foreach(var child in node.GetChildren()) foreach(var item in Descendants(child)) yield return item; }
    private static void CheckRewardRows(WorldRoot model,(int Item,int Count)[] expected)
    {
        var rewards=QuestRewardRows(model);
        var actual=Descendants(rewards).OfType<Control>().Where(c=>c.HasMeta("quest_reward_item_id"))
            .Select(c=>(c.GetMeta("quest_reward_item_id").AsInt32(),c.GetMeta("quest_reward_count").AsInt32())).ToArray();
        if(!actual.SequenceEqual(expected)) throw new Exception("Quest Details displayed rewards from the wrong quest");
        foreach(var icon in Descendants(rewards).OfType<TextureRect>())
            if(icon.CustomMinimumSize.X<32 || icon.CustomMinimumSize.Y<32) throw new Exception("Quest reward icon minimum collapsed");
        GD.Print("QUEST_REWARD_ROWS_OK: "+string.Join(",",actual));
    }
    private async Task CaptureNpcFixtures(WorldRoot model,ClassicNpcDialogue shell,string prefix,string output,List<object> measurements)
    {
        var window=(HudWindow)DetailField(model,"NpcDialogWindow.Panel")!;
        async Task Capture(string id)
        {
            await ToSignal(GetTree().CreateTimer(.8),SceneTreeTimer.SignalName.Timeout);
            var rect=new Rect2(Vector2.Zero,shell.Size);
            var outside=Descendants(shell).OfType<Control>().Where(c=>c.IsVisibleInTree() && c.GetParent()==shell && !rect.Grow(.5f).Encloses(c.GetRect())).Select(c=>c.Name.ToString()).ToArray();
            if(outside.Length>0) throw new Exception(id+": shell overflow: "+string.Join(",",outside));
            if(!new Rect2(Vector2.Zero,GetViewportRect().Size).Encloses(window.GetGlobalRect())) throw new Exception(id+": NPC window escaped the viewport: "+window.GetGlobalRect());
            if(shell.Modulate.A<1) throw new Exception(id+": NPC dialogue did not settle");
            if(!shell.Portrait.Visible && shell.Speech.Size!=KnightOnlineUiClassic.Layout.ClassicNpcLayout.SpeechWide.Size) throw new Exception(id+": speech leaves the empty portrait slot");
            foreach(var arrow in Descendants(shell).OfType<Button>().Where(b=>b.Name.ToString() is "npc_previous_page" or "npc_next_page" && b.IsVisibleInTree()))
                if(arrow.Disabled && arrow.GetThemeStylebox("disabled") is StyleBoxTexture dim && dim.ModulateColor.R>.6f) throw new Exception(id+": a disabled page arrow looks enabled");
            CheckNpcOwnership(shell);
            var actions=Descendants(shell).OfType<ClassicNpcAction>().Where(b=>b.IsVisibleInTree()).ToArray();
            measurements.Add(new { id,shellWidth=shell.Size.X,shellHeight=shell.Size.Y,page=shell.Page,pageCount=shell.PageCount,overflow=outside,fixedMismatch=Array.Empty<string>(),buttons=actions.Length,
                actionBounds=actions.Select(b=>b.GetGlobalRect().ToString()).ToArray() });
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-"+id+".png"));
        }
        ClassicNpcAction Topic(string text) => Descendants(shell).OfType<ClassicNpcAction>().Single(b=>b.IsVisibleInTree() && b.Source?.Text.Contains(text)==true);
        DetailCall(model,"NpcDialogWindow.Begin","[Guard] Patrick","What mission are you going to undertake?");
        int selected=-1;
        string[] topics={"[In progress] Orc Watcher hunting","Worm hunt","[In progress] Bandicoot hunting","[Ready] Kecoon hunting","[Completed] Bulcan hunting","Silk bundle delivery","Apples of Moradon","Close"};
        for(int i=0;i<topics.Length;i++) { int index=i;DetailCall(model,"AddNpcMenuButton",$"{i+1}.   {topics[i]}",(Action)(()=>selected=index)); }
        DetailCall(model,"NpcDialogWindow.End",topics.Length);
        await Capture("npc-menu-full");
        if(shell.PageCount!=1 || Descendants(shell).OfType<ClassicNpcAction>().Count(b=>b.IsVisibleInTree() && b.Name!="npc_footer_close")!=topics.Length-1) throw new Exception("The full NPC menu is not shown on one page");
        var states=new Dictionary<string,QuestViewState> { ["Orc Watcher"]=QuestViewState.InProgress,["Kecoon"]=QuestViewState.Claimable,["Bulcan"]=QuestViewState.Completed,["Worm hunt"]=QuestViewState.Available };
        foreach(var state in states)
        {
            var label=Topic(state.Key).GetChildren().OfType<Label>().Single(l=>l.HorizontalAlignment==HorizontalAlignment.Right);
            if(label.GetThemeColor("font_color")!=ClassicQuestStatus.Ink(state.Value)) throw new Exception("NPC menu status colour is incorrect: "+state.Key);
        }
        var hover=Topic("Orc Watcher");
        var point=hover.GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseMotion { Position=point,GlobalPosition=point });await Capture("npc-menu-hover");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=true,Position=point,GlobalPosition=point });await Capture("npc-menu-pressed");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=false,Position=point,GlobalPosition=point });
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventMouseMotion { Position=Vector2.Zero,GlobalPosition=Vector2.Zero });
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(selected!=0) throw new Exception("Pointer input on an NPC option lost its native callback");
        Topic("Apples of Moradon").EmitSignal(BaseButton.SignalName.Pressed);
        if(selected!=6) throw new Exception("The NPC menu lost its original callback index");
        Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_footer_close").EmitSignal(BaseButton.SignalName.Pressed);
        if(selected!=7) throw new Exception("The footer Close lost the server's Close option");
        float menuHeight=shell.Size.Y;
        DetailCall(model,"NpcDialogWindow.Begin","[Guard] Patrick","Select a mission.");
        for(int i=0;i<24;i++) { int index=i;DetailCall(model,"AddNpcMenuButton",$"{i+1}.   Mission {i+1}",(Action)(()=>selected=index)); }
        DetailCall(model,"NpcDialogWindow.End",24);
        if(!window.Visible) window.Visible=true;
        await Capture("npc-menu-overflow");
        if(shell.PageCount<2 || window.Size.Y>GetViewportRect().Size.Y-24) throw new Exception("Long NPC lists must page inside the screen");
        var previous=Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_previous_page");
        if(!previous.Disabled) throw new Exception("The first NPC page must disable its previous arrow");
        var frame=window.GetGlobalRect();
        while(shell.Page<shell.PageCount-1) Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_next_page").EmitSignal(BaseButton.SignalName.Pressed);
        await Capture("npc-menu-overflow-bottom");
        if(window.GetGlobalRect()!=frame) throw new Exception("NPC menu paging moved the window");
        if(!Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_next_page").Disabled) throw new Exception("The last NPC page must disable its next arrow");
        Topic("Mission 24").EmitSignal(BaseButton.SignalName.Pressed);
        if(selected!=23) throw new Exception("The last NPC callback was lost");
        DetailCall(model,"ShowQuestView",new QuestView(63,1,21,true,true,false,false,QuestViewState.Available,0,
            "Bandicoot Tooth","Collect two Apples of Moradon.","Oh? Nowadays, I am totally preoccupied with the manufacturing of medications learned from my teacher but it is so hard to find the materials for practice. Can you find two Apples of Moradon for me?",
            new QuestObjectives(63,false,[]),[],[new QuestTransfer(true,0,810418000,2,0),new QuestTransfer(false,2,0,850,0),new QuestTransfer(false,1,0,3500,0)],[]));
        if(!window.Visible) window.Visible=true;
        await Capture("npc-apple-accept");
        if(shell.Size.Y>=menuHeight) throw new Exception("Short quests retain empty fixed-height space");
        var actions=Descendants(shell).OfType<ClassicNpcAction>().Where(b=>b.IsVisibleInTree()).Select(b=>b.Source?.Text).ToArray();
        if(!actions.Contains("Accept") || !actions.Contains("Reject")) throw new Exception("Quest acceptance actions were lost");
        Descendants(shell).OfType<ClassicNpcAction>().Single(b=>b.Source?.Text=="Reject").EmitSignal(BaseButton.SignalName.Pressed);
        if(window.Visible) throw new Exception("The native Reject callback no longer closes the quest");
        GD.Print("NPC_QUEST_FIT_OK: full menu, status ink, pointer hover/press, original callback indices, paged long menu, dynamic short quest, native Reject, reward selection");
    }
    private static void CheckNpcOwnership(ClassicNpcDialogue shell)
    {
        foreach(var scroll in Descendants(shell).OfType<ScrollContainer>().Where(s=>s.IsVisibleInTree()))
            if(scroll.GetHScrollBar().Visible) throw new Exception("NPC content needs horizontal scrolling");
        foreach(var button in Descendants(shell).OfType<ClassicNpcAction>().Where(b=>b.IsVisibleInTree()))
        {
            if(button.Size.Y!=32) throw new Exception("NPC action bounds regressed: "+button.Source?.Text+" / "+button.Size);
            foreach(string state in new[]{"normal","hover","pressed","hover_pressed","disabled","focus"})
                if(button.GetThemeStylebox(state) is not StyleBoxEmpty) throw new Exception("Generic skin overwrote NPC "+state+" art: "+button.Source?.Text);
            if(button.GetChildren().OfType<ClassicNpcOptionPlate>().Count()!=1) throw new Exception("Duplicate NPC artwork");
            if(!new Rect2(Vector2.Zero,shell.Size).Encloses(new Rect2(button.GlobalPosition-shell.GlobalPosition,button.Size))) throw new Exception("NPC action escaped its frame: "+button.Source?.Text);
        }
        if(shell.Status!=null && shell.Status.IsVisibleInTree()
            && Descendants(shell).OfType<Label>().FirstOrDefault(l=>l.HasMeta("quest_status") && !l.IsQueuedForDeletion()) is {} native
            && shell.Status.GetThemeColor("font_color")!=ClassicQuestStatus.Ink((QuestViewState)native.GetMeta("quest_status").AsInt32()))
            throw new Exception("Composed NPC status lost its ink");
    }
}
