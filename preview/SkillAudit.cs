using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Native;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureSkillAudit(PluginGame game,int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/skill-window-audit");
        if(OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR") is {Length:>0} auditDirectory) output=auditDirectory;
        System.IO.Directory.CreateDirectory(output);
        foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false)) throw new Exception("Missing skill audit pack: "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size=new Vector2I(440,650);
        AddChild(new ColorRect {Color=new Color("252822"),Size=new Vector2(440,650),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        Game.Session.Self.Sheet.SeedProgress(83,0,100);
        Native.Set(Game.Session.Self, "LastEnter",new MyInfo {Nation=nation,Class=nation*100+6,Race=nation==1?1:11,
            Name="Skill Preview",Gear=new int[8]}); Game.Session.Self.Inventory.Reset(new LibreKO.Domain.ItemSlot[InventoryConstants.InventoryTotal]);
        SkillData.EnsureLoaded();ItemData.EnsureLoaded();
        byte[] seed={4,0,0,0,0,60,20,23,10};
        Game.Session.Self.Mastery.Seed(seed);
        var world=PreviewFixtures.Prepared(new WorldRoot());
        var classField=new PreviewFixtures.ClientMember("Session.Self.Class");
        var zoneField=typeof(WorldRoot).GetProperty("ZoneId",BindingFlags.Instance|BindingFlags.NonPublic)!;
        classField.SetValue(world,nation*100+6);
        var bridge=Native.Call(world, "CreatePluginAdapters")!;
        var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;
        Native.Call(bridge, "AttachTo", game);
        var shell=new Control();AddChild(shell);
        bool closed=false;
        var host=(WindowHost)Create(typeof(WindowHost),"skills","Skills",shell,(Action)(()=>closed=true));
        var actual=new SkillWindow(host) {Position=new Vector2(36,34)};shell.AddChild(actual);
        var view=(LayoutView)typeof(SkillWindow).GetField("_view",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        var source=Plugin.Kit.Layout("{nation}_skilltree_us");
        var checks=new List<string>();var captures=new List<object>();var rules=new List<object>();
        string prefix=nation==1?"karus":"human";
        void Require(bool valid,string text) {if(!valid) throw new Exception("SKILL_AUDIT: "+text);checks.Add(text);}
        void Invoke(string method,params object[] args)=>typeof(SkillWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(actual,args);
        int Selected()=>(int)typeof(SkillWindow).GetField("_selected",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        List<SkillCell> Cells()=>(List<SkillCell>)typeof(SkillWindow).GetField("_cells",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        Label Label(string id)=>view.Get<Label>(id)!;
        string Original(string id)=>source.Find(id)?.Text.Trim()??"";
        void SetClass(int local) {classField.SetValue(world,nation*100+local);Invoke("Rebuild");}
        // Shows the page that holds a skill and returns its cell.
        SkillCell Show(int skillId)
        {
            foreach(var tab in game.Skills.Tabs)
            {
                var list=game.Skills.Skills(tab.Category);
                int index=list.ToList().FindIndex(s=>s.Id==skillId);
                if(index<0) continue;
                Invoke("SelectTab",tab.Category);
                typeof(SkillWindow).GetField("_page",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(actual,index/6);
                Invoke("RefreshPage");
                return Cells().First(c=>c.SkillId==skillId);
            }
            throw new Exception("SKILL_AUDIT: skill "+skillId+" is not listed for class "+game.Character.Class);
        }
        async Task Capture(string state)
        {
            Input.ParseInputEvent(new InputEventMouseMotion {Position=Vector2.Zero,GlobalPosition=Vector2.Zero});
            await Settled();
            Require(Label("string_info").GetThemeFont("font")==Plugin.Kit.Bold,state+": description uses Character Report bold font");
            Require(Label("string_skill_mp").GetThemeFont("font")==Plugin.Kit.Bold,state+": details use Character Report bold font");
            var records=view.Where(_=>true).Where(p=>p.Control.IsVisibleInTree() && p.Node.Id!="string_info")
                .Select(p=>new {id=p.Node.Id,x=p.Control.GlobalPosition.X-actual.GlobalPosition.X,y=p.Control.GlobalPosition.Y-actual.GlobalPosition.Y,
                    width=p.Control.Size.X,height=p.Control.Size.Y,declaredX=p.Node.X,declaredY=p.Node.Y,declaredWidth=p.Node.W,declaredHeight=p.Node.H,
                    text=p.Control is Label l?l.Text:"",minimum=p.Control.GetCombinedMinimumSize().ToString()}).ToList();
            foreach(var pair in view.Where(n=>n.IsString && n.Id!="string_info"))
            {
                if(!pair.Control.IsVisibleInTree()) continue;
                Require(pair.Control.Size==pair.Node.SizeVec,state+": exact text rectangle "+pair.Node.Id+" actual="+pair.Control.Size+" declared="+pair.Node.SizeVec+" minimum="+pair.Control.GetCombinedMinimumSize());
                Require(new Rect2(Vector2.Zero,actual.Size).Encloses(new Rect2(pair.Control.Position,pair.Control.Size)),state+": text inside window "+pair.Node.Id);
                if(pair.Control is Label label && !pair.Node.Id.StartsWith("string_list_"))
                {
                    Require(label.GetLineCount()<=1,state+": detail/point labels stay on one line "+pair.Node.Id);
                    var font=label.GetThemeFont("font");int size=label.GetThemeFontSize("font_size");
                    Require(label.Text.Length==0 || font.GetStringSize(label.Text,HorizontalAlignment.Left,-1,size).X<=label.Size.X+0.5f,
                        state+": text fits its rectangle without trimming "+pair.Node.Id+" '"+label.Text+"'");
                }
            }
            foreach(var cell in Cells()) Require(cell.Size==new Vector2(32,32),state+": native icon size "+cell.Index);
            foreach(var pair in view.Where(n=>n.IsButton).Where(p=>p.Control.IsVisibleInTree()))
            {
                Require(pair.Control.Size==pair.Node.SizeVec,state+": exact button rectangle "+pair.Node.Id);
                foreach(var caption in pair.Control.GetChildren().OfType<Label>())
                {
                    Require(caption.Position==Vector2.Zero && caption.Size==pair.Control.Size,state+": tab caption centered "+pair.Node.Id);
                    Require(caption.GetThemeFont("font").GetStringSize(caption.Text,HorizontalAlignment.Left,-1,caption.GetThemeFontSize("font_size")).X<=caption.Size.X-4,
                        state+": tab text fits original artwork "+pair.Node.Id);
                }
            }
            var scroll=actual.FindChild("skill_description_scroll",true,false) as ScrollContainer;
            Require(scroll!.Size==new Vector2(312,42),state+": fixed description viewport");
            Require(Label("text_page").Text=="Page" && int.TryParse(Label("string_page").Text,out _),state+": original pager shows the page number and the word Page");
            var tabs=game.Skills.Tabs;
            int category=(int)typeof(SkillWindow).GetField("_tab",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
            var info=Selected()>=0?game.Skills.Info(Selected()):default;
            if(Selected()>=0)
            {
                Require(Label("string_skill_mp").Text==NativeSkills.MpLine(info.Mp),state+": original MP wording");
                Require(Label("string_skill_point").Text==NativeSkills.RequirementLine(info),state+": correct requirement kind");
                Require(Label("string_skill_item0").Text==NativeSkills.BasicItemLine(Selected(),info.BasicItem),state+": weapon line follows the client's cast gate");
            }
            string name=prefix+"-"+state;
            using var image=GetViewport().GetTexture().GetImage();
            using var crop=image.GetRegion(new Rect2I((int)actual.Position.X,(int)actual.Position.Y,365,574));
            crop.SavePng(output+"/"+name+".png");
            System.IO.File.WriteAllText(output+"/"+name+"-bounds.json",JsonSerializer.Serialize(records,new JsonSerializerOptions {WriteIndented=true}));
            captures.Add(new {state,file=name+".png",classCode=game.Character.Class,category,selected=Selected(),tabs=tabs.Select(t=>new{t.Category,t.Label}),description=Label("string_info").Text});
        }
        string[] families=nation==1?new[]{"berserker","hunter","sorcerer","shaman"}:new[]{"blade","ranger","mage","cleric"};
        Require(Label("national_2").Text==(nation==1?"Linguistic":"Language"),"National rows keep the nation's original captions");
        foreach(int local in new[]{1,2,3,4,5,7,9,11,6,8,10,12,14,15})
        {
            SetClass(local);
            var tabs=game.Skills.Tabs;
            int category=tabs.Any(t=>t.Category==5)?5:SkillPage.Basic;
            Invoke("SelectTab",category);
            int family=CharacterClassCatalog.Family(game.Character.Class);
            int tier=CharacterClassCatalog.Tier(game.Character.Class);
            string[] masters=nation==1?new[]{"Berserker Hero","Shadow Bane","Elemental Lord","Shadow Knight"}:new[]{"Blade Master","kasar hood","Arc Mage","Paladin"};
            string expected=family>4?"class_fallback":tier==CharacterClassCatalog.TierBeginner?"img_public":tier==CharacterClassCatalog.TierMaster?"img_"+masters[family-1]:"img_"+families[family-1];
            Require(view.Get(expected)!.Visible,"Class "+game.Character.Class+": correct original title family/tier");
            // The client shows the three mastery trees from the start; a beginner cannot spend in them yet.
            for(int row=0;row<3;row++)
            {
                string caption=family<=4?Original($"img_{families[family-1]}_{row}"):SkillData.PageName(game.Character.Class,MasteryPoints.FirstTree+row);
                Require(Label("tree_name_"+row).Text==caption,"Class "+game.Character.Class+": tree "+row+" shows the original caption '"+caption+"'");
                Require(view.Get<BaseButton>("btn_"+(4+row))!.Visible,"Class "+game.Character.Class+": tree "+row+" allocation button shown");
                if(tier==CharacterClassCatalog.TierBeginner)
                    Require(view.Get<BaseButton>("btn_"+(4+row))!.Disabled,"Class "+game.Character.Class+": a beginner cannot spend in tree "+row);
            }
            foreach(var tab in tabs.Where(t=>MasteryPoints.IsTree(t.Category)))
            {
                string caption=tab.Category==MasteryPoints.MasterTree?Original("img_master")
                    :family<=4?Original($"img_{families[family-1]}_{tab.Category-MasteryPoints.FirstTree}"):tab.Label;
                string button=tab.Category==MasteryPoints.MasterTree?"btn_master":$"btn_{families[Math.Clamp(family-1,0,3)]}{tab.Category-MasteryPoints.FirstTree}";
                Require(view.Get(button)!.GetChildren().OfType<Label>().First().Text==caption,"Class "+game.Character.Class+": tab "+tab.Category+" shows the original caption '"+caption+"'");
            }
            // Every skill of the class, as if learned: the window applies the client's two placement rules.
            int refusedAdd=0,refusedDrag=0;
            var probe=new SkillCell(0);
            foreach(var tab in tabs)
                foreach(var listed in game.Skills.Skills(tab.Category))
                {
                    var def=SkillData.Get(listed.Id)!;
                    probe.Bind(listed with {Available=true},SkillLearnState.Learned);
                    Require(probe.CanAdd==HotbarAssign.CanAdd(def.Id,def.Type1),"Class "+game.Character.Class+": right-click rule for "+def.Id);
                    Require(probe.CanDrag==HotbarAssign.CanDrag(def.Type1,def.Type2),"Class "+game.Character.Class+": drag rule for "+def.Id);
                    if(!probe.CanAdd) refusedAdd++;
                    if(!probe.CanDrag) refusedDrag++;
                }
            probe.Free();
            rules.Add(new {classCode=game.Character.Class,refusedAdd,refusedDrag});
            await Capture("class-"+game.Character.Class);
        }
        SetClass(15);
        var awakening=Show(nation*100000+15802);
        Require(awakening.State==SkillLearnState.Learned && !awakening.CanAdd && !awakening.CanDrag,"Master 2 (level 62) Awakening is learned but refused for right-click and drag");
        Require(awakening._GetDragData(Vector2.Zero).VariantType==Variant.Type.Nil,"Awakening starts no drag");
        await Capture("master-awakening");
        // The owner's "62": one Master point at level 61 locks the two-point skill until level 62.
        Game.Session.Self.Sheet.SeedProgress(61,0,100);Game.Session.Self.Mastery.Seed(new byte[]{0,0,0,0,0,40,20,23,1});Invoke("Rebuild");
        awakening=Show(nation*100000+15802);
        Require(awakening.State==SkillLearnState.Locked && awakening.TooltipText.Contains("Reach level 62"),"Awakening at level 61 is locked until level 62");
        Require(awakening.GetNode<TextureRect>("icon").Texture==SkillIcons.Enigma(),"A locked skill shows the client's enigma icon");
        await Capture("master-locked-62");
        Game.Session.Self.Sheet.SeedProgress(83,0,100);Game.Session.Self.Mastery.Seed(seed);
        SetClass(6);
        var scream=Show(nation*100000+6802);
        Require(scream.State==SkillLearnState.Learned && scream.CanAdd && scream.CanDrag,"The active Master 2 skill Scream is placeable");
        var exceed=Show(nation*100000+6815);
        Require(exceed.State==SkillLearnState.Learnable && !exceed.CanAdd && !exceed.CanDrag,"Exceed Break at 10 of 15 Master points is learnable and not placeable");
        Require(exceed.GetNode<TextureRect>("icon").Texture==SkillIcons.Get(exceed.SkillId) && exceed.GetNode<TextureRect>("icon").SelfModulate.R<0.6f,
            "A learnable skill shows its own icon in the client's shade");
        Require(Label("string_list_"+exceed.Index).GetThemeColor("font_color")==UiTheme.Neutral,"A learnable skill name uses the client's learnable colour");
        Require(exceed.TooltipText.Contains("Spend 5 more points"),"A learnable skill says how many points are missing");
        Invoke("Select",exceed.SkillId);
        Require(Label("string_skill_point").GetThemeColor("font_color")==UiTheme.Neutral && Label("string_skill_point").TooltipText.Contains("Spend 5 more points"),
            "The requirement line of a learnable skill shows the state and the reason");
        await Capture("master-learnable");
        zoneField.SetValue(world,ChaosBattle.Zone);
        Require(!Show(nation*100000+6802).CanAdd && !Show(nation*100000+6802).CanDrag,"In the Chaos dungeon class skills are not placeable");
        zoneField.SetValue(world,0);
        SetClass(12);
        var special=game.Skills.Tabs.SelectMany(t=>game.Skills.Skills(t.Category)).First(s=>SkillData.Get(s.Id)!.Type1==MagicType.Special && s.Available);
        var specialCell=Show(special.Id);
        Require(specialCell.CanAdd && specialCell.CanDrag,"A learned Special-type skill ("+special.Id+") is placeable");
        // Original detail wording, including the weapon the cast gate asks for.
        foreach(var (local,needWeapon,text) in new[]{(3,SkillUsable.AnyWeapon,"All Weapons"),(8,SkillPage.DaggerWeapon,"Dagger, Jamadar"),(7,SkillPage.BowWeapon,"Bow")})
        {
            var def=SkillData.ForClass(nation*100+local).First(s=>s.NeedWeapon==needWeapon);
            Require(NativeSkills.BasicItemLine(def.Id,game.Skills.Info(def.Id).BasicItem)=="Basic item : Required Item : "+text,"Weapon line of "+def.Id+" reads '"+text+"'");
        }
        var noWeapon=SkillData.ForClass(nation*100+12).First(s=>s.NeedWeapon==SkillUsable.NeedsNoWeapon);
        Require(NativeSkills.BasicItemLine(noWeapon.Id,"")=="No basic item","A skill without a weapon reads 'No basic item'");
        Require(NativeSkills.MpLine(0)=="Doesn't use MP","Original MP wording without a cost");
        SetClass(6);Invoke("SelectTab",5);
        var attack=game.Skills.Skills(5);
        Require(attack.Count>6,"Attack contains multiple original pages");
        Cells()[0].OnHover!(Selected(),true);
        Invoke("TurnPage",1);await Capture("attack-page-2");
        Require(Selected()==attack[6].Id && Label("string_info").Text==game.Skills.Info(attack[6].Id).Description,"Paging clears stale hover and selection");
        Require(Label("string_page").Text=="2","The pager shows the page number");
        for(int i=0;i<8;i++) {Invoke("TurnPage",-1);Invoke("TurnPage",1);}
        Require(Selected()==attack[6].Id,"Rapid repeated paging keeps the current skill");
        Invoke("TurnPage",100);await Capture("attack-last-page");
        Require(view.Get<BaseButton>("btn_right")!.Disabled,"Last-page arrow is disabled");
        Invoke("SelectTab",8);await Capture("master");
        // Mastery points the client applies without a plugin notification (a mastery plan) reach an open window.
        Game.Session.Self.Mastery.Seed(new byte[]{4,0,0,0,0,61,20,23,10});
        await ToSignal(GetTree().CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
        Require(Label("string_4").Text=="61","An open window follows mastery points applied by the client");
        Game.Session.Self.Mastery.Seed(seed);
        await ToSignal(GetTree().CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
        // "+" spends on press and repeats while held, at the client's delays.
        var plus=view.Get<BaseButton>("btn_4")!;
        var repeat=(Godot.Timer)typeof(SkillWindow).GetField("_spendRepeat",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        Require(plus.ActionMode==BaseButton.ActionModeEnum.Press,"Allocation spends on press");
        plus.EmitSignal(BaseButton.SignalName.ButtonDown);
        Require(!repeat.IsStopped() && Mathf.IsEqualApprox(repeat.WaitTime,SkillLearn.RepeatDelaySeconds),"Holding allocation starts the client's repeat delay");
        plus.EmitSignal(BaseButton.SignalName.ButtonUp);
        Require(repeat.IsStopped(),"Releasing allocation stops the repeat");
        Game.Session.Self.Mastery.SetPool(0);Invoke("Rebuild");await Capture("no-skill-points");
        Require(view.Get<BaseButton>("btn_4")!.Disabled,"No mastery points disables allocation");
        // The client's Skill Preset entry sits in the free part of the Skill Point row and runs the client's Presets key.
        var presetButton=view.Get<Button>("btn_preset")!;
        var presetRect=new Rect2(presetButton.GlobalPosition-actual.GlobalPosition,presetButton.Size);
        var plusColumn=view.Get<Control>("btn_1")!;
        Require(presetButton.Size==new Vector2(92,18) && new Rect2(Vector2.Zero,actual.Size).Encloses(presetRect)
            && Mathf.IsEqualApprox(presetRect.End.X,plusColumn.GlobalPosition.X-actual.GlobalPosition.X+plusColumn.Size.X)
            && !presetRect.Intersects(new Rect2(Label("string_skillpoint").GlobalPosition-actual.GlobalPosition,Label("string_skillpoint").Size)),
            "Skill Preset keeps its declared rectangle inside the window, flush with the P column and clear of the point count");
        Require(presetButton.GetThemeFont("font").GetStringSize(presetButton.Text,HorizontalAlignment.Left,-1,presetButton.GetThemeFontSize("font_size")).X<=presetButton.Size.X-4,
            "Skill Preset caption fits its button");
        bool presets=false;
        ((Dictionary<KeyAction,Action>)Native.Get<object>(world, "Hotkeys.keyActions")!)[KeyAction.Presets]=()=>presets=true;
        presetButton.EmitSignal(BaseButton.SignalName.Pressed);
        Require(presets,"Skill Preset opens the client's skill presets");
        Game.Session.Self.Mastery.SetPool(4);Invoke("Rebuild");Invoke("SelectTab",0);
        int skillId=Selected();var skill=SkillData.Get(skillId)!;
        string originalDescription=skill.Desc;
        try
        {
            skill.Desc=string.Join(" ",Enumerable.Repeat(originalDescription.Length>0?originalDescription:"A long skill description.",15));
            Invoke("RefreshInfo");await Capture("long-description");
            var scroll=(ScrollContainer)actual.FindChild("skill_description_scroll",true,false)!;
            Require(Label("string_info").Size.Y>scroll.Size.Y && scroll.GetVScrollBar().Visible,"Long description scrolls within fixed viewport");
            scroll.ScrollVertical=100;await Capture("long-description-scrolled");
        }
        finally {skill.Desc=originalDescription;Invoke("RefreshInfo");}
        view.Get<BaseButton>("btn_close")!.EmitSignal(BaseButton.SignalName.Pressed);
        Require(closed,"Original close control calls the host");
        string pluginPath=ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/KnightOnlineUiClassic.dll");
        string clientPath=ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/LibreKO.dll");
        string Hash(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path)));
        System.IO.File.WriteAllText(output+"/"+prefix+".json",JsonSerializer.Serialize(new {nation,pluginHash=Hash(pluginPath),clientHash=Hash(clientPath),
            renderer=RenderingServer.GetCurrentRenderingMethod(),captures,rules,checks,data="Real LibreKO skill data and gameplay bridge; controlled level/mastery points; one labelled long-description stress fixture"},new JsonSerializerOptions{WriteIndented=true}));
        GD.Print("SKILL_AUDIT_OK "+prefix+" captures="+captures.Count+" checks="+checks.Count+" refused="+string.Join(",",rules.Select(r=>JsonSerializer.Serialize(r))));
        shell.Free();world.Free();net.Free();
        GC.Collect();GC.WaitForPendingFinalizers();
    }
}
