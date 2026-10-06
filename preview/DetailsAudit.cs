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
    private static object? DetailField(World world,string name) => typeof(World).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(world);
    private static object? DetailCall(World world,string name,params object?[] args)
    {
        var method=typeof(World).GetMethods(BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)
            .Single(m=>m.Name==name && m.GetParameters().Length==args.Length && m.GetParameters().Select((p,i)=>args[i]==null || p.ParameterType.IsInstanceOfType(args[i])).All(valid=>valid));
        return method.Invoke(method.IsStatic?null:world,args);
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
        typeof(Net).GetProperty("MyClan")!.SetValue(offline,new MyClanInfo { InClan=true,ClanId=1,AllianceId=1,Fame=ClanRanks.Chief,Name="canCLAN",Notice="Meet at Moradon",Flag=ClanTypes.Accredited5,MaxMembers=50 });
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
        var measurements=new List<object>();
        foreach(string id in CharacterDetailsSkin.WindowIds)
        {
            if(OS.GetCmdlineUserArgs().Contains("showcase-details") && id=="npc_dialog") continue;
            var model=new World();
            string caption=id switch { "presets"=>"Presets","titles"=>"Titles","quests"=>"Quests","character_clan_details"=>"Knights Management","clanpoint"=>"Clan Contribution","quest_target"=>"Target","quest_receipt"=>"Reward","quest_available"=>"Quest available","npc_dialog"=>"Quest","userinfo"=>"User Information",_=>id };
            var panel=new HudWindow(id,caption,new Vector2(40,40),persistLayout:false);
            var body=panel.Body;
            character.Visible=CharacterPageRouter.Supports(id);
            if(id=="presets")
            {
                DetailCall(model,"BuildPresetSlotBar",body);DetailCall(model,"BuildPresetStatBlock",body);DetailCall(model,"BuildPresetSkillBlock",body);
                var plans=(PresetPlan[])DetailField(model,"_presetPlans")!;
                for(int i=0;i<plans.Length;i++) plans[i]=new PresetPlan();
                plans[0].SetStats([185,55,12,0,0]);plans[0].SetTrees([60,0,0,10]);
                typeof(World).GetField("_selfClass",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(model,nation==1?106:206);
                offline.Sheet.SeedStats(250,120,72,50,50,50);offline.Sheet.SeedProgress(83,0,100);
                offline.Mastery.ResetTrees(148);
                DetailCall(model,"RefreshPresetUI");
            }
            else if(id=="titles")
            {
                DetailCall(model,"BuildTitlePicker");var built=(HudWindow)DetailField(model,"_titlePanel")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                ((Label)DetailField(model,"_titleHint")!).Text="Every earned title bonus already counts. Choose the name shown above your character.";
                var list=(VBoxContainer)DetailField(model,"_titleList")!;
                foreach(var entry in new[]{(0,"No title",""),(1,"Street Smart","Defense +10"),(2,"Protector of El Morad","HP +150, Resistance +5"),(3,"Champion of the Border Defense War","Attack +3%, National points +5%")})
                    list.AddChild((Control)DetailCall(model,"BuildTitleRow",entry.Item1,entry.Item2,entry.Item3)!);
            }
            else if(id=="quests")
            {
                DetailCall(model,"BuildQuestLog");body.AddChild((Control)DetailField(model,"_questsContent")!);
                foreach(var tabs in Descendants(body).OfType<HBoxContainer>())
                    if(tabs.GetChildren().OfType<Button>().FirstOrDefault(b=>b.ToggleMode) is {} tab) tab.SetPressedNoSignal(true);
                ((Control)DetailField(model,"_questDetailEmpty")!).Visible=false;((Control)DetailField(model,"_questDetailBody")!).Visible=true;
                ((Label)DetailField(model,"_questDetailTitle")!).Text="Defeat the Monsters of Moradon";
                ((Label)DetailField(model,"_questDetailSub")!).Text="Hunt / Level 10 / In Progress";
                ((RichTextLabel)DetailField(model,"_questDetailDesc")!).Text="Help the guards protect Moradon. Defeat the creatures near the village, then return to the quest NPC for your reward.";
                var goals=(VBoxContainer)DetailField(model,"_questObjectiveBox")!;
                goals.AddChild((Control)DetailCall(model,"QuestValueRow","Worm","7 / 10",new Color("fff080"))!);
                goals.AddChild((Control)DetailCall(model,"QuestValueRow","Bandicoot","5 / 5",new Color("8ff099"))!);
                var rewards=(VBoxContainer)DetailField(model,"_questRewardBox")!;
                var list=(VBoxContainer)DetailField(model,"_questListBox")!;
                foreach(var child in list.GetChildren()) child.QueueFree();
                int questId=500;
                foreach(string title in new[]{"A New Beginning","Defeat the Monsters of Moradon","Daily Supply Delivery","Path of the Warrior"})
                {
                    var objectives=new QuestObjectives(questId,false,new[]{new QuestKillGroup(10,new[]{1},"Worm")});
                    ((Dictionary<int,QuestObjectives>)DetailField(model,"_questObjectives")!)[questId]=objectives;
                    QuestTransfer[] payouts=questId==501?[new(false,2,0,25000,0),new(false,1,0,10000,0),new(false,0,810418000,2,0)]:questId==502?[new(false,2,0,850,0)]:[];
                    var fixture=new QuestView(questId,1,21,false,false,false,false,QuestViewState.InProgress,0,title,"Help the guards.","",objectives,new ushort[]{7},payouts,Array.Empty<string>());
                    if(questId==503) fixture=fixture with { Transfers=[new(true,1,0,500,0),new(false,2,0,1000,0)],Options=[new(false,0,810418000,2,0),new(false,1,0,3500,0)] };
                    ((Dictionary<int,QuestView>)DetailField(model,"_questViews")!)[questId]=fixture;
                    ((List<QuestEntry>)DetailField(model,"_quests")!).Add(new QuestEntry(questId,1));
                    ((Dictionary<int,ushort[]>)DetailField(model,"_questKills")!)[questId]=new ushort[]{7};
                    list.AddChild((Control)DetailCall(model,"BuildQuestRow",questId,1,"Canbo")!);questId++;
                }
                ((Control)DetailField(model,"_questDetailBody")!).Visible=false;
                ((Control)DetailField(model,"_questDetailEmpty")!).Visible=true;
                ((Dictionary<string,HudWindow>)DetailField(model,"_mainWindows")!)["Quests"]=panel;
                var selectedPageField=typeof(World).GetField("_selectedCharacterPage",BindingFlags.Instance|BindingFlags.NonPublic)!;
                selectedPageField.SetValue(model,Enum.Parse(selectedPageField.FieldType,"Quest"));
                panel.Visible=false;
            }
            else if(id=="character_clan_details")
            {
                var nativeRoot=new VBoxContainer { CustomMinimumSize=new Vector2(560,0) };body.AddChild(nativeRoot);
                DetailCall(model,"BuildClanMineView",nativeRoot);
                var status=new Label { Text="Clan notice and contribution are up to date." };nativeRoot.AddChild(status);
                typeof(World).GetField("_clanStatus",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(model,status);
                ((Control)DetailField(model,"_clanMineBox")!).Visible=true;
                foreach(var entry in new[]{("_clanNameLbl","canCLAN"),("_clanStandingLbl","Accredited Knights / Grade 3"),("_clanDutyLbl","Chief / 4 members online"),("_clanFundLbl","Contribution: 125,000"),("_clanNoticeLbl","Meet at Moradon. Prepare for the next war.")}) ((Label)DetailField(model,entry.Item1)!).Text=entry.Item2;
                ((Button)DetailField(model,"_clanLeaveBtn")!).Text="Disband";
                var list=(VBoxContainer)DetailField(model,"_clanList")!;
                var members=(List<ClanMember>)DetailField(model,"_clanMembers")!;
                members.AddRange(new[]{new ClanMember { Name="Canbo",Fame=ClanRanks.Chief,Level=83,Class=205,IsOnline=true,Memo="" },new ClanMember { Name="Guardian",Fame=ClanRanks.ViceChief,Level=80,Class=201,IsOnline=true,Memo="" },new ClanMember { Name="Magician",Fame=3,Level=78,Class=203,IsOnline=false,HoursSinceLogin=48,Memo="" }});
                DetailCall(model,"RenderClanMembers");
                foreach(var tabs in Descendants(body).OfType<HBoxContainer>())
                    if(tabs.GetChildren().OfType<Button>().FirstOrDefault(b=>b.ToggleMode) is {} tab) tab.SetPressedNoSignal(true);
            }
            else if(id=="clanpoint")
            {
                try { DetailCall(model,"ClanPointsInit"); } catch(TargetInvocationException e) when(e.InnerException is NullReferenceException) { }
                var built=(HudWindow)DetailField(model,"_clanPointsPanel")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                ((Label)DetailField(model,"_clanPointsMineLbl")!).Text="2,025,525,525";
                ((Label)DetailField(model,"_clanPointsFundLbl")!).Text="125,000 (12 points)";
                ((CheckBox)DetailField(model,"_clanPointsAuto")!).ButtonPressed=true;
                ((Label)DetailField(model,"_clanPointsStatus")!).Text="Only accredited Knights can save Contribution.";
            }
            else if(id=="quest_target")
            {
                DetailCall(model,"EnsureQuestTargetWindow");var built=(HudWindow)DetailField(model,"_questTargetWindow")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                ((VBoxContainer)DetailField(model,"_questTargetText")!).AddChild(new Label { Text="Bandicoot\nLook near the fields outside Moradon.",AutowrapMode=TextServer.AutowrapMode.WordSmart });
                ((Label)DetailField(model,"_questTargetCaption")!).Text="Moradon";
            }
            else if(id=="userinfo")
            {
                DetailCall(model,"UserInfoInit");var built=(HudWindow)DetailField(model,"_userInfoPanel")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                foreach(var row in new[]{("Name","Guardian"),("Level","83 (rebirth 5)"),("Class","Protector"),("Clan","canCLAN"),("Clan rank","Accredited Knights"),("Clan leader","Canbo"),("National points","2,025,525,525"),("Monthly NP","120,000")})
                    DetailCall(model,"AddUserInfoRow",row.Item1,row.Item2,null);
            }
            else if(id=="quest_receipt")
            {
                DetailCall(model,"OnQuestReceipt",new QuestReceipt(62,new[]{new QuestReceiptEntry(900001000,25000),new QuestReceiptEntry(900000000,10000)}));
                var built=(HudWindow)DetailField(model,"_questReceiptWindow")!;
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                typeof(World).GetField("_questReceiptWindow",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(model,panel);
            }
            else if(id=="quest_available")
            {
                DetailCall(model,"EnsureQuestNotificationWindow");
                var built=(HudWindow)DetailField(model,"_questNotificationWindow")!;
                var notifications=(List<QuestView>)DetailField(model,"_questNotifications")!;
                for(int i=0;i<43;i++) notifications.Add(new QuestView(62+i,1,21,true,true,false,false,QuestViewState.Available,0,
                    i==0?"A New Beginning":"Daily Supply Delivery","Speak to the guards.",i==0?"Welcome to Moradon. Help the guards protect the village.":string.Join(" ",Enumerable.Repeat("The guards need help delivering supplies before the next battle.",4)),new QuestObjectives(62+i,false,[]),[],[],i==0?["Tell me more","I will help","Not now"]:["I will bring the supplies"]));
                for(int index=1;index<=3;index++) notifications[index]=notifications[index] with { State=index==1?QuestViewState.InProgress:index==2?QuestViewState.Claimable:QuestViewState.Completed };
                DetailCall(model,"RefreshQuestNotification");
                panel.Title=$"Quests available (1 of {notifications.Count})";
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                typeof(World).GetField("_questNotificationWindow",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(model,panel);
            }
            else if(id=="npc_dialog")
            {
                panel.Title="Defeat the Monsters of Moradon";
                DetailCall(model,"BuildNpcDialog");
                var built=(HudWindow)DetailField(model,"_npcPanel")!;
                DetailCall(model,"ShowQuestView",new QuestView(62,1,21,true,false,true,false,QuestViewState.Claimable,0,
                    "Defeat the Monsters of Moradon","Return to the guard.","You have protected Moradon. Choose your reward before confirming the quest.",
                    new QuestObjectives(62,false,[new QuestKillGroup(10,[1],"Worm")]),[10],[new QuestTransfer(false,2,0,25000,0)],[])
                    { Options=[new QuestTransfer(false,1,0,10000,0),new QuestTransfer(false,2,0,30000,0)] });
                foreach(var control in built.Body.GetChildren().OfType<Control>().ToArray()) control.Reparent(body);
                typeof(World).GetField("_npcPanel",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(model,panel);
            }
            var originalActions=Descendants(body).OfType<BaseButton>().Select(b=>b.GetInstanceId()).ToHashSet();
            if(id is "titles" or "presets")
            {
                string field=id=="titles"?"_titlePanel":"_presetPanel";
                string shown=id=="titles"?"_titleShown":"_presetShown";
                string close=id=="titles"?"CloseTitlePicker":"ClosePreset";
                typeof(World).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(model,panel);
                typeof(World).GetField(shown,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(model,true);
                panel.Closed+=()=>DetailCall(model,close);
            }
            int beforeButtons=originalActions.Count;
            AddChild(panel);await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
            var composed=CharacterDetailsSkin.Apply(body)!;
            if(id=="quests")
            {
                var nativePanel=(LibreKO.Plugins.IGameCharacterPanel)Activator.CreateInstance(typeof(World).GetNestedType("CharacterPanelBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{model},null)!;
                _windowData!.LiveCharacterPanel=nativePanel;
                var connField=typeof(Net).GetField("_conn",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var connection=connField.GetValue(offline);
                // Any redundant request must fail in this isolated offline check.
                connField.SetValue(offline,null);
                try
                {
                    nativePanel.Act("quest_details","501");
                    CharacterPageRouter.Present("quests");
                    await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
                    if((int)DetailField(model,"_questSelected")! !=501 || !((Control)DetailField(model,"_questDetailBody")!).IsVisibleInTree() || ((Control)DetailField(model,"_questDetailEmpty")!).IsVisibleInTree()) throw new Exception("Native Details lost the selected quest");
                    if(((Label)DetailField(model,"_questDetailTitle")!).Text!="Defeat the Monsters of Moradon") throw new Exception("Native Details opened the wrong quest");
                    CheckRewardRows(model,[(900001000,25000),(900000000,10000),(810418000,2)]);
                    nativePanel.Act("quest_details","502");
                    if((int)DetailField(model,"_questSelected")! !=502 || ((Label)DetailField(model,"_questDetailTitle")!).Text!="Daily Supply Delivery") throw new Exception("Native Details failed to change the selected quest");
                    CheckRewardRows(model,[(900001000,850)]);
                    nativePanel.Act("quest_details","501");
                    GD.Print("NATIVE_QUEST_DETAILS_SELECTION_OK: cached native selection, no list reload, second quest, return to first");
                }
                finally { connField.SetValue(offline,connection);_windowData.LiveCharacterPanel=null; }
            }
            if(id=="titles") ((Label)DetailField(model,"_titleHint")!).Text="3 earned. Every claimed title's bonus already counts — this only picks the name shown above your character.";
            if(id=="character_clan_details") ((Label)DetailField(model,"_clanNoticeLbl")!).Text=string.Join(" ",Enumerable.Repeat("Prepare for the next war. Meet at Moradon.",5));
            panel.Position=new Vector2(40,40);
            await ToSignal(GetTree().CreateTimer(.5),SceneTreeTimer.SignalName.Timeout);
            if(composed is ClassicDetailPanel && id is "npc_dialog" or "quest_available" or "quest_receipt")
            {
                var expected=((GetViewportRect().Size-panel.Size)/2).Floor();
                if(panel.Position!=expected) throw new Exception(id+": content window did not open at the screen centre");
                var opened=(ClassicDetailPanel)composed;
                int frames=opened.GetMeta("classic_open_frames").AsInt32();
                if(frames>6) throw new Exception(id+": opening waited more than six layout frames");
                GD.Print($"QUEST_OPEN_SETTLED_OK: {id}, frames={frames}, ms={opened.GetMeta("classic_open_ms").AsInt64()}");
                GD.Print("QUEST_CENTER_OK "+id+" "+panel.Position+" "+panel.Size);
            }
            var presentedActions=Descendants(body).Concat(Descendants(composed)).OfType<BaseButton>().Select(b=>b.GetInstanceId()).Distinct().Where(originalActions.Contains).ToHashSet();
            int afterButtons=presentedActions.Count;
            if(beforeButtons!=afterButtons) throw new Exception(id+": a live action control was lost");
            if(!originalActions.SetEquals(presentedActions)) throw new Exception(id+": native action instances were replaced");
            var viewport=composed is CharacterEmbeddedPage embedded?embedded.ContentScroll:((ClassicDetailPanel)composed).ContentScroll;
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
            var overflow=Descendants(composed).OfType<Control>().Where(c=>c.IsVisibleInTree() && c.GetParent()==composed && (c.Position.X<0 || c.Position.Y<0 || c.Position.X+c.Size.X>composed.Size.X+1 || c.Position.Y+c.Size.Y>composed.Size.Y+1)).Select(c=>c.Name.ToString()).ToArray();
            if(overflow.Length>0) throw new Exception(id+": controls escape the shell: "+string.Join(", ",overflow));
            measurements.Add(new { id,buttons=afterButtons,fixedControls=fixedNodes.Length,fixedMismatch=failures,overflow,composition=composed.GetType().Name,bodyWidth=body.Size.X,viewportWidth=viewport.Size.X,shellWidth=composed.Size.X,shellHeight=composed.Size.Y });
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-"+id+".png"));
            GD.Print("DETAIL_CAPTURE "+prefix+"-"+id);
            if(id=="quests")
            {
                var nativePanel=(LibreKO.Plugins.IGameCharacterPanel)Activator.CreateInstance(typeof(World).GetNestedType("CharacterPanelBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{model},null)!;
                foreach(int selectedQuest in new[]{502,503,500})
                {
                    nativePanel.Act("quest_details",selectedQuest.ToString());
                    await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
                    var expected=selectedQuest==502?new[]{(900001000,850)}:selectedQuest==503?new[]{(900001000,1000),(810418000,2),(900000000,3500)}:Array.Empty<(int,int)>();
                    CheckRewardRows(model,expected);
                    if(selectedQuest==503 && !Descendants((Node)DetailField(model,"_questRewardBox")!).OfType<Label>().Any(l=>l.Text=="Reward options")) throw new Exception("Reward choices appear as guaranteed payouts");
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
                if(confirm.Disabled || (int)DetailField(model,"_questRewardChoice")! !=0) throw new Exception("Native reward selection callback was lost");
                var picked=(Dictionary<int,QuestTransfer>)DetailField(model,"_pendingQuestRewards")!;
                if(!picked.TryGetValue(62,out var reward) || reward != new QuestTransfer(false,1,0,10000,0)) throw new Exception("Reward choice was not retained for its quest");
                DetailCall(model,"OnQuestView",new QuestView(99,1,21,false,false,false,false,QuestViewState.InProgress,0,"Unrelated quest","","",new QuestObjectives(99,false,[]),[],[],[]));
                if((int)DetailField(model,"_questRewardChoice")! !=0 || picked[62]!=reward) throw new Exception("Unrelated quest refresh erased the reward choice");
                GD.Print("QUEST_PENDING_CHOICE_OK");
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-npc-reward-selected.png"));
                var rewardView=new QuestView(62,1,21,true,false,true,false,QuestViewState.Claimable,0,
                    "Defeat the Monsters of Moradon","Return to the guard.","You have protected Moradon. Choose your reward before confirming the quest.",
                    new QuestObjectives(62,false,[new QuestKillGroup(10,[1],"Worm")]),[10],[new QuestTransfer(false,2,0,25000,0)],[])
                    { Options=[new(false,1,0,10000,0),new(false,2,0,30000,0)] };
                DetailCall(model,"ShowQuestView",rewardView);
                if((int)DetailField(model,"_questRewardChoice")! !=0 || Descendants(body).OfType<Button>().Single(b=>b.Text=="Confirm").Disabled)
                    throw new Exception("Reopening the quest erased its pending reward choice");
                DetailCall(model,"OnQuestView",rewardView with { State=QuestViewState.Available,CanClaim=false,CanAccept=true,Counts=[0],Dialogue="Help the guards protect Moradon. Defeat the creatures, then return for your reward." });
                if(Descendants(body).OfType<Control>().Any(c=>c.TooltipText=="Pick this reward") || Descendants(body).OfType<Label>().Any(l=>l.Text=="Choose one"))
                    throw new Exception("Quest acceptance exposes an uncommitted reward selection");
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-npc-reward-offer.png"));
                GD.Print("QUEST_REWARD_STAGE_OK: restored claim selection; acceptance has no reward picker");
                foreach(var state in new[]{QuestViewState.InProgress,QuestViewState.Completed})
                {
                    if(state==QuestViewState.Completed) DetailCall(model,"OnQuestReceipt",new QuestReceipt(62,[new(900001000,25000),new(900000000,10000)]));
                    DetailCall(model,"ShowQuestView",rewardView with { State=state,CanClaim=false,Counts=[state==QuestViewState.Completed?(ushort)10:(ushort)5],Dialogue=state==QuestViewState.Completed?"You have protected Moradon. Your reward has been granted.":"Defeat the remaining creatures, then return to the guard." });
                    await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
                    var status=Descendants(body).OfType<Label>().Single(l=>l.HasMeta("quest_status"));
                    if(status.GetThemeColor("font_color")!=ClassicQuestStatus.Ink(state)) throw new Exception("NPC status colour is incorrect");
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-npc-status-"+state.ToString().ToLowerInvariant()+".png"));
                }
                GD.Print("NPC_STATUS_INK_OK: available, in progress, ready, completed");
                await CaptureNpcFixtures(model,(ClassicDetailPanel)composed,prefix,output,measurements);
            }
            if(id=="quest_available")
            {
                var anchor=panel.Position;
                foreach(var label in Descendants(body).OfType<Label>().Where(l=>l.HasMeta("quest_status")))
                    if(label.GetThemeColor("font_color")!=ClassicQuestStatus.Ink((QuestViewState)label.GetMeta("quest_status").AsInt32())) throw new Exception("Initial notification status colour is incorrect");
                var initialHeight=panel.Size.Y;
                var arrows=Descendants(body).OfType<Button>().Where(b=>b.TooltipText is "Previous quest" or "Next quest").Select(b=>b.GlobalPosition).ToArray();
                DetailCall(model,"StepQuestNotification",1);
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
                if(panel.Position!=anchor || !arrows.SequenceEqual(Descendants(body).OfType<Button>().Where(b=>b.TooltipText is "Previous quest" or "Next quest").Select(b=>b.GlobalPosition))) throw new Exception("Notification paging moved its top edge or arrows");
                if(panel.Size.Y==initialHeight) throw new Exception("Notification height did not follow different content");
                GD.Print("QUEST_PAGING_ANCHOR_OK");
                if(!Descendants(body).OfType<Label>().Any(l=>l.Text=="Daily Supply Delivery")) throw new Exception("Native notification paging failed");
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-quest-notification-next.png"));
                for(int step=0;step<12;step++)
                {
                    DetailCall(model,"StepQuestNotification",1);
                    if(Descendants(body).OfType<Button>().Any(b=>!b.HasMeta("classic_npc_plate") || b.GetThemeStylebox("normal") is not StyleBoxEmpty))
                        throw new Exception("A freshly paged notification exposed its default style");
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    foreach(var label in Descendants(body).OfType<Label>().Where(l=>l.HasMeta("quest_status")))
                        if(label.GetThemeColor("font_color")!=ClassicQuestStatus.Ink((QuestViewState)label.GetMeta("quest_status").AsInt32())) throw new Exception("First-frame notification status colour is incorrect");
                    if(panel.Position!=anchor || !arrows.SequenceEqual(Descendants(body).OfType<Button>().Where(b=>b.TooltipText is "Previous quest" or "Next quest").Select(b=>b.GlobalPosition)))
                        throw new Exception("First-frame notification geometry moved its paging controls");
                }
                GD.Print("QUEST_PAGING_FIRST_FRAME_OK: 12 page transitions");
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
                if(panel.Position!=anchor) throw new Exception("Rapid quest paging moved the window");
                panel.Visible=false;panel.Position=Vector2.Zero;panel.Visible=true;
                await ToSignal(GetTree().CreateTimer(.5),SceneTreeTimer.SignalName.Timeout);
                if(panel.Position!=((GetViewportRect().Size-panel.Size)/2).Floor()) throw new Exception("Notification did not reopen at the centre");
                GD.Print("QUEST_DYNAMIC_REOPEN_OK");
            }
            if(id=="character_clan_details")
            {
                var tabField=typeof(World).GetField("_clanTab",BindingFlags.NonPublic|BindingFlags.Instance)!;
                tabField.SetValue(model,Enum.Parse(tabField.FieldType,"Points"));
                foreach(var button in Descendants(composed).OfType<Button>().Where(b=>b.ToggleMode)) button.SetPressedNoSignal(button.Text=="Contribution");
                DetailCall(model,"OnClanDonationList",new List<(string,int)> { ("Canbo",125000),("Guardian",65000),("Magician",12000) });
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-clan-contribution.png"));
                tabField.SetValue(model,Enum.Parse(tabField.FieldType,"Union"));
                foreach(var button in Descendants(composed).OfType<Button>().Where(b=>b.ToggleMode)) button.SetPressedNoSignal(button.Text=="Union");
                DetailCall(model,"OnAllianceList","Prepare for the next war.",new List<AllianceClanEntry> { new AllianceClanEntry { Id=1,Name="canCLAN",InAlliance=true,Officers=new List<AllianceOfficer> { new() { Fame=ClanRanks.Chief,Name="Canbo" } } },new AllianceClanEntry { Id=2,Name="Guardians",InAlliance=true,Officers=new List<AllianceOfficer> { new() { Fame=ClanRanks.Chief,Name="Guardian" } } } });
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-clan-confederacy.png"));
                try { DetailCall(model,"ClanPointsInit"); } catch(TargetInvocationException e) when(e.InnerException is NullReferenceException) { }
                var donation=(HudWindow)DetailField(model,"_clanPointsPanel")!;
                if(donation.GetParent()!=null) donation.Reparent(this);else AddChild(donation);
                var nested=CharacterDetailsSkin.Apply(donation.Body) as CharacterEmbeddedPage ?? throw new Exception("Donation subpage was not composed");
                typeof(World).GetField("_clanPointsShown",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(model,true);
                donation.Visible=true;CharacterPageRouter.Present("clanpoint");
                await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                if(composed.Visible || !nested.Visible) throw new Exception("Nested donation did not replace management content");
                nested.Back?.Invoke();
                if(!composed.Visible || nested.Visible || (bool)DetailField(model,"_clanPointsShown")!) throw new Exception("Nested Back did not restore management and close the native model");
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
                    if((bool)DetailField(model,id=="titles"?"_titleShown":"_presetShown")!) throw new Exception("Native model stayed open after Back");
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
        AddChild(confirmation);CharacterDetailsSkin.StyleDialog(confirmation);confirmation.PopupCentered(new Vector2I(360,230));
        await ToSignal(GetTree().CreateTimer(.5),SceneTreeTimer.SignalName.Timeout);
        GD.Print("CONFIRM_OK "+confirmation.GetOkButton().Text+" size="+confirmation.GetOkButton().Size+" min="+confirmation.GetOkButton().CustomMinimumSize+" parent="+confirmation.GetOkButton().GetParent<Control>().Size);
        GD.Print("CONFIRM_CANCEL "+confirmation.GetCancelButton().Text+" size="+confirmation.GetCancelButton().Size+" min="+confirmation.GetCancelButton().CustomMinimumSize);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-confirmation.png"));
        int accepted=0,canceled=0;confirmation.Confirmed+=()=>accepted++;confirmation.Canceled+=()=>canceled++;
        CharacterDetailsSkin.StyleDialog(confirmation);
        confirmation.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        if(accepted!=1 || confirmation.Visible) throw new Exception("Native confirmation callback failed");
        confirmation.PopupCentered(new Vector2I(360,230));confirmation.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
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
    private async Task CaptureLongQuest(World model,CharacterEmbeddedPage page,LibreKO.Plugins.IGameCharacterPanel native,string prefix,string output)
    {
        var view=new QuestView(65,1,21,false,false,false,false,QuestViewState.InProgress,0,"Kecoon hunting",
            "[Sentinel] Patrick asked me to hunt five Kecoons for him.","",new QuestObjectives(65,false,[new QuestKillGroup(5,[1],"Kecoon")]),[0],
            [new(false,2,0,1875,0),new(false,1,0,2000,0)],[])
            { Options=new[]{330150005,330150015,330150025,330150035,330150075}.Select(id=>new QuestTransfer(false,0,id,1,0)).ToArray() };
        var views=(Dictionary<int,QuestView>)DetailField(model,"_questViews")!;
        var entries=(List<QuestEntry>)DetailField(model,"_quests")!;
        views[65]=view;entries.Add(new QuestEntry(65,1));
        ((Dictionary<int,QuestObjectives>)DetailField(model,"_questObjectives")!)[65]=view.Objectives;
        ((Dictionary<int,ushort[]>)DetailField(model,"_questKills")!)[65]=[0];
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
        var last=Descendants((Node)DetailField(model,"_questRewardBox")!).OfType<Control>().Last(c=>c.HasMeta("quest_reward_item_id"));
        if(!page.ContentScroll.GetGlobalRect().Encloses(last.GetGlobalRect())) throw new Exception("Last reward cannot be fully scrolled into view");
        GD.Print("QUEST_SCROLL_BOUNDS_OK: native range, arrows, wheel, track paging, last option, fixed actions");
        var pending=(Dictionary<int,QuestTransfer>)DetailField(model,"_pendingQuestRewards")!;
        pending[65]=view.Options[2];views[65]=view with { State=QuestViewState.Claimable,CanClaim=true,Counts=[5] };entries[entries.FindIndex(q=>q.QuestId==65)]=new QuestEntry(65,3);
        ((Dictionary<int,ushort[]>)DetailField(model,"_questKills")!)[65]=[5];
        DetailCall(model,"RefreshQuestDetail");page.ContentScroll.ScrollVertical=0;
        CheckRewardRows(model,[(900001000,1875),(900000000,2000),(330150025,1)]);
        if(!Descendants((Node)DetailField(model,"_questRewardBox")!).OfType<Label>().Any(l=>l.Text=="Selected reward")) throw new Exception("Pending choice is not identified");
        await Capture("quests-selected-reward");
        entries[entries.FindIndex(q=>q.QuestId==65)]=new QuestEntry(65,2);views[65]=view with { State=QuestViewState.Completed };
        DetailCall(model,"OnQuestReceipt",new QuestReceipt(65,[new(900001000,1875),new(900000000,2000),new(330150025,1)]));
        CheckRewardRows(model,[(900001000,1875),(900000000,2000),(330150025,1)]);
        if(pending.ContainsKey(65) || ((Label)DetailField(model,"_questRewardTitle")!).Text!="Received rewards") throw new Exception("Granted rewards were confused with pending options");
        await Capture("quests-received-rewards");
        GD.Print("QUEST_GRANTED_REWARDS_OK");
    }
    private static IEnumerable<Node> Descendants(Node node) { yield return node;foreach(var child in node.GetChildren()) foreach(var item in Descendants(child)) yield return item; }
    private static void CheckRewardRows(World model,(int Item,int Count)[] expected)
    {
        var rewards=(Control)DetailField(model,"_questRewardBox")!;
        var actual=Descendants(rewards).OfType<Control>().Where(c=>c.HasMeta("quest_reward_item_id"))
            .Select(c=>(c.GetMeta("quest_reward_item_id").AsInt32(),c.GetMeta("quest_reward_count").AsInt32())).ToArray();
        if(!actual.SequenceEqual(expected)) throw new Exception("Quest Details displayed rewards from the wrong quest");
        foreach(var icon in Descendants(rewards).OfType<TextureRect>())
            if(icon.CustomMinimumSize.X<32 || icon.CustomMinimumSize.Y<32) throw new Exception("Quest reward icon minimum collapsed");
        GD.Print("QUEST_REWARD_ROWS_OK: "+string.Join(",",actual));
    }
    private async Task CaptureNpcFixtures(World model,ClassicDetailPanel shell,string prefix,string output,List<object> measurements)
    {
        var menu=(ScrollContainer)DetailField(model,"_npcMenuScroll")!;
        var list=(VBoxContainer)DetailField(model,"_npcMenuBox")!;
        async Task Capture(string id)
        {
            await ToSignal(GetTree().CreateTimer(.8),SceneTreeTimer.SignalName.Timeout);
            var body=((HudWindow)DetailField(model,"_npcPanel")!).Body;
            var rect=new Rect2(Vector2.Zero,shell.Size);
            var outside=Descendants(shell).OfType<Control>().Where(c=>c.IsVisibleInTree() && c.GetParent()==shell && !rect.Encloses(c.GetRect())).Select(c=>c.Name.ToString()).ToArray();
            if(outside.Length>0) throw new Exception(id+": shell overflow: "+string.Join(",",outside));
            CheckNpcOwnership(body);
            measurements.Add(new { id,shellWidth=shell.Size.X,shellHeight=shell.Size.Y,menuHeight=menu.Size.Y,menuMinimum=menu.CustomMinimumSize.Y,listHeight=list.Size.Y,overflow=outside,fixedMismatch=Array.Empty<string>(),buttons=list.GetChildren().OfType<Button>().Count() });
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-"+id+".png"));
        }
        if(OS.GetCmdlineUserArgs().Contains("npc-live-mail-audit") || OS.GetCmdlineUserArgs().Contains("npc-portrait-pilot-audit") || OS.GetCmdlineUserArgs().Contains("npc-portraits-all-audit") || OS.GetCmdlineUserArgs().Contains("npc-portrait-fit-audit") || OS.GetCmdlineUserArgs().Contains("npc-portrait-width-audit"))
        {
            DetailCall(model,"BeginNpcDialog","[Guard] Patrick","I'm Moradon's Guardian, no still just Sentinel Patrick.\nWhat did you want?");
            DetailCall(model,"AddNpcMenuButton","1.   Tell me more...",(Action)(()=>{}));
            DetailCall(model,"AddNpcMenuButton","2.   Close",(Action)(()=>{}));
            DetailCall(model,"EndNpcDialog",2);
            // Reproduce the live race: NPC chrome runs before the deferred generic observer.
            InvokeNpcStyle(list,"StyleContent");
            foreach(var button in list.GetChildren().OfType<Button>()) InvokeNpcStyle(button,"StyleTree");
            await Capture("npc-live-greeting");
            DetailCall(model,"BeginNpcDialog","[Guard] Patrick","What mission are you going to undertake?");
            for(int i=0;i<17;i++)
            {
                DetailCall(model,"AddNpcMenuButton",$"{i+1}.   {(i%3==0?"[In progress] ":i==4?"[Ready] ":"")}Mission {i+1}",(Action)(()=>{}));
                var added=list.GetChildren().OfType<Button>().Last();
                InvokeNpcStyle(added,"StyleContent");InvokeNpcStyle(added,"StyleTree");
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            DetailCall(model,"EndNpcDialog",17);
            await Capture("npc-live-menu");
            var live=list.GetChildren().OfType<Button>().First();
            live.EmitSignal(Control.SignalName.MouseEntered);await Capture("npc-live-hover");
            live.EmitSignal(Control.SignalName.MouseExited);live.Disabled=true;await Capture("npc-live-disabled");
            ItemData.EnsureLoaded();
            var itemTable=(Dictionary<int,ItemData.Item>)typeof(ItemData).GetField("_items",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            string[] helmets={"Half Plate Helmet","Rogue Helmet","Mage Linen Cap","Priest Helmet"};
            var offeredHelmets=helmets.Select(name=>itemTable.Values.First(i=>i.Name.Equals(name,StringComparison.OrdinalIgnoreCase) && i.Icon>0))
                .Select(item=>new QuestTransfer(false,0,item.Id+5,1,0)).ToArray();
            DetailCall(model,"ShowQuestView",new QuestView(65,1,21,true,true,false,false,QuestViewState.Available,0,
                "Subdual of Gavolt","Hunt Gavolt.","Wheat bread, Moradon's special local product, has killer taste. But since not too long ago, Gavolts raided the harvested mill and as a result, the food supply is running low and the number of citizens suffering from malnutrition rising. Can you wipe those monsters out and help the farmers of Moradon?",
                new QuestObjectives(65,false,[new QuestKillGroup(5,[1],"Gavolt")]),[0],[new QuestTransfer(false,2,0,6250,0)],[])
                { Options=offeredHelmets });
            var liveContent=(VBoxContainer)DetailField(model,"_npcQuestContent")!;
            foreach(var control in Descendants(liveContent).OfType<Control>().Concat(Descendants(list).OfType<Control>()).ToArray())
            { InvokeNpcStyle(control,"StyleContent");InvokeNpcStyle(control,"StyleTree"); }
            await Capture("npc-live-offer");
            var liveScroll=(ScrollContainer)DetailField(model,"_npcQuestScroll")!;
            liveScroll.ScrollVertical=(int)liveContent.Size.Y;
            await Capture("npc-live-offer-bottom");
            var rewardPanel=liveContent.GetChildren().OfType<PanelContainer>().Last();
            if(rewardPanel.GetGlobalRect().End.Y>liveScroll.GetGlobalRect().End.Y+.5f) throw new Exception("The last offered reward cannot be reached");
            GD.Print("NPC_LIVE_STYLE_ORDER_OK: deep late insertion, chrome before deferred skin, 32px actual bounds, original callbacks, hover and disabled states");
        }
        DetailCall(model,"BeginNpcDialog","[Guard] Patrick","What mission are you going to undertake?");
        int selected=-1;
        string[] topics={"[In progress] Orc Watcher hunting","Worm hunt","[In progress] Bandicoot hunting","[Ready] Kecoon hunting","[Completed] Bulcan hunting","Silk bundle delivery","Apples of Moradon","Close"};
        for(int i=0;i<topics.Length;i++) { int index=i;DetailCall(model,"AddNpcMenuButton",$"{i+1}.   {topics[i]}",(Action)(()=>selected=index)); }
        DetailCall(model,"EndNpcDialog",topics.Length);
        await Capture("npc-menu-full");
        foreach(var button in list.GetChildren().OfType<Button>().Where(b=>b.HasMeta("quest_status")))
            if(button.GetThemeColor("font_color")!=ClassicQuestStatus.Ink((QuestViewState)button.GetMeta("quest_status").AsInt32())) throw new Exception("NPC menu status colour is incorrect");
        var hover=list.GetChildren().OfType<Button>().First();
        hover.EmitSignal(Control.SignalName.MouseEntered);await Capture("npc-menu-hover");
        hover.ToggleMode=true;hover.SetPressedNoSignal(true);await Capture("npc-menu-pressed");
        hover.SetPressedNoSignal(false);hover.ToggleMode=false;hover.EmitSignal(Control.SignalName.MouseExited);
        if(menu.Size.Y+1<list.GetCombinedMinimumSize().Y || menu.GetVScrollBar().Visible) throw new Exception("The full NPC menu is still clipped");
        list.GetChildren().OfType<Button>().ElementAt(6).EmitSignal(BaseButton.SignalName.Pressed);
        if(selected!=6) throw new Exception("The NPC menu lost its original callback index");
        float menuHeight=shell.Size.Y;
        DetailCall(model,"BeginNpcDialog","[Guard] Patrick","Select a mission.");
        for(int i=0;i<24;i++) { int index=i;DetailCall(model,"AddNpcMenuButton",$"{i+1}.   Mission {i+1}",(Action)(()=>selected=index)); }
        DetailCall(model,"EndNpcDialog",24);
        await Capture("npc-menu-overflow");
        if(!menu.GetVScrollBar().Visible || shell.Size.Y>GetViewportRect().Size.Y-35) throw new Exception("Long NPC lists must scroll inside the screen");
        menu.ScrollVertical=(int)list.Size.Y;
        await Capture("npc-menu-overflow-bottom");
        var last=list.GetChildren().OfType<Button>().Last();
        if(last.GetGlobalRect().End.Y>menu.GetGlobalRect().End.Y+1) throw new Exception("The last NPC option cannot be reached");
        last.EmitSignal(BaseButton.SignalName.Pressed);
        if(selected!=23) throw new Exception("The last NPC callback was lost");
        DetailCall(model,"ShowQuestView",new QuestView(63,1,21,true,true,false,false,QuestViewState.Available,0,
            "Bandicoot Tooth","Collect two Apples of Moradon.","Oh? Nowadays, I am totally preoccupied with the manufacturing of medications learned from my teacher but it is so hard to find the materials for practice. Can you find two Apples of Moradon for me?",
            new QuestObjectives(63,false,[]),[],[new QuestTransfer(true,0,810418000,2,0),new QuestTransfer(false,2,0,850,0),new QuestTransfer(false,1,0,3500,0)],[]));
        // This isolated fixture does not load the full item table.
        var content=(VBoxContainer)DetailField(model,"_npcQuestContent")!;
        var item=Descendants(content).OfType<Label>().FirstOrDefault(l=>l.Text.Contains("810418000"));
        if(item!=null) item.Text="Apples of Moradon";
        await Capture("npc-apple-accept");
        if(shell.Size.Y>=470 || shell.Size.Y>=menuHeight) throw new Exception("Short quests retain empty fixed-height space");
        var actions=Descendants(list).OfType<Button>().Select(b=>b.Text).ToArray();
        if(!actions.Contains("Accept") || !actions.Contains("Reject")) throw new Exception("Quest acceptance actions were lost");
        Descendants(list).OfType<Button>().Single(b=>b.Text=="Reject").EmitSignal(BaseButton.SignalName.Pressed);
        if(((HudWindow)DetailField(model,"_npcPanel")!).Visible) throw new Exception("The native Reject callback no longer closes the quest");
        GD.Print("NPC_QUEST_FIT_OK: full menu, original callback indices, scrolling to last option, dynamic short quest, native Reject, reward selection");
    }
    private static void InvokeNpcStyle(Node node,string method)
    {
        var owner=method=="StyleTree"?typeof(CharacterDetailsSkin):typeof(ClassicNpcQuestChrome);
        owner.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{node});
    }
    private static void CheckNpcOwnership(Control body)
    {
        foreach(var scroll in Descendants(body).OfType<ScrollContainer>().Where(s=>s.IsVisibleInTree()))
        {
            if(scroll.GetHScrollBar().Visible) throw new Exception("NPC content needs horizontal scrolling");
            var content=scroll.GetChildren().OfType<Control>().FirstOrDefault();
            if(content!=null && scroll.GetVScrollBar().Visible && content.GetRect().End.X>scroll.GetVScrollBar().Position.X+.5f)
                throw new Exception("The native NPC scrollbar covers content: "+content.Size);
        }
        foreach(var button in Descendants(body).OfType<Button>().Where(b=>b.IsVisibleInTree()))
        {
            if(!button.HasMeta("classic_npc_plate") || button.CustomMinimumSize.Y!=32 || button.Size.Y!=32 || button.SizeFlagsVertical!=Control.SizeFlags.Fill)
                throw new Exception("Live NPC button bounds regressed: "+button.Text+" / "+button.Size+" / "+button.CustomMinimumSize);
            foreach(string state in new[]{"normal","hover","pressed","hover_pressed","disabled","focus"})
                if(button.GetThemeStylebox(state) is not StyleBoxEmpty) throw new Exception("Generic skin overwrote NPC "+state+" art: "+button.Text);
            if(button.GetChildren().OfType<ClassicNpcOptionPlate>().Count()!=1) throw new Exception("Duplicate NPC artwork");
            if(button.HasMeta("quest_status") && button.GetThemeColor("font_color")!=ClassicQuestStatus.Ink((QuestViewState)button.GetMeta("quest_status").AsInt32()))
                throw new Exception("Deferred generic skin overwrote NPC status ink");
        }
        foreach(var label in Descendants(body).OfType<Label>().Where(l=>l.HasMeta("quest_status")))
            if(label.GetThemeColor("font_color")!=ClassicQuestStatus.Ink((QuestViewState)label.GetMeta("quest_status").AsInt32())) throw new Exception("NPC status caption lost its ink");
    }
}
