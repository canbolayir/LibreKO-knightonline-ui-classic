using Godot;
using System.Reflection;
using LibreKO;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;

public partial class Preview : Control
{
    private PreviewData? _windowData,_chatData;
    static object Create(Type type, params object?[] args) => Activator.CreateInstance(type, BindingFlags.Instance|BindingFlags.NonPublic, null, args, null)!;
    public override async void _Ready()
    {
        try
        {
            string auditPack=OS.GetEnvironment("LIBREKO_AUDIT_CLIENT_PACK");
            if(auditPack.Length>0 && !ProjectSettings.LoadResourcePack(auditPack,false))throw new Exception("Missing audit resource pack: "+auditPack);
            HudLayout.PersistLayouts=false;
            PluginSettings.Enabled=false;
            string root=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../"));
            var manifest=PluginManifest.Parse(System.IO.File.ReadAllText(root+"/plugin.json"));
            var info=(PluginInfo)Create(typeof(PluginInfo),root,manifest,PluginState.Loaded,"");
            var game=new PluginGame();
            var context=(PluginContext)Create(typeof(PluginContext),info,new List<PluginInfo>{info},new PluginUi(),game);
            new Plugin().Initialize(context);
            int nation=OS.GetCmdlineUserArgs().Contains("karus") ? 1 : 2;
            var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var proxies=attach.GetParameters().Select(p=>
            {
                var proxy=DispatchProxy.Create(p.ParameterType,typeof(PreviewData));
                ((PreviewData)proxy).Nation=nation;
                return proxy;
            }).ToArray();
            _windowData=(PreviewData)proxies[Array.FindIndex(attach.GetParameters(),p=>p.ParameterType==typeof(IGameWindows))];
            _chatData=(PreviewData)proxies[Array.FindIndex(attach.GetParameters(),p=>p.ParameterType==typeof(IGameChat))];
            bool characterAudit = OS.GetCmdlineUserArgs().Contains("character-audit");
            if (characterAudit || OS.GetCmdlineUserArgs().Contains("details-audit") || OS.GetCmdlineUserArgs().Contains("hud-audit"))
                foreach (var proxy in proxies) ((PreviewData)proxy).CharacterAudit = new CharacterAuditData(nation) { ClanFlag=OS.GetCmdlineUserArgs().Contains("details-audit")?LibreKO.Network.ClanTypes.Accredited5:LibreKO.Network.ClanTypes.Training };
            attach.Invoke(game,proxies);
            if(OS.GetCmdlineUserArgs().Contains("upstream-integration-audit")){await CaptureUpstreamIntegration(game,nation);GetTree().Quit();return;}
            if(OS.GetCmdlineUserArgs().Contains("merchant-palette")){await CaptureMerchantPalette(nation);GetTree().Quit();return;}
            if(OS.GetCmdlineUserArgs().Contains("merchant-audit")){await CaptureMerchantAudit(game,nation);GetTree().Quit();return;}
            if(OS.GetCmdlineUserArgs().Contains("trade-audit")) { await CaptureExchangeAudit(game,nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("party-audit")) { await CapturePartyAudit(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("inventory-audit")) { await CaptureInventoryAudit(game,nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("skill-audit")) { await CaptureSkillAudit(game,nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("vendor-audit")) { await CaptureVendorAudit(game,nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("npc-integration-audit")) { await CaptureNpcIntegration(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("hunt-icon-options")) { await CaptureHuntIconOptions(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("npc-design-audit")) { await CaptureNpcDesigns(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("npc-portrait-audit")) { await CaptureNpcPortraitAudit(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("hud-audit")) { await CaptureHudAudit(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("details-audit")) { await CaptureDetails(nation);GetTree().Quit();return; }
            if (characterAudit) { await CaptureCharacterAudit(game, nation); GetTree().Quit(); return; }
            bool detail=OS.GetCmdlineUserArgs().Contains("detail");
            if(!detail) AddChild(new StatusHud());
            var hotbar=new HotkeyBar();
            AddChild(hotbar);
            if(OS.GetCmdlineUserArgs().Contains("horizontal")) {
                typeof(HotkeyBar).GetField("_horizontal",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(hotbar,true);
                typeof(HotkeyBar).GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(hotbar,null);
            }
            if(OS.GetCmdlineUserArgs().Contains("hotbar-check")) {
                int hotbarIndex=Array.FindIndex(attach.GetParameters(),p=>p.ParameterType==typeof(IGameHotbar));
                CheckHotbars(hotbar,game,(PreviewData)proxies[hotbarIndex]);
            }
            if(detail) {
                hotbar.Scale=new Vector2(2,2);hotbar.Position=new Vector2(60,50);
                if(OS.GetCmdlineUserArgs().Contains("native-reference")) {
                    var source=Plugin.Kit.Layout("{nation}_hotkey_us").Images.First();
                    AddChild(new TextureRect {
                        Position=new Vector2(1100,50),Size=new Vector2(source.W,source.H),Scale=new Vector2(2,2),
                        Texture=new AtlasTexture { Atlas=Plugin.Kit.Texture(source.Texture!),
                            Region=new Rect2(source.SrcX,source.SrcY,source.SrcW,source.SrcH),FilterClip=true },
                        ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.Scale
                    });
                }
            } else {
            AddChild(new ChatWindow());
            AddChild(new Taskbar());
            AddWindow("inventory",new Vector2(260,130),h=>new InventoryWindow(h));
            AddWindow("skills",new Vector2(940,130),h=>new SkillWindow(h));
            AddWindow("character_info",new Vector2(1360,130),h=>new CharacterWindow(h));
            foreach(var window in GetChildren().OfType<InventoryWindow>())
                typeof(InventoryWindow).GetMethod("SetCospreOpen",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,new object[]{true});
            }
            await ToSignal(GetTree().CreateTimer(3),SceneTreeTimer.SignalName.Timeout);
            if(detail) hotbar.Position=new Vector2(60,50);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var image=GetViewport().GetTexture().GetImage();
            image.SavePng(root+"/preview/classic-"+(nation==1 ? "karus" : "elmo")+(OS.GetCmdlineUserArgs().Contains("horizontal") ? "-horizontal" : "")+(OS.GetCmdlineUserArgs().Contains("hotbar-check") ? "-multi" : "")+(detail ? "-detail":"")+(OS.GetCmdlineUserArgs().Contains("native-reference") ? "-native":"")+".png");
            GD.Print("CLASSIC_PREVIEW_OK");
            GetTree().Quit();
        }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private static void CheckHotbars(HotkeyBar hotbar,PluginGame game,PreviewData data)
    {
        void Require(bool valid,string message) { if(!valid) throw new Exception("HOTBAR_CHECK: "+message); }
        Control Canvas()=>hotbar.GetNode<Control>("canvas");
        Control Bar(string id)=>Canvas().GetNode<Control>(id);
        IEnumerable<Control> Bars()=>Canvas().GetChildren().OfType<Control>();
        Transform2D InGroup(Control control)=>hotbar.GetGlobalTransform().AffineInverse()*control.GetGlobalTransform();
        bool Near(Vector2 a,Vector2 b)=>(a-b).Length()<0.01f;
        bool SameTransform(Transform2D a,Transform2D b)=>Near(a.Origin,b.Origin) && Near(a.X,b.X) && Near(a.Y,b.Y);
        Control? ContentOf(Control control)=>control.Name=="content" ? control
            : control.GetParent() is Control parent && parent.Name=="content" ? parent:null;
        void Press(string bar,string button) { Bar(bar).GetNode<BaseButton>(button).EmitSignal(BaseButton.SignalName.Pressed); }
        void Raise()=>typeof(PluginGame).GetMethod("RaiseHotbar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(game,null);
        string Page(string id)=>Bar(id).GetNode<Label>("page_label").Text;
        Require(Page("main_bar")=="2","Main bar must start on real F2");
        var initialPage=Bar("main_bar").GetNode<Label>("page_label");
        Require(new Rect2(Vector2.Zero,Bar("main_bar").Size).Encloses(new Rect2(initialPage.Position,initialPage.Size)),
            "The initial single bar's page number must fit its footer");
        Press("main_bar","add_bar"); Press("main_bar","add_bar");
        Require(Page("extra_bar_0")=="3" && Page("extra_bar_1")=="4","New bars must select consecutive pages");
        Press("extra_bar_0","btn_down");
        Require(Page("extra_bar_0")=="4" && Page("main_bar")=="2","Extra page arrows must not change main page");
        game.Hotbar.SetPage(0);Raise();
        Require(Page("main_bar")=="1" && Page("extra_bar_0")=="4","F keys must leave extra pages pinned");
        var slot=Bar("extra_bar_0").GetNode<HotSlot>("slot_0");
        slot._GuiInput(new InputEventMouseButton { Pressed=true,ButtonIndex=MouseButton.Left });
        Require(data.LastActivate==30,"Extra F4 slot must activate absolute slot 30");
        slot._DropData(Vector2.Zero,new Godot.Collections.Dictionary { {"id",123},{"barAbs",15} });
        Require(data.LastDrop==(30,123,15),"Cross-bar drag must keep source and target absolute slots");
        Press("extra_bar_0","remove_bar");
        Require(Page("extra_bar_0")=="4" && !Canvas().HasNode("extra_bar_1"),"Removing a middle bar must preserve the next bar's page");
        Require(data.ClearCount==0,"Closing a bar must never clear stored skills");
        game.Hotbar.SetPage(1);Raise();
        for(int i=0;i<12;i++) {
            if(Bar("main_bar").HasNode("add_bar")) Press("main_bar","add_bar");
        }
        Require(Bars().Count()==8,"Maximum must be eight bars");
        Require(!Bar("main_bar").HasNode("add_bar"),"Add button must hide at the limit");
        bool horizontal=OS.GetCmdlineUserArgs().Contains("horizontal");
        data.WithCooldown=true;Raise();
        var beforeSize=hotbar.Size;
        var snapshot=Canvas().FindChildren("*","Control",true,false).Cast<Control>().ToDictionary(
            c=>Canvas().GetPathTo(c).ToString(),c=>(Size:c.Size,Transform:InGroup(c),Text:c is Label,
                Content:ContentOf(c) is Control content ? Canvas().GetPathTo(content).ToString():null));
        Press("main_bar","rotate");
        Require(Near(hotbar.Size,new Vector2(beforeSize.Y,beforeSize.X)),"Rotation must swap the group's width and height exactly");
        Vector2 Turn(Vector2 point)=>horizontal ? new Vector2(beforeSize.Y-point.Y,point.X) : new Vector2(point.Y,beforeSize.X-point.X);
        foreach(var (path,old) in snapshot) {
            var control=Canvas().GetNode<Control>(path);
            var transform=InGroup(control);
            Require(Near(control.Size,old.Size),$"Rotation must preserve the size of {path}");
            if(old.Content is string contentPath && contentPath!=path) {
                var oldLocal=snapshot[contentPath].Transform.AffineInverse()*old.Transform;
                var newLocal=InGroup(Canvas().GetNode<Control>(contentPath)).AffineInverse()*transform;
                Require(SameTransform(oldLocal,newLocal),$"Icons, counts and cooldown must keep their internal alignment: {path}");
            } else Require(Near(transform*(control.Size/2),Turn(old.Transform*(old.Size/2))),$"Rotation must preserve the rotated center of {path}");
            if(old.Text || old.Content!=null) Require(Mathf.Abs(control.GetGlobalTransform().Rotation)<0.001f,$"Text and slot content must remain upright: {path}");
            else foreach(var corner in new[]{Vector2.Zero,new Vector2(old.Size.X,0),old.Size,new Vector2(0,old.Size.Y)})
                Require(Near(transform*corner,Turn(old.Transform*corner)),$"The entire frame/control must rotate rigidly: {path}");
        }
        var delta=InGroup(Bar("main_bar").GetNode<HotSlot>("slot_1")).Origin-InGroup(Bar("main_bar").GetNode<HotSlot>("slot_0")).Origin;
        Require(horizontal ? Near(delta,new Vector2(0,36)) : Near(delta,new Vector2(36,0)),"Rotate must change every bar's slot direction");
        foreach(var bar in Bars())
            foreach(var cell in bar.GetChildren().OfType<HotSlot>())
            {
                Require(Mathf.Abs(cell.GetNode<TextureRect>("content/icon").GetGlobalTransform().Rotation)<0.001f,
                    "Skill pictures must remain upright in both bar orientations");
                var shade=cell.GetNode<ColorRect>("content/cooldown");
                Require(shade.Visible && Near(shade.Size,new Vector2(32,16)) && Near(shade.Position,new Vector2(0,16)),
                    "Half cooldown must shade the lower half of the upright icon");
                Require(cell.GetNode<Label>("content/count").Text=="42","Item counts must remain visible in the upright content layer");
            }
        Press("main_bar","rotate");
        foreach(var (path,old) in snapshot) {
            var control=Canvas().GetNode<Control>(path);
            var transform=InGroup(control);
            Require(Near(transform.Origin,old.Transform.Origin) && Near(transform.X,old.Transform.X) && Near(transform.Y,old.Transform.Y),$"Rotating back must restore the exact layout: {path}");
        }
        foreach(Control bar in Bars())
            foreach(BaseButton button in bar.GetChildren().OfType<BaseButton>())
                Require(new Rect2(Vector2.Zero,bar.Size).Encloses(new Rect2(button.Position,button.Size)),"Controls must stay inside the bar");
        foreach(var bar in Bars()) {
            var page=bar.GetNode<Label>("page_label");
            var action=bar.GetNode<BaseButton>(bar.Name=="main_bar" ? "rotate":"remove_bar");
            Require(Mathf.Abs(page.Position.Y+page.Size.Y/2-action.Position.Y-action.Size.Y/2)<2 && page.Position.X>=action.Position.X+action.Size.X,
                $"The page number must sit beside the bottom controls: {bar.Name}, page={page.GetRect()}, control={action.GetRect()}");
            Require(new Rect2(Vector2.Zero,bar.Size).Encloses(new Rect2(page.Position,page.Size)),
                $"Page numbers must fit inside the footer: {bar.Name}, page={page.GetRect()}, bar={bar.Size}");
            Require(!new Rect2(page.Position,page.Size).Intersects(bar.GetNode<Control>("page_drag").GetRect()),
                "The top drag ornament must not contain the page number");
        }
        var keys=hotbar.FindChildren("key_*","Label",true,false).Cast<Label>().ToArray();
        Require(keys.Length==10,"The group must show only one 1–0 number rail");
        Require(keys.Select(k=>k.GetThemeFont("font").GetInstanceId()).Distinct().Count()==1 &&
            keys.Select(k=>k.GetThemeFontSize("font_size")).Distinct().Count()==1 &&
            keys.Select(k=>k.GetThemeColor("font_color")).Distinct().Count()==1,"All ten numerals must use the same font, size and color");
        var bars=Bars().OrderBy(b=>b.Position.X).ToList();
        for(int i=1;i<bars.Count;i++) Require(bars[i-1].Position.X+bars[i-1].Size.X==bars[i].Position.X,"Adjacent bars must touch without a gap");
        Require(Bar("main_bar").GetNode<BaseButton>("rotate").Position==new Vector2(2,Bar("main_bar").Size.Y-19),"Rotate must stay at the same dock within the rotated group");
        data.WithCooldown=false;Raise();
        while(Canvas().HasNode("extra_bar_0")) Press("extra_bar_0","remove_bar");
        Press("main_bar","add_bar");Press("main_bar","add_bar");
        GD.Print("CLASSIC_HOTBAR_CHECK_OK: add/remove, 8-bar limit, pinned pages, activation, cross-bar drag, rigid frame quarter-turn, upright icons/text/counts, vertical cooldown fill, exact layout restoration, control bounds, footer page numbers, shared font/number rail, flush edges, unchanged local dock");
    }
    private void AddWindow(string id,Vector2 at,Func<WindowHost,Control> build)
    {
        var shell=new Control();
        AddChild(shell);
        var host=(WindowHost)Create(typeof(WindowHost),id,id,shell,(Action)(()=>{}));
        var window=build(host); window.Position=at; AddChild(window);
    }
}

public class PreviewData : DispatchProxy
{
    public int Nation=2;
    public CharacterAuditData? CharacterAudit;
    public IGameCharacterPanel? LiveCharacterPanel;
    private int _page;
    public int LastActivate=-1,ClearCount;
    public bool WithCooldown;
    public bool WithUnreadMail;
    public Func<int>? MailUnreadCount;
    public Func<GameNpcPortrait?>? NpcPortraitSource;
    public Func<int,bool,string,IReadOnlyList<string>>? ChatReader;
    public IReadOnlyList<LibreKO.Domain.NearbyRow> NearbyRows=Array.Empty<LibreKO.Domain.NearbyRow>();
    public string LastSent="",LastMenu="";
    public int LastLinkTip;
    public (int,int,int) LastDrop;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var type=method!.ReturnType;
        string name=method.Name;
        if(name=="ReadHistory") return ChatReader?.Invoke((int)args![0]!, (bool)args[1]!, (string)args[2]!) ?? Array.Empty<string>();
        if(name=="NearbyPlayers") return NearbyRows;
        if(name=="Send") {LastSent=(string)args![0]!;return null;}
        if(name=="ShowLinkTooltip") {LastLinkTip=(int)args![0]!;return null;}
        if(name=="PlayerMenu") {LastMenu=(string)args![0]!;return null;}
        if(name=="get_NpcPortrait") return NpcPortraitSource?.Invoke();
        if(name=="NotificationCount") return (string)args![0]! == "mail" ? MailUnreadCount?.Invoke() ?? (WithUnreadMail?3:0) : 0;
        if(name=="SetPage") { _page=(int)args![0]!; return null; }
        if(name=="ActivateAbs") { LastActivate=(int)args![0]!; return null; }
        if(name=="Drop") { LastDrop=((int)args![0]!, (int)args[1]!, (int)args[2]!); return null; }
        if(name=="Clear") { ClearCount++; return null; }
        if (CharacterAudit != null && method.DeclaringType == typeof(IGameCharacter))
            return CharacterAudit.CharacterValue(name, type, args);
        if (name == "get_CharacterPanel" && LiveCharacterPanel != null) return LiveCharacterPanel;
        if (name == "get_CharacterPanel" && CharacterAudit != null) return CharacterAudit;
        if(type==typeof(void)) return null;
        if(type==typeof(bool) && name=="get_MiniMapVisible") return true;
        if(type==typeof(string)) return name switch {
            "get_Name"=>"Classic Preview", "get_ClassName"=>"Warrior", "get_ZoneName"=>"Moradon",
            "get_NationName"=>Nation==1 ? "Karus" : "El Morad", _=>"" };
        if(type==typeof(int)) return name switch {
            "get_Page"=>_page, "get_Nation"=>Nation, "get_Class"=>Nation==1 ? 105 : 205, "get_Level"=>70,
            "get_Hp"=>2400,"get_MaxHp"=>3200,"get_Mp"=>800,"get_MaxMp"=>1200,
            "get_Pages"=>8,"get_SlotsPerPage"=>10,"get_GridStart"=>14,"get_GridCount"=>28,
            "get_Gold"=>1234567,"get_Weight"=>320,"get_MaxWeight"=>1800,"get_Race"=>Nation==1 ? 1:11,
            _=>0 };
        if(type==typeof(double)) return name=="get_ExpPercent" ? 42.5 : 0d;
        if(type==typeof(float)) return name switch {"get_X"=>764.8f,"get_Z"=>416.8f,_=>0f};
        if(type==typeof(GameItem)) return GameItem.Empty((int)args![0]!);
        if(type==typeof(HotSlotInfo)) {
            int abs=(int)args![0]!;
            return WithCooldown ? new HotSlotInfo(abs,1000+abs,false,null,0.5f,"Cooldown fixture","",42):HotSlotInfo.Empty(abs);
        }
        if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(IReadOnlyList<>))
            return Array.CreateInstance(type.GetGenericArguments()[0],0);
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
