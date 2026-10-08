using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureSkillAudit(PluginGame game,int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/skill-window-audit");
        System.IO.Directory.CreateDirectory(output);
        foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false)) throw new Exception("Missing skill audit pack: "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size=new Vector2I(440,650);
        AddChild(new ColorRect {Color=new Color("252822"),Size=new Vector2(440,650),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        net.Sheet.SeedProgress(83,0,100);
        typeof(Net).GetProperty("LastEnter")!.SetValue(net,new MyInfo {Nation=nation,Class=nation*100+6,Race=nation==1?1:11,
            Name="Skill Preview",Gear=new int[8],Inventory=new LibreKO.Domain.ItemSlot[InventoryConstants.InventoryTotal]});
        SkillData.EnsureLoaded();ItemData.EnsureLoaded();
        net.Mastery.Seed(new byte[]{4,0,0,0,0,60,20,23,10});
        var world=new World();
        var classField=typeof(World).GetField("_selfClass",BindingFlags.Instance|BindingFlags.NonPublic)!;
        classField.SetValue(world,nation*100+6);
        var bridge=Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{world},null)!;
        var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;
        attach.Invoke(game,Enumerable.Repeat(bridge,attach.GetParameters().Length).ToArray());
        var shell=new Control();AddChild(shell);
        bool closed=false;
        var host=(WindowHost)Create(typeof(WindowHost),"skills","Skills",shell,(Action)(()=>closed=true));
        var actual=new SkillWindow(host) {Position=new Vector2(36,34)};shell.AddChild(actual);
        var view=(LayoutView)typeof(SkillWindow).GetField("_view",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        var checks=new List<string>();var captures=new List<object>();
        string prefix=nation==1?"karus":"human";
        void Require(bool valid,string text) {if(!valid) throw new Exception("SKILL_AUDIT: "+text);checks.Add(text);}
        void Invoke(string method,params object[] args)=>typeof(SkillWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(actual,args);
        int Selected()=>(int)typeof(SkillWindow).GetField("_selected",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        List<SkillCell> Cells()=>(List<SkillCell>)typeof(SkillWindow).GetField("_cells",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        Label Label(string id)=>view.Get<Label>(id)!;
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
                    Require(label.GetLineCount()<=1,state+": detail/point labels stay on one line "+pair.Node.Id);
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
            var tabs=game.Skills.Tabs;
            int category=(int)typeof(SkillWindow).GetField("_tab",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
            var info=Selected()>=0?game.Skills.Info(Selected()):default;
            if(Selected()>=0)
            {
                Require(Label("string_skill_mp").Text.Contains("MP") && !Label("string_skill_mp").Text.Contains("Level"),state+": MP has its own line");
                Require(Label("string_skill_point").Text==(info.UsesPoints?$"Required Skill Point : {info.RequiredPoints}":$"Required Level : {info.RequiredLevel}"),state+": correct requirement kind");
                var definition=SkillData.Get(Selected())!;
                string weapon=NativeSkills.EquippedWeaponRequirementName(definition.ItemGroup);
                Require(Label("string_skill_item0").Text==(weapon.Length>0?$"Required weapon : {weapon}":"No weapon required"),state+": weapon requirement follows the casting equipment group");
            }
            string name=prefix+"-"+state;
            using var image=GetViewport().GetTexture().GetImage();
            using var crop=image.GetRegion(new Rect2I((int)actual.Position.X,(int)actual.Position.Y,365,574));
            crop.SavePng(output+"/"+name+".png");
            System.IO.File.WriteAllText(output+"/"+name+"-bounds.json",JsonSerializer.Serialize(records,new JsonSerializerOptions {WriteIndented=true}));
            captures.Add(new {state,file=name+".png",classCode=game.Character.Class,category,selected=Selected(),tabs=tabs.Select(t=>new{t.Category,t.Label}),description=Label("string_info").Text});
        }
        foreach(int local in new[]{1,2,3,4,5,7,9,11,6,8,10,12,14,15})
        {
            classField.SetValue(world,nation*100+local);Invoke("Rebuild");
            var tabs=game.Skills.Tabs;
            int category=tabs.Any(t=>t.Category==5)?5:SkillPage.Basic;
            Invoke("SelectTab",category);
            int family=CharacterClassCatalog.Family(game.Character.Class);
            int tier=CharacterClassCatalog.Tier(game.Character.Class);
            string[] families=nation==1?new[]{"berserker","hunter","sorcerer","shaman"}:new[]{"blade","ranger","mage","cleric"};
            string[] masters=nation==1?new[]{"Berserker Hero","Shadow Bane","Elemental Lord","Shadow Knight"}:new[]{"Blade Master","kasar hood","Arc Mage","Paladin"};
            string expected=family>4?"class_fallback":tier==CharacterClassCatalog.TierBeginner?"img_public":tier==CharacterClassCatalog.TierMaster?"img_"+masters[family-1]:"img_"+families[family-1];
            Require(view.Get(expected)!.Visible,"Class "+game.Character.Class+": correct original title family/tier");
            Require(Label("tree_name_0").Text==(tier==CharacterClassCatalog.TierBeginner?"":SkillData.PageName(game.Character.Class,5)),"Class "+game.Character.Class+": tree caption follows native data");
            await Capture("class-"+game.Character.Class);
        }
        classField.SetValue(world,nation*100+8);Invoke("Rebuild");Invoke("SelectTab",6);
        await Capture("rogue-assassin");
        Invoke("SelectTab",7);await Capture("rogue-search");
        classField.SetValue(world,nation*100+10);Invoke("Rebuild");Invoke("SelectTab",7);await Capture("mage-lightning");
        classField.SetValue(world,nation*100+12);Invoke("Rebuild");Invoke("SelectTab",6);await Capture("priest-aura");
        Invoke("SelectTab",7);await Capture("priest-spirit");
        classField.SetValue(world,nation*100+6);Invoke("Rebuild");Invoke("SelectTab",5);
        var attack=game.Skills.Skills(5);
        Require(attack.Count>6,"Attack contains multiple original pages");
        Cells()[0].OnHover!(Selected(),true);
        Invoke("TurnPage",1);await Capture("attack-page-2");
        Require(Selected()==attack[6].Id && Label("string_info").Text==game.Skills.Info(attack[6].Id).Description,"Paging clears stale hover and selection");
        for(int i=0;i<8;i++) {Invoke("TurnPage",-1);Invoke("TurnPage",1);}
        Require(Selected()==attack[6].Id,"Rapid repeated paging keeps the current skill");
        Invoke("TurnPage",100);await Capture("attack-last-page");
        Require(view.Get<BaseButton>("btn_right")!.Disabled,"Last-page arrow is disabled");
        Invoke("SelectTab",8);await Capture("master");
        net.Mastery.SetPool(0);Invoke("Rebuild");await Capture("no-skill-points");
        Require(view.Get<BaseButton>("btn_4")!.Disabled,"No mastery points disables allocation");
        net.Mastery.SetPool(4);Invoke("Rebuild");Invoke("SelectTab",0);
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
            renderer=RenderingServer.GetCurrentRenderingMethod(),captures,checks,data="Real LibreKO skill data and gameplay bridge; controlled level/mastery points; one labelled long-description stress fixture"},new JsonSerializerOptions{WriteIndented=true}));
        GD.Print("SKILL_AUDIT_OK "+prefix+" captures="+captures.Count+" checks="+checks.Count);
        shell.Free();world.Free();net.Free();
        GC.Collect();GC.WaitForPendingFinalizers();
    }
}
