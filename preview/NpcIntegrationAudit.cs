using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Collections;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureNpcIntegration(int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/npc-design-integration-audit");System.IO.Directory.CreateDirectory(output);
        foreach(string path in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck","build/client/source-content/content/npcs.pck","build/client/source-content/content/weapons.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+path),false)) throw new Exception("Missing live audit pack: "+path);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(1000,700);
        AddChild(new ColorRect { Color=new Color("252822"),Size=new Vector2(1000,700),MouseFilter=MouseFilterEnum.Ignore });
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);var world=new World();
        DetailCall(world,"BuildNpcDialog");
        var layer=(CanvasLayer)DetailField(world,"_npcLayer")!;layer.Reparent(this);
        DetailCall(world,"BuildItemTooltip");
        var tooltipLayer=(CanvasLayer)DetailField(world,"_itemTipLayer")!;tooltipLayer.Reparent(this);
        var window=(HudWindow)DetailField(world,"_npcPanel")!;
        var actorType=typeof(World).GetNestedType("Ent",BindingFlags.NonPublic)!;
        var actor=Activator.CreateInstance(actorType,nonPublic:true)!;
        ((IDictionary)DetailField(world,"_ents")!).Add(701,actor);
        var native=(IGameWindows)Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{world},null)!;
        using var seed=JsonDocument.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://../../LibreKO/Server/LibreKO.Game/Seed/Data/Npcs.json")));
        var prototypes=seed.RootElement.EnumerateArray().GroupBy(p=>p.GetProperty("Id").GetInt32()).ToDictionary(g=>g.Key,g=>g.Last());
        void SetNpc(int id)
        {
            var data=prototypes[id];
            foreach(var field in new[]{"NpcId","ModelId","Level","NpcType","Name"})
            {
                object value=field=="Name"?data.GetProperty(field).GetString()!:field=="NpcId"?id:data.GetProperty(field).GetInt32();
                actorType.GetField(field)!.SetValue(actor,value);
            }
            actorType.GetField("IsNpc")!.SetValue(actor,true);actorType.GetField("Gear")!.SetValue(actor,new int[8]);
            typeof(World).GetField("_vendorNpcId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,701);
            typeof(World).GetField("_npcTalkId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,-1);
            var scene=(PackedScene?)typeof(World).GetMethod("ResolveMobScene",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(world,new object[]{data.GetProperty("ModelId").GetInt32()});
            scene?.Instantiate<Node3D>().Free();
        }
        _windowData!.NpcPortraitSource=()=>native.NpcPortrait;
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        var shell=(ClassicNpcDialogue)(CharacterDetailsSkin.Apply(window.Body)??window.GetChildren().OfType<ClassicNpcDialogue>().Single());
        async Task Frames(int count=8) { for(int i=0;i<count;i++) await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw); }
        async Task Ready()
        {
            await Frames();for(int i=0;i<100 && shell.Portrait.Visible && !shell.Portrait.IsReady;i++) await Frames(1);
            await Frames(2);
        }
        string prefix=nation==1?"karus":"human";
        var results=new List<object>();
        async Task Capture(string name)
        {
            await Frames(1);
            var rect=window.GetGlobalRect();var viewport=new Rect2(Vector2.Zero,GetViewportRect().Size);
            var failures=new List<string>();
            if(rect.Size.X!=363 || !viewport.Encloses(rect)) failures.Add("Window width or viewport containment: "+rect);
            if(shell.Speech.Size!=new Vector2(228,120) || shell.Speech.Text.Size!=new Vector2(212,shell.Speech.ViewportHeight)) failures.Add("Speech frame grew");
            if(shell.Status!=null)
            {
                var status=shell.Status.GetGlobalRect();var header=((Control)shell.Status.GetParent()).GetGlobalRect();
                if(status.Intersects(shell.Speech.GetGlobalRect()) || !header.Encloses(status) || Mathf.Abs(status.End.X-header.End.X)>.5f) failures.Add("Quest status is not outside speech and aligned to its section header");
            }
            if(shell.Modulate.A<1) failures.Add("Opening did not settle");
            var closeButton=Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_close");
            var closeArt=Plugin.Kit.Layout("co_questmenu_us").Find("btn_close")!.Images.First(n=>n.Tag==0);
            var titleRect=KnightOnlineUiClassic.Layout.ClassicNpcLayout.TitlePlate;
            if(closeButton.Size!=new Vector2(closeArt.W,closeArt.H) || Mathf.Abs(closeButton.Position.Y+closeButton.Size.Y/2-titleRect.GetCenter().Y)>.5f || closeButton.Position.X<titleRect.End.X+6) failures.Add("Close icon is scaled or misaligned with the title plate");
            foreach(var grid in Descendants(shell).OfType<ClassicNpcCardGrid>())
                foreach(var card in grid.GetChildren().OfType<ClassicNpcCard>())
                    if(card.Size.X>(grid.Size.X-6)/2+1 || !grid.GetGlobalRect().Grow(.5f).Encloses(card.GetGlobalRect())) failures.Add("Target or reward card escaped the two-column grid");
            foreach(var node in Descendants(shell).OfType<Control>().Where(c=>c.IsVisibleInTree()))
            {
                bool clipped=false;
                for(Node? parent=node.GetParent();parent!=null && parent!=shell;parent=parent.GetParent()) if(parent is Control c && c.ClipContents) clipped=true;
                if(!clipped && !rect.Grow(.5f).Encloses(node.GetGlobalRect())) failures.Add(node.Name+" escaped: "+node.GetGlobalRect());
            }
            if(failures.Count>0) throw new Exception(name+": "+string.Join("; ",failures));
            using var image=GetViewport().GetTexture().GetImage();
            using var crop=image.GetRegion(new Rect2I((int)rect.Position.X-10,(int)rect.Position.Y-10,(int)rect.Size.X+20,(int)rect.Size.Y+20));
            crop.SavePng(System.IO.Path.Combine(output,prefix+"-"+name+".png"));
            results.Add(new { name,frame=rect.ToString(),speech=shell.Speech.GetGlobalRect().ToString(),speechViewportHeight=shell.Speech.ViewportHeight,speechLineHeight=shell.Speech.LineHeight,
                questStatus=shell.Status?.Text,questStatusRect=shell.Status?.GetGlobalRect().ToString(),close=closeButton.GetGlobalRect().ToString(),speechContentHeight=shell.Speech.Text.GetContentHeight(),
                speechScroll=shell.Speech.Text.GetVScrollBar().Value,page=shell.Page,pageCount=shell.PageCount,
                lowerScroll=shell.QuestScroll.GetVScrollBar().MaxValue>shell.QuestScroll.GetVScrollBar().Page+.5,failures });
        }
        void Close() => DetailCall(world,"CloseNpcDialog");
        void Conversation(int npc,string story,string[] topics,Action<int>? pressed=null)
        {
            Close();SetNpc(npc);DetailCall(world,"BeginNpcDialog",prototypes[npc].GetProperty("Name").GetString()!,story);
            for(int i=0;i<topics.Length;i++) { int index=i;DetailCall(world,"AddNpcMenuButton",$"{i+1}.   {topics[i]}",(Action)(()=>pressed?.Invoke(index))); }
            DetailCall(world,"EndNpcDialog",topics.Length);
        }
        string[] guardTopics={"[In progress] Orc Watcher hunting","Patrick's trust","[In progress] Bandicoot hunt","[Ready] Kecoon hunting","Bulcan hunting","Wild bulcan hunting","Kekoon warrior hunt","Subdual of Gavolt","Kekoon Captain hunt","Subdual of Vulture","Giant bulcan hunting","Werewolf elimination","Subdual of Silan","Giant Gavolt hunting","Werewolf skin","Glyptodont hunt","Gloomwing hunt"};
        int menuChoice=-1;
        Conversation(13013,"What mission are you going to undertake? Help keep Moradon safe.",guardTopics,index=>menuChoice=index);
        await Ready();await Capture("guard-list");
        var originalFrame=window.GetGlobalRect();
        var next=Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_next_page");next.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();await Capture("guard-list-page-2");
        next=Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_next_page");next.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();await Capture("guard-list-page-3");
        if(window.GetGlobalRect()!=originalFrame || shell.Page!=2 || shell.PageCount!=3) throw new Exception("Menu paging moved the fixed page or lost missions");
        Descendants(shell).OfType<ClassicNpcAction>().Single(b=>b.Source?.Text.Contains("Gloomwing")==true).EmitSignal(BaseButton.SignalName.Pressed);
        if(menuChoice!=16) throw new Exception("Native menu callback changed");
        Conversation(31508,"How are you? I am the Enchanter dispatched to Moradon Castle. I offer buffs to help you on your journey. There are three types of buffs that you can receive: attack power, defensive power and enhancement of physical strength, and they can be stacked! Possible payment means are Noah, and, for those people between level 36 and 60, can pay with the [sign of victory] too.",
            ["[Buff]Noah/Symbol of Victory","Spirit Aisles' of Mounting","Settlement support free of charge buff"]);
        await Ready();await Capture("royal-dialogue");var royalFrame=window.GetGlobalRect();
        var target=shell.Speech.Text.GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseMotion { Position=target,GlobalPosition=target });await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.WheelDown,Pressed=true,Position=target,GlobalPosition=target });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.WheelDown,Pressed=false,Position=target,GlobalPosition=target });await Frames(2);
        if(shell.Speech.Text.GetVScrollBar().Value<=0 || royalFrame!=window.GetGlobalRect()) throw new Exception("Long conversation scroll input or fixed bounds failed");
        var speechBar=shell.Speech.Text.GetVScrollBar();speechBar.Value=speechBar.MaxValue-speechBar.Page;await Capture("royal-dialogue-end");
        Input.ParseInputEvent(new InputEventMouseMotion { Position=Vector2.Zero,GlobalPosition=Vector2.Zero });
        Conversation(18004,"",["Kaishan's trust","1st job change","Redistributions"]);await Ready();await Capture("kaishan-services");
        var headerClose=Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_close");
        var closePoint=headerClose.GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseMotion { Position=closePoint,GlobalPosition=closePoint });await Frames(2);await Capture("close-hover");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=true,Position=closePoint,GlobalPosition=closePoint });await Frames(2);await Capture("close-pressed");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=false,Position=closePoint,GlobalPosition=closePoint });await Frames(2);
        if(window.Visible || (bool)DetailField(world,"_npcDialogShown")!) throw new Exception("Header close did not invoke its native callback on actual pointer input");
        Input.ParseInputEvent(new InputEventMouseMotion { Position=Vector2.Zero,GlobalPosition=Vector2.Zero });
        ItemData.EnsureLoaded();
        var items=(Dictionary<int,ItemData.Item>)typeof(ItemData).GetField("_items",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        int[] helmets=new[]{"Half Plate Helmet","Rogue Helmet","Mage Linen Cap","Priest Helmet"}.Select(name=>items.Values.First(i=>i.Name.Equals(name,StringComparison.OrdinalIgnoreCase)&&i.Icon>0).Id+5).ToArray();
        var gavolt=new QuestView(501,13013,21,true,true,false,false,QuestViewState.Available,0,"Subdual of Gavolt","Hunt five Gavolts.",
            "Gavolts raided our mill and took the wheat. Hunt five Gavolts to help the farmers supply Moradon again.",
            new QuestObjectives(501,false,[new(5,[1],"Gavolt",true)]),[0],[new(false,2,0,6250,0)],[]) { Options=helmets.Select(id=>new QuestTransfer(false,0,id,1,0)).ToArray() };
        void Offer(int npc,QuestView view) { Close();SetNpc(npc);DetailCall(world,"ShowQuestView",view); }
        Offer(13013,gavolt);await Ready();await Capture("gavolt-offer");
        var itemCard=Descendants(shell).OfType<ClassicNpcCard>().First(c=>c.Source.GetChildren().OfType<Label>().Any(l=>l.Text.Contains("Half Plate Helmet")));
        var itemName=itemCard.GetChildren().OfType<Label>().First();
        if(itemName.GetThemeFont("font").GetInstanceId()!=shell.Speech.Text.GetThemeFont("normal_font").GetInstanceId()) throw new Exception("Item name and speech font families differ");
        void MovePointer(Vector2 point) => Input.ParseInputEvent(new InputEventMouseMotion { Position=point,GlobalPosition=point });
        MovePointer(itemCard.GetGlobalRect().Position+new Vector2(20,20));await Frames(3);
        if(!world.ItemTooltipVisible) throw new Exception("Item icon hover did not open the native tooltip");
        var tooltip=(Control)DetailField(world,"_itemTipPanel")!;
        var tooltipText=Descendants(tooltip).OfType<Label>().Select(l=>l.Text).ToArray();
        if(!tooltipText.Any(t=>t.Contains(ItemData.DisplayName(helmets[0])))) throw new Exception("Tooltip does not describe the actual reward item");
        MovePointer(itemCard.GetGlobalRect().Position+new Vector2(70,12));await Frames(3);
        if(!world.ItemTooltipVisible) throw new Exception("Item name hover lost the native tooltip");
        var tooltipViewport=GetViewportRect().Size;
        tooltip.Position=new Vector2(Math.Min(window.GetGlobalRect().End.X+12,tooltipViewport.X-tooltip.Size.X-8),Math.Min(itemCard.GetGlobalRect().Position.Y,tooltipViewport.Y-tooltip.Size.Y-8));
        await Frames(2);
        using(var hoverImage=GetViewport().GetTexture().GetImage())
        {
            var hoverRect=window.GetGlobalRect().Merge(tooltip.GetGlobalRect()).Grow(8);
            using var hoverCrop=hoverImage.GetRegion(new Rect2I((int)hoverRect.Position.X,(int)hoverRect.Position.Y,(int)hoverRect.Size.X,(int)hoverRect.Size.Y));
            hoverCrop.SavePng(System.IO.Path.Combine(output,prefix+"-item-tooltip.png"));
        }
        MovePointer(Vector2.Zero);await Frames(3);
        if(world.ItemTooltipVisible) throw new Exception("Item tooltip did not close after mouse exit");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,prefix+"-item-hover-verification.json"),JsonSerializer.Serialize(new { nation=prefix,itemNameUsesSpeechFont=true,nativeTooltipOnIconHover=true,nativeTooltipOnNameHover=true,nativeTooltipCloses=true,actualItemId=helmets[0],tooltipText },new JsonSerializerOptions { WriteIndented=true }));
        int accepts=0;Descendants(window.Body).OfType<Button>().Single(b=>b.Text=="Accept").Pressed+=()=>accepts++;
        Descendants(shell).OfType<ClassicNpcAction>().Single(b=>b.Source?.Text=="Accept").EmitSignal(BaseButton.SignalName.Pressed);
        if(accepts!=1 || window.Visible) throw new Exception("Accept no longer invokes the native action");
        Offer(13013,gavolt with { CanAccept=false,CanClaim=true,State=QuestViewState.Claimable,Counts=[5] });await Ready();await Capture("gavolt-ready-disabled");
        var confirm=Descendants(shell).OfType<ClassicNpcAction>().Single(b=>b.Source?.Text=="Confirm");
        if(!confirm.Disabled) throw new Exception("Confirm must require a reward choice");
        var choices=Descendants(shell).OfType<ClassicNpcCard>().Where(c=>c.Source.HasMeta("quest_reward_selected")).ToArray();
        choices[2].EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=true });
        await Frames();await Capture("gavolt-ready-selected");
        if((int)DetailField(world,"_questRewardChoice")! !=2 || confirm.Disabled || !choices[2].Source.GetMeta("quest_reward_selected").AsBool()) throw new Exception("Native reward choice or confirm state failed");
        var magpie=new QuestView(570,31506,21,true,true,false,false,QuestViewState.Available,0,"Hit and Miss Festival Begins","Hunt ten of each creature.",
            "Please hunt ten of each creature: Paramun, Doom Soldier, Troll Berserker and Giant Golem.",
            new QuestObjectives(570,false,[new(10,[1],"Paramun"),new(10,[2],"Doom Soldier"),new(10,[3],"Troll Berserker"),new(10,[4],"Giant Golem")]),[0,0,0,0],[new(false,2,0,20000000,0)],[]);
        Offer(31506,magpie);await Ready();await Capture("magpie-four-targets");
        int apple=items.Values.First(i=>i.Name.Contains("Apples of Moradon",StringComparison.OrdinalIgnoreCase)&&i.Icon>0).Id;
        Offer(13013,gavolt with { QuestId=571,Title="Apples of Moradon",Dialogue="I need materials for practice. Can you find two Apples of Moradon for me?",Objectives=new QuestObjectives(571,false,[]),Counts=[],Transfers=[new(true,0,apple,2,0),new(false,2,0,850,0),new(false,1,0,3500,0)],Options=[] });
        await Ready();await Capture("apple-collect");
        Offer(13013,gavolt with { QuestId=572,Title="Supply delivery",Dialogue="Bring these supplies to Moradon.",Objectives=new QuestObjectives(572,false,[]),Counts=[],Transfers=[new(true,0,apple,2,0),new(true,0,helmets[0],1,0),new(true,0,helmets[1],1,0),new(false,2,0,850,0)],Options=[] });
        await Ready();await Capture("multiple-collect");
        Offer(13013,gavolt with { CanAccept=false,State=QuestViewState.InProgress,Counts=[2] });await Ready();await Capture("gavolt-in-progress");
        Offer(13013,gavolt with { CanAccept=false,CanClaim=true,State=QuestViewState.Claimable,Counts=[5],Options=Enumerable.Range(0,28).Select(i=>new QuestTransfer(false,0,helmets[i%4],i+1,0)).ToArray() });
        await Ready();await Capture("many-rewards-top");
        shell.QuestScroll.ScrollVertical=int.MaxValue;await Frames();await Capture("many-rewards-bottom");
        if(shell.QuestScroll.GetVScrollBar().Value<=0) throw new Exception("Long lower content is inaccessible");
        GetWindow().Size=new Vector2I(1000,540);
        Conversation(13013,"What mission are you going to undertake?",guardTopics,index=>menuChoice=index);await Ready();await Capture("guard-small-viewport");
        var close=Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_footer_close");close.EmitSignal(BaseButton.SignalName.Pressed);
        if(window.Visible || (bool)DetailField(world,"_npcDialogShown")!) throw new Exception("Native Close fallback failed");
        if(NpcPortraitCache.ActiveRenderers!=0 || NpcPortraitCache.RetainedViewportCount!=0) throw new Exception("Portrait retains rendering resources");
        string binary=ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/KnightOnlineUiClassic.dll");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,prefix+"-verification.json"),JsonSerializer.Serialize(new { nation=prefix,productionComponents=true,
            pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(binary))),nativeMenuCallback=true,nativeAcceptCallback=true,nativeRewardChoice=true,
            nativeCloseCallback=true,nativeHeaderClosePointer=true,speechWheel=true,lowerOverflow=true,twoColumnSections=true,portraitRenders=NpcPortraitCache.RenderRequests,retainedPortraitViewports=NpcPortraitCache.RetainedViewportCount,screens=results },new JsonSerializerOptions {WriteIndented=true}));
        _windowData.NpcPortraitSource=null;NpcPortraitCache.Clear();tooltipLayer.Free();layer.Free();world.Free();net.Free();
        GD.Print("NPC_INTEGRATION_OK: "+prefix+", live controls, complete paged menu, accept/choice/close callbacks, fixed speech scroll, two-column targets/rewards, small viewport, cached portraits");
    }
}
