using Godot;
using System.Reflection;
using System.Text.Json;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;

public partial class Preview
{
    private async Task CaptureHudAudit(int nation)
    {
        var output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/ui-refinement-audit"));
        bool widthPortraitAudit=OS.GetCmdlineUserArgs().Contains("npc-portrait-width-audit");
        bool fitPortraitAudit=OS.GetCmdlineUserArgs().Contains("npc-portrait-fit-audit") || widthPortraitAudit;
        bool allPortraitAudit=OS.GetCmdlineUserArgs().Contains("npc-portraits-all-audit") || fitPortraitAudit;
        bool portraitAudit=OS.GetCmdlineUserArgs().Contains("npc-portrait-pilot-audit") || allPortraitAudit;
        bool liveAudit=portraitAudit || OS.GetCmdlineUserArgs().Contains("npc-live-mail-audit");
        bool responseAudit=liveAudit || OS.GetCmdlineUserArgs().Contains("hud-npc-response-audit");
        bool chatInkAudit=responseAudit || OS.GetCmdlineUserArgs().Contains("chat-ink-audit");
        if(chatInkAudit) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/karus-chat-ink-audit"));
        if(OS.GetCmdlineUserArgs().Contains("upstream-chat-audit")) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/upstream-chat-bag-audit/hud"));
        if(OS.GetCmdlineUserArgs().Contains("chat-dock-audit")) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/chat-dock-audit/hud"));
        if(responseAudit) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/hud-npc-response-audit"));
        if(liveAudit) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/npc-live-mail-audit"));
        if(portraitAudit) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/"+(allPortraitAudit?"npc-portraits-all-audit":"npc-portrait-pilot-audit")));
        if(fitPortraitAudit) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/npc-portrait-fit-audit"));
        if(widthPortraitAudit) output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/npc-portrait-width-audit"));
        System.IO.Directory.CreateDirectory(output);
        using var mailAudit=liveAudit?new MailAudit():null;
        if(mailAudit!=null)
        {
            await mailAudit.Connect();
            _windowData!.MailUnreadCount=()=>mailAudit.Windows.NotificationCount("mail");
        }
        _windowData!.WithUnreadMail=true;
        var window=GetWindow();window.ContentScaleMode=Window.ContentScaleModeEnum.Disabled;
        window.Size=new Vector2I(1120,660);window.ContentScaleSize=Vector2I.Zero;
        Input.ParseInputEvent(new InputEventMouseMotion { Position=new Vector2(1104,644),GlobalPosition=new Vector2(1104,644) });
        AddChild(new ColorRect { Color=new Color("34362b"),Size=new Vector2(1120,660),MouseFilter=MouseFilterEnum.Ignore });
        // A light background exposes font outlines, stray button fills and clipped corners.
        AddChild(new ColorRect { Color=new Color("afa995"),Position=new Vector2(0,0),Size=new Vector2(460,280),MouseFilter=MouseFilterEnum.Ignore });
        var status=new StatusHud();AddChild(status);
        var chat=new ChatWindow();AddChild(chat);
        var info=new LogWindow();AddChild(info);info.Visible=true;
        var pm=new HudWindow("whisper_fixture","Guardian",new Vector2(570,32),persistLayout:false);
        var scroll=new ScrollContainer { CustomMinimumSize=new Vector2(248,222) };
        var messages=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };
        scroll.AddChild(messages);pm.Body.AddChild(scroll);
        messages.AddChild(ClassicWhisperSkin.Line("Guardian",false,false,"We will meet at Moradon."));
        messages.AddChild(ClassicWhisperSkin.Line("Canbo",true,false,"I am ready. See you there!"));
        messages.AddChild(ClassicWhisperSkin.Line("Guardian",false,false,"Bring your potions and equipment."));
        var inputRow=new HBoxContainer();pm.Body.AddChild(inputRow);
        inputRow.AddChild(new LineEdit { Text="Ready",SizeFlagsHorizontal=SizeFlags.ExpandFill });
        inputRow.AddChild(new Button { Text="Send" });AddChild(pm);
        await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        ClassicWhisperSkin.Apply(pm);
        await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
        chat.Position=new Vector2(20,424);info.Position=new Vector2(660,409);
        pm.Position=new Vector2(570,32);
        var chatLog=(LogText)typeof(ChatWindow).GetField("_log",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(chat)!;
        var infoLog=(LogText)typeof(LogWindow).GetField("_log",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(info)!;
        chatLog.Set(new[]{"[color=#ffff00]Welcome to LibreKO. Press Enter to chat.[/color]","Canbo: Are you ready for the quest?","[color=#80ffff]Guardian: I am at Moradon.[/color]","[color=#a8ffb0]Party: Let us go together.[/color]"});
        infoLog.Set(new[]{"Beginning attack on Worm.","[color=#80cfff]You recovered 42 MP.[/color]","[color=#ffff00]Water of Ibex succeeded.[/color]"});
        await ToSignal(GetTree().CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
        foreach(var log in new[]{chatLog,infoLog})
        {
            var text=Descendants(log).OfType<RichTextLabel>().Single();
            if(text.GetThemeFont("normal_font")!=ThemeDB.FallbackFont) throw new Exception("Chat font differs from the native LibreKO chat font");
        }
        foreach(var label in Descendants(status).OfType<Label>().Where(l=>l.Text.Contains('/') || l.Text.StartsWith("Lv.")))
            if(label.GetThemeFont("font")!=UiTheme.Strong) throw new Exception("Status text uses a different font family");
        var infoTitle=(Control)typeof(LogWindow).GetField("_title",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(info)!;
        if(((Panel)infoTitle).GetThemeStylebox("panel") is not StyleBoxTexture) throw new Exception("Info lacks its original plate");
        if(infoTitle.Position.X+infoTitle.Size.X!=info.Size.X) throw new Exception("Info caption is not aligned to the right edge");
        var infoDrag=infoTitle;
        var infoRail=(Control)typeof(LogWindow).GetField("_rail",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(info)!;
        if(infoDrag.Position.X!=info.Size.X-64 || infoDrag.MouseFilter!=MouseFilterEnum.Stop || infoRail.Position.X+infoRail.Size.X!=info.Size.X-3 || infoLog.Position.X!=4 || infoLog.GetRect().End.X>=infoRail.Position.X) throw new Exception("Info drag and scroll controls must share the right edge");
        var mail=Descendants(status).OfType<ClassicStatusShortcut>().Single(b=>b.Name=="btn_mail");
        var shortcuts=Descendants(status).OfType<ClassicStatusShortcut>().ToArray();
        var letters=shortcuts.Where(s=>s.HasBackground).OrderBy(s=>s.Position.X).ToArray();
        if(!letters.Select(s=>s.Letter).SequenceEqual(new[]{"Q","P","A","D"})) throw new Exception("The shortcut row must contain Q/P/A/D with no M");
        if(mail.HasBackground || mail.Letter!="" || mail.Size!=new Vector2(28,28) || mail.MailIconRect.Size!=new Vector2(28,20)) throw new Exception("Mail must be a larger native envelope without a plate or letter");
        if(mail.Position.X!=letters[0].Position.X || mail.Position.Y!=letters[0].Position.Y+letters[0].Size.Y+5) throw new Exception("Mail is not aligned below Q");
        var coordinates=Descendants(status).OfType<Label>().Single(l=>l.Name=="Text_Coordinates");
        if(coordinates.Text!="764, 416" || coordinates.Position!=new Vector2(155,58) || coordinates.Size.X!=117 || coordinates.Size.Y>16) throw new Exception("HUD coordinates exceed the native parchment field");
        var zoneName=Descendants(status).OfType<Label>().Single(l=>l.Name=="Text_ZoneName");
        if(zoneName.Text!="Moradon" || zoneName.GetRect().End.Y>coordinates.Position.Y || zoneName.Size.X!=117 || coordinates.GetRect().End.Y>74) throw new Exception("Map name and coordinates overlap or exceed parchment bounds");
        if(nation==1)
        {
            var infoLabel=Descendants(infoTitle).OfType<Label>().Single();
            if(infoLabel.GetThemeColor("font_color")!=Colors.Black || infoLabel.GetThemeConstant("outline_size")!=0) throw new Exception("Karus Info caption is not clean black ink");
        }
        string prefix=nation==1?"karus":"human";
        async Task Shot(string state)
        {
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,prefix+"-"+state+".png"));
        }
        await Shot("hud");
        if(responseAudit)
        {
            foreach(var shortcut in letters) shortcut.EmitSignal(Control.SignalName.MouseEntered);
            await Shot("shortcuts-hover");
            foreach(var shortcut in letters) { shortcut.ToggleMode=true;shortcut.SetPressedNoSignal(true);shortcut.QueueRedraw(); }
            await Shot("shortcuts-pressed");
            foreach(var shortcut in letters) { shortcut.SetPressedNoSignal(false);shortcut.ToggleMode=false;shortcut.EmitSignal(Control.SignalName.MouseExited); }
            GD.Print("HUD_SHORTCUT_RESTORE_OK: Q/P/A/D native plates, no M, transparent 28x20 envelope aligned under Q");
        }
        if(chatInkAudit && nation==1)
        {
            var captions=Descendants(chat).OfType<Label>().Where(l=>new[]{"Chat","All","Private","Shout","Party","Clan","Nation","Alliance"}.Contains(l.Text)).ToArray();
            if(captions.Length!=8 || captions.Any(l=>l.GetThemeColor("font_color")!=Colors.Black || l.GetThemeConstant("outline_size")!=0))
                throw new Exception("Karus chat captions must use clean black ink");
            var tabs=Descendants(chat).OfType<Button>().Where(b=>b.ToggleMode).ToArray();
            foreach(var tab in tabs)
            {
                tab.EmitSignal(Control.SignalName.MouseEntered);tab.SetPressedNoSignal(true);
                if(Descendants(tab).OfType<Label>().Single().GetThemeColor("font_color")!=Colors.Black) throw new Exception("Karus tab state changed its black ink");
                tab.EmitSignal(Control.SignalName.MouseExited);
            }
            await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
            await Shot("chat-selected");
            GD.Print("KARUS_CHAT_INK_OK: eight black captions, unchanged hover and selected ink, no dark outline or shadow");
        }
        if(OS.GetCmdlineUserArgs().Contains("upstream-chat-audit"))
        {
            foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck"})
                if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false)) throw new Exception("Missing chat audit pack");
            ItemData.EnsureLoaded();
            var type=typeof(World).Assembly.GetType("LibreKO.ChatSystem")!;
            var system=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object?[]{null},null)!;
            var entryType=type.GetNestedType("ChatEntry",BindingFlags.NonPublic)!;
            var store=type.GetField("_store",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(system)!;
            int raptor=156211038;
            void Add(byte channel,string name,string text)
            {
                var entry=entryType.GetMethod("Player",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,new object[]{channel,name,nation,false,text})!;
                entryType.GetProperty("Time")!.SetValue(entry,new DateTime(2026,10,5,14,32,0));
                store.GetType().GetMethod("AddLast",new[]{entryType})!.Invoke(store,new[]{entry});
            }
            Add(1,"Canbo","Selling "+LibreKO.Domain.ChatItemLink.Token(raptor));
            Add(3,"Guardian","Bring potions and meet at Moradon.");
            Add(5,"Merchant","Looking for a party.");
            var read=type.GetMethod("ClassicHistory",BindingFlags.Instance|BindingFlags.NonPublic)!;
            _chatData!.ChatReader=(mask,time,colors)=>(IReadOnlyList<string>)read.Invoke(system,new object[]{mask,time,colors})!;
            var all=_chatData.ChatReader(1023,false,"");
            if(all.Count!=3 || !all[0].Contains("[url=i:156211038]") || !all[0].Contains("Raptor(+8)") || all[0].Contains("<LINK>")) throw new Exception("Classic item links do not render native item metadata");
            var party=_chatData.ChatReader(4,true,"");
            if(party.Count!=1 || !party[0].Contains("14:32") || !party[0].Contains("Guardian")) throw new Exception("Classic filters and timestamps failed");
            if(!_chatData.ChatReader(1023,false,"ff80ff,ff9a3c,5fd95f,46d3c0,6fb7ff")[0].Contains("#ff80ff")) throw new Exception("Classic custom channel ink failed");
            chatLog.Set(all);
            var input=(LineEdit)typeof(ChatWindow).GetField("_input",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(chat)!;
            input.GrabFocus();input.Clear();
            foreach(char character in "abc")
            {
                Input.ParseInputEvent(new InputEventKey {Pressed=true,Keycode=(Key)char.ToUpperInvariant(character),Unicode=character});
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(new InputEventKey {Pressed=false,Keycode=(Key)char.ToUpperInvariant(character)});
            }
            if(input.Text!="abc" || input.CaretColumn!=3) throw new Exception("Actual chat typing reverses characters or resets the caret: "+input.Text);
            input.CaretColumn=1;
            Input.ParseInputEvent(new InputEventKey {Pressed=true,Keycode=Key.X,Unicode='x'});
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventKey {Pressed=false,Keycode=Key.X});
            if(input.Text!="axbc" || input.CaretColumn!=2) throw new Exception("Typing within an existing draft moves the caret unexpectedly");
            input.GrabFocus();input.Text="Selling ";input.CaretColumn=input.Text.Length;
            typeof(LibreKO.Plugins.PluginGame).GetMethod("RaiseChatItemLink",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Plugin.Kit.Game,new object[]{raptor,ItemData.DisplayName(raptor)});
            if(input.CaretColumn!=input.Text.Length) throw new Exception("Inserting an item link resets the caret");
            if(!input.Text.Contains("Raptor(+8)")) throw new Exception("Classic Shift-link request did not reach draft");
            await Shot("item-link");
            input.EmitSignal(LineEdit.SignalName.TextSubmitted,input.Text);
            if(!_chatData.LastSent.Contains("<LINK>")) throw new Exception("Classic link submission lost its wire token");
            input.GrabFocus();input.Text="draft";
            input.EmitSignal(Control.SignalName.GuiInput,new InputEventKey {Pressed=true,Keycode=Key.Up});
            if(input.CaretColumn!=input.Text.Length) throw new Exception("Inserting an item link resets the caret");
            if(!input.Text.Contains("Raptor(+8)")) throw new Exception("Chat history Up failed");
            input.EmitSignal(Control.SignalName.GuiInput,new InputEventKey {Pressed=true,Keycode=Key.Down});
            if(input.Text!="draft") throw new Exception("Chat history Down lost unfinished text");
            input.Clear();input.ReleaseFocus();
            var label=Descendants(chatLog).OfType<RichTextLabel>().Single();
            label.EmitSignal(RichTextLabel.SignalName.MetaHoverStarted,"i:156211038");
            if(_chatData.LastLinkTip!=raptor) throw new Exception("Item hover tooltip bridge failed");
            label.EmitSignal(RichTextLabel.SignalName.MetaClicked,"p:Canbo");
            if(_chatData.LastMenu!="Canbo") throw new Exception("Player name menu bridge failed");
            Descendants(chat).OfType<Button>().First(b=>b.ToggleMode).EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton {Pressed=true,ButtonIndex=MouseButton.Right});await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
            var settings=(ClassicChatPanel)typeof(ChatWindow).GetField("_settingsPanel",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(chat)!;
            foreach(var button in Descendants(settings.Body).OfType<Button>())
                if(button.Position.X<0 || button.Position.Y<0 || button.GetRect().End.X>settings.Body.Size.X || button.GetRect().End.Y>settings.Body.Size.Y) throw new Exception("Chat settings button exceeds actual body bounds: "+button.Text);
            await Shot("settings");
            var general=Descendants(settings.Body).OfType<Button>().Single(b=>b.Text=="General: On");
            var shout=Descendants(settings.Body).OfType<Button>().Single(b=>b.Text=="Shout: On");
            var timeButton=Descendants(settings.Body).OfType<Button>().Single(b=>b.Text=="Time: Off");
            general.EmitSignal(BaseButton.SignalName.Pressed);shout.EmitSignal(BaseButton.SignalName.Pressed);timeButton.EmitSignal(BaseButton.SignalName.Pressed);
            if(!label.Text.Contains("Guardian") || label.Text.Contains("Merchant") || label.Text.Contains("Selling") || !label.Text.Contains("14:32")) throw new Exception("Actual Classic filter/time controls failed");
            settings.Visible=false;await Shot("filtered");
            general.EmitSignal(BaseButton.SignalName.Pressed);shout.EmitSignal(BaseButton.SignalName.Pressed);timeButton.EmitSignal(BaseButton.SignalName.Pressed);
            _chatData.NearbyRows=new[]{new LibreKO.Domain.NearbyRow("Guardian",1,nation,70,0,0,12,0,0,LibreKO.Domain.NearbyRelation.Party),new LibreKO.Domain.NearbyRow("Sentinel",2,nation,70,0,0,24,0,0,LibreKO.Domain.NearbyRelation.Clan),new LibreKO.Domain.NearbyRow("Merchant",3,3-nation,60,0,0,38,0,0,LibreKO.Domain.NearbyRelation.Enemy)};
            chat.OpenNearby();await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
            var nearby=(ClassicChatPanel)typeof(ChatWindow).GetField("_nearbyPanel",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(chat)!;
            if(Descendants(nearby.Body).OfType<Button>().Count(b=>b.Text.Contains(" m"))!=3) throw new Exception("Nearby rows failed");
            await Shot("nearby");
            Descendants(nearby.Body).OfType<Button>().First(b=>b.Text.StartsWith("Guardian")).EmitSignal(BaseButton.SignalName.Pressed);
            if(input.Text!="@Guardian ") throw new Exception("Nearby whisper action failed");
            _chatData.NearbyRows=Enumerable.Range(0,17).Select(i=>new NearbyRow("Player"+i,i,nation,70,0,0,i*4,0,0,NearbyRelation.Ally)).ToArray();
            Descendants(nearby).OfType<Godot.Timer>().Single().EmitSignal(Godot.Timer.SignalName.Timeout);
            if(Descendants(nearby.Body).OfType<Button>().Count(b=>b.Text.Contains(" m"))!=8) throw new Exception("Nearby row allocation is not bounded to one page");
            await Shot("nearby-paged");
            var next=Descendants(nearby.Body).OfType<Button>().Single(b=>b.Text=="Next");next.EmitSignal(BaseButton.SignalName.Pressed);next.EmitSignal(BaseButton.SignalName.Pressed);
            if(!next.Disabled || Descendants(nearby.Body).OfType<Button>().Count(b=>b.Text.Contains(" m"))!=1) throw new Exception("Nearby last page does not match roster");
            nearby.Visible=false;input.Clear();input.ReleaseFocus();
            var nativeLog=new HudLogText {BbcodeEnabled=true};
            var jump=new Button();type.GetField("_jumpPill",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(system,jump);
            type.GetField("_scroll",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(system,nativeLog);
            bool immediate=false;
            void Published(string text)=>immediate=((IReadOnlyList<string>)read.Invoke(system,new object[]{1023,false,""})!).Any(l=>l.Contains("Immediate message"));
            LibreKO.Plugins.PluginHost.Game.Chat.LineAdded+=Published;
            var fresh=entryType.GetMethod("Player",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,new object[]{(byte)1,"Canbo",nation,false,"Immediate message"});
            type.GetMethod("AddEntry",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(system,new[]{fresh});
            LibreKO.Plugins.PluginHost.Game.Chat.LineAdded-=Published;
            nativeLog.Free();jump.Free();
            if(!immediate) throw new Exception("Published chat callback sees stale structured history");
            GD.Print("CLASSIC_CHAT_FEATURES_OK: structured filters, timestamps, native item links, wire submission, history/draft, item tooltip callback, name menu, actual settings bounds and nearby whisper");
        }
        if(OS.GetCmdlineUserArgs().Contains("chat-dock-audit")) await AuditChatDock(chat,info,infoTitle,Shot);
        pm.SetMinimized(true);pm.AttentionStyler?.Invoke(true);
        await ToSignal(GetTree().CreateTimer(.15),SceneTreeTimer.SignalName.Timeout);await Shot("pm-unread");
        if(mailAudit!=null)
        {
            async Task CheckMail(int count)
            {
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
                if((int)typeof(ClassicStatusShortcut).GetField("_pending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mail)! !=count)
                    throw new Exception("Classic mail badge did not follow the native unread count");
                if(count==0 && (int)typeof(ClassicStatusShortcut).GetField("_mailFrame",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mail)! !=0)
                    throw new Exception("Read mail still animates the envelope");
            }
            await mailAudit.Read(1,true,2);await CheckMail(2);await Shot("mail-remaining");
            await mailAudit.Read(1,true,2);await CheckMail(2);
            await mailAudit.Read(2,false,2);await CheckMail(2);
            await mailAudit.Read(2,true,1);await CheckMail(1);
            await mailAudit.Read(3,true,0);await CheckMail(0);await Shot("mail-read");
            mailAudit.SetCount(1);await CheckMail(1);await Shot("mail-new");
            mailAudit.SetCount(0);await CheckMail(0);
            GD.Print("MAIL_READ_BADGE_OK: four native refresh requests, late successful reads, failed read, repeated read, last read clears badge and animation, new mail notifies again");
        }
        var binary=ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/KnightOnlineUiClassic.dll");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,prefix+"-hud-verification.json"),JsonSerializer.Serialize(new {
            nation,chatFont="ThemeDB.FallbackFont",pmFont="UiTheme.Strong / native chat family",statusFont="UiTheme.Strong / native chat family",
            infoTitleX=infoTitle.Position.X,infoTitleWidth=infoTitle.Size.X,infoWidth=info.Size.X,mailHeight=mail.Size.Y,
            mailX=mail.Position.X,mailY=mail.Position.Y,mailIconWidth=mail.MailIconRect.Size.X,mailIconHeight=mail.MailIconRect.Size.Y,
            letters=letters.Select(s=>s.Letter).ToArray(),mailHasBackground=mail.HasBackground,karusInfoBlack=nation==1,
            mailRefreshRequests=mailAudit?.RefreshRequests??0,mailReadClearsBadge=liveAudit,
            pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(binary))),
            clientHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(binary)!,"LibreKO.dll"))))
        },new JsonSerializerOptions { WriteIndented=true }));
        GD.Print("HUD_REFINEMENT_OK: native chat/PM/status fonts, right info caption, separate transparent envelope row");
    }
}
