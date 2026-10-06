using Godot;
using LibreKO.Domain;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public partial class ChatWindow
{
    public void OpenSettings()
    {
        if(_settingsPanel!=null) {_settingsPanel.Visible=true;_settingsPanel.MoveToFront();return;}
        _settingsPanel=new ClassicChatPanel("Chat Settings",516);GetParent().AddChild(_settingsPanel);
        var body=_settingsPanel.Body;
        var hint=ClassicNpcLayout.Text("Right-click a chat tab to open settings.",12);
        hint.Position=Vector2.Zero;hint.Size=new Vector2(319,24);body.AddChild(hint);
        var categories=ChatCategories.Each.Where(c=>c!=ChatCategory.Whisper).ToArray();
        for(int i=0;i<categories.Length;i++)
        {
            var category=categories[i];var button=ClassicChatPanel.Button("");
            void Caption()=>button.Text=ChatCategories.Caption(category)+": "+((_filter&(int)category)!=0?"On":"Off");
            Caption();button.Position=new Vector2(i%2*163,30+i/2*28);button.Size=new Vector2(156,24);body.AddChild(button);
            button.Pressed+=()=> {_filter^=(int)category;Plugin.Kit.Context.Settings.SetInt("chat.filter",_filter);Caption();Reload();};
        }
        var timestamps=ClassicChatPanel.Button("Time: "+(_timestamps?"On":"Off"));
        timestamps.Position=new Vector2(0,178);timestamps.Size=new Vector2(156,24);body.AddChild(timestamps);
        timestamps.Pressed+=()=> {_timestamps=!_timestamps;timestamps.Text="Time: "+(_timestamps?"On":"Off");Plugin.Kit.Context.Settings.SetBool("chat.timestamps",_timestamps);Reload();};
        var font=ClassicChatPanel.Button("Text: "+_fontSize);font.Position=new Vector2(163,178);font.Size=new Vector2(156,24);body.AddChild(font);
        font.Pressed+=()=> {_fontSize=_fontSize==12?13:_fontSize==13?15:12;font.Text="Text: "+_fontSize;_log.SetFontSize(_fontSize);Plugin.Kit.Context.Settings.SetInt("chat.font_size",_fontSize);};
        var locked=ClassicChatPanel.Button("Position: "+(_layout!.Locked?"Locked":"Free"));locked.Position=new Vector2(0,208);locked.Size=new Vector2(156,24);body.AddChild(locked);
        locked.Pressed+=()=> {_layout.Locked=!_layout.Locked;locked.Text="Position: "+(_layout.Locked?"Locked":"Free");Plugin.Kit.Context.Settings.SetBool("chat.locked",_layout.Locked);};
        var nearby=ClassicChatPanel.Button("Nearby Players");nearby.Position=new Vector2(163,208);nearby.Size=new Vector2(156,24);body.AddChild(nearby);nearby.Pressed+=OpenNearby;
        var label=ClassicNpcLayout.Text("Channel colours (click to cycle)",12,true);label.Position=new Vector2(0,244);label.Size=new Vector2(319,24);body.AddChild(label);
        var original=new[]{"e8e8e8","ff9a3c","5fd95f","46d3c0","6fb7ff"};
        var values=_colors.Split(',');if(values.Length!=5 || values.Any(c=>!Color.HtmlIsValid(c))) values=original.ToArray();
        string[] names={"General","Shout","Party","Clan","Alliance"};
        var swatches=new List<Button>();
        for(int i=0;i<5;i++)
        {
            int slot=i;var swatch=ClassicChatPanel.Button(names[i]);swatch.Position=new Vector2(i%2*163,274+i/2*28);swatch.Size=new Vector2(156,24);body.AddChild(swatch);swatches.Add(swatch);
            void Paint()=>swatch.AddThemeColorOverride("font_color",new Color(values[slot]));Paint();
            swatch.Pressed+=()=> {int index=Array.FindIndex(ChatColors.Palette,c=>c.ToHtml(false)==values[slot]);values[slot]=ChatColors.Palette[(index+1)%ChatColors.Palette.Length].ToHtml(false);_colors=string.Join(',',values);Plugin.Kit.Context.Settings.Set("chat.colors",_colors);Paint();Reload();};
        }
        var reset=ClassicChatPanel.Button("Reset Colours");reset.Position=new Vector2(163,330);reset.Size=new Vector2(156,24);body.AddChild(reset);
        reset.Pressed+=()=> {_colors="";values=original.ToArray();Plugin.Kit.Context.Settings.Set("chat.colors","");for(int i=0;i<5;i++) swatches[i].AddThemeColorOverride("font_color",new Color(values[i]));Reload();};
        var help=ClassicNpcLayout.Text("History: Up / Down     Item link: Shift + right-click",11);help.Position=new Vector2(0,366);help.Size=new Vector2(319,24);body.AddChild(help);
    }
    public void OpenNearby()
    {
        if(_nearbyPanel!=null) {_nearbyPanel.Visible=true;_nearbyPanel.MoveToFront();return;}
        _nearbyPanel=new ClassicChatPanel("Nearby Players",356);GetParent().AddChild(_nearbyPanel);
        var list=new VBoxContainer {Position=Vector2.Zero,Size=new Vector2(319,234)};
        list.AddThemeConstantOverride("separation",3);_nearbyPanel.Body.AddChild(list);
        var previousButton=ClassicChatPanel.Button("Previous");var nextButton=ClassicChatPanel.Button("Next");
        var pageCaption=ClassicNpcLayout.Text("1 / 1",12);
        _nearbyPanel.Body.AddChild(previousButton);_nearbyPanel.Body.AddChild(nextButton);_nearbyPanel.Body.AddChild(pageCaption);
        var timer=new Godot.Timer {WaitTime=1,Autostart=true};_nearbyPanel.AddChild(timer);
        IReadOnlyList<NearbyRow>? previous=null;int page=0,lastPage=-1;
        void Refresh()
        {
            if(!_nearbyPanel.IsVisibleInTree()) return;
            var players=_game.Chat.NearbyPlayers();
            int pages=Math.Max(1,(players.Count+7)/8);page=Math.Clamp(page,0,pages-1);
            if(previous!=null && previous.SequenceEqual(players) && page==lastPage) return;
            previous=players.ToArray();lastPage=page;
            var visible=players.Skip(page*8).Take(8).ToArray();
            bool first=list.GetChildCount()==0;
            int height=140+Math.Max(1,visible.Length)*28;
            _nearbyPanel.CustomMinimumSize=new Vector2(363,height);_nearbyPanel.Size=new Vector2(363,height);
            list.Size=new Vector2(319,Math.Max(1,visible.Length)*28);
            if(first) _nearbyPanel.Position=new Vector2(Mathf.Round((GetViewportRect().Size.X-363)/2),Mathf.Round((GetViewportRect().Size.Y-height)/2));
            float pagerY=list.Size.Y+4;
            previousButton.Position=new Vector2(0,pagerY);previousButton.Size=new Vector2(100,24);previousButton.Disabled=page==0;
            nextButton.Position=new Vector2(219,pagerY);nextButton.Size=new Vector2(100,24);nextButton.Disabled=page==pages-1;
            pageCaption.Position=new Vector2(100,pagerY);pageCaption.Size=new Vector2(119,24);pageCaption.HorizontalAlignment=HorizontalAlignment.Center;pageCaption.Text=$"{page+1} / {pages}";
            foreach(var child in list.GetChildren()) {list.RemoveChild(child);child.QueueFree();}
            if(visible.Length==0) list.AddChild(ClassicNpcLayout.Text("No one nearby.",12));
            foreach(var player in visible)
            {
                var button=ClassicChatPanel.Button(player.Name+"  ·  "+player.Relation+"  ·  "+Mathf.Round(player.Distance)+" m");
                button.CustomMinimumSize=new Vector2(319,25);button.Alignment=HorizontalAlignment.Left;
                button.AddThemeColorOverride("font_color",new Color(player.Relation switch {NearbyRelation.Enemy=>"e06666",NearbyRelation.Party=>"5fd95f",NearbyRelation.Clan=>"46d3c0",_=>"6fa8ff"}));
                button.Pressed+=()=> {_prefix="@";FocusChannel("@");_input.Text="@"+player.Name+" ";_input.CaretColumn=_input.Text.Length;};
                button.GuiInput+=ev=> {if(ev is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Right}) {_game.Chat.PlayerMenu(player.Name,button.GetGlobalMousePosition());button.AcceptEvent();}};
                list.AddChild(button);
            }
        }
        previousButton.Pressed+=()=> {page--;Refresh();};nextButton.Pressed+=()=> {page++;Refresh();};
        timer.Timeout+=Refresh;Refresh();
    }
}
