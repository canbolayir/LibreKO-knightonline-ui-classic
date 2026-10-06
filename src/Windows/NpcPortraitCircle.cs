using Godot;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

/// <summary>A static portrait inside a ring sampled from the original quest frame artwork.</summary>
public partial class NpcPortraitCircle : Control
{
    private static readonly Shader CircleShader=new() { Code="""
        shader_type canvas_item;
        uniform sampler2D frame_art : source_color, filter_nearest;
        uniform vec2 frame_size;
        void fragment() {
            vec2 offset=UV-vec2(.5);
            float radius=length(offset);
            if(radius>.5) { COLOR=vec4(0.); }
            else if(radius>.445) {
                float angle=atan(offset.y,offset.x)/6.2831853+.5;
                vec2 art=vec2(42.+angle*278.,306.+(radius-.445)/.055*4.)/frame_size;
                COLOR=texture(frame_art,art);
                COLOR.rgb=mix(vec3(.12,.10,.07),COLOR.rgb,COLOR.a);
                COLOR.a=1.-smoothstep(.493,.5,radius);
            } else {
                vec4 portrait=texture(TEXTURE,UV);
                COLOR=vec4(mix(vec3(.055,.045,.03),portrait.rgb,portrait.a),1.);
            }
        }
        """ };
    private readonly TextureRect _image;
    private readonly VBoxContainer _column;
    private readonly Label _nameLabel,_levelLabel;
    private string _appearance="";
    public const int ColumnWidth=96;
    private int _captionWidth=ColumnWidth;
    public int CaptionWidth => _captionWidth;
    public string DisplayedName => _nameLabel.Text;
    public string DisplayedLevel => _levelLabel.Text;
    public Texture2D? PortraitTexture => _image.Texture;
    public bool IsReady => _image.Texture!=null;
    public NpcPortraitCircle(bool dialogueLayout=false)
    {
        int columnWidth=dialogueLayout?84:ColumnWidth;
        int portraitSize=dialogueLayout?84:72;
        _captionWidth=columnWidth;
        Name="npc_portrait";MouseFilter=MouseFilterEnum.Ignore;
        SetMeta("classic_detail_control",true);
        CustomMinimumSize=new Vector2(columnWidth,0);SizeFlagsVertical=SizeFlags.ShrinkBegin;
        _column=new VBoxContainer { MouseFilter=MouseFilterEnum.Ignore,CustomMaximumSize=new Vector2(columnWidth,-1) };
        _column.AddThemeConstantOverride("separation",3);_column.SetMeta("classic_detail_control",true);
        AddChild(_column);_column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _column.MinimumSizeChanged+=UpdateMinimumSize;
        var frame=Plugin.Kit.Texture("ui_quest_us.png")!;
        var material=new ShaderMaterial { Shader=CircleShader };
        material.SetShaderParameter("frame_art",frame);material.SetShaderParameter("frame_size",frame.GetSize());
        var center=new CenterContainer { CustomMinimumSize=new Vector2(columnWidth,portraitSize),MouseFilter=MouseFilterEnum.Ignore };
        _column.AddChild(center);
        _image=new TextureRect { CustomMinimumSize=new Vector2(portraitSize,portraitSize),MouseFilter=MouseFilterEnum.Ignore,
            ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.Scale,
            TextureFilter=TextureFilterEnum.Linear,Material=material };
        center.AddChild(_image);
        _nameLabel=Caption(Plugin.Kit.Bold,13,ClassicReportDesign.Caption);_nameLabel.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        _nameLabel.CustomMaximumSize=new Vector2(columnWidth,-1);_nameLabel.Visible=!dialogueLayout;
        _levelLabel=Caption(dialogueLayout?Layout.ClassicNpcLayout.BodyFont:Plugin.Kit.Regular,12,ClassicReportDesign.Value);
        _column.AddChild(_nameLabel);_column.AddChild(_levelLabel);
    }
    private static Label Caption(Font font,int size,Color ink)
    {
        var label=new Label { HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore };
        label.SetMeta("classic_detail_control",true);label.AddThemeFontOverride("font",font);label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",ink);label.AddThemeColorOverride("font_shadow_color",Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x",1);label.AddThemeConstantOverride("shadow_offset_y",1);
        return label;
    }
    public override Vector2 _GetMinimumSize() => new(_captionWidth,_column?.GetCombinedMinimumSize().Y??0);
    public void FitCaptionWidth(float width)
    {
        int fitted=Math.Max(ColumnWidth,(int)Mathf.Floor(width));
        if(fitted==_captionWidth) return;
        _captionWidth=fitted;
        CustomMinimumSize=new Vector2(fitted,0);
        _column.CustomMaximumSize=_nameLabel.CustomMaximumSize=new Vector2(fitted,-1);
        UpdateMinimumSize();
    }
    public async void Show(GameNpcPortrait npc)
    {
        string name=npc.Name.Trim();
        _nameLabel.Text=name.StartsWith('[') && name.EndsWith(']')?name[1..^1]:name;
        _levelLabel.Text="Lv. "+npc.Level;
        TooltipText=npc.Name+" · "+_levelLabel.Text;
        if(_appearance==npc.AppearanceKey) return;
        _appearance=npc.AppearanceKey;
        _image.Texture=null;
        string requested=_appearance;
        var texture=await NpcPortraitCache.Get(this,npc);
        if(!GodotObject.IsInstanceValid(this) || !IsInsideTree() || requested!=_appearance) return;
        _image.Texture=texture;
    }
}
