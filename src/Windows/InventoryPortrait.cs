using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Windows;

public partial class InventoryPortrait : SubViewportContainer
{
    private readonly SubViewport _viewport;
    private readonly Node3D _scene=new();
    private readonly Camera3D _camera=new() { Current=true, Fov=32, Near=0.05f, Far=20 };
    private Node3D? _model;
    private string _signature="";
    private bool _rotating;
    private int _fitFrames;
    private Vector3[]? _framePoints;
    public InventoryPortrait(Vector2 size)
    {
        Size=size;
        Stretch=true;
        ClipContents=true;
        MouseFilter=MouseFilterEnum.Stop;
        TooltipText="Drag to rotate";
        _viewport=new SubViewport { Size=new Vector2I((int)size.X,(int)size.Y),TransparentBg=true,OwnWorld3D=true,
            RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled,Msaa3D=Viewport.Msaa.Msaa2X };
        AddChild(_viewport);
        _viewport.AddChild(_scene);
        _scene.AddChild(_camera);
        _scene.AddChild(new WorldEnvironment { Environment=new Godot.Environment {
            BackgroundMode=Godot.Environment.BGMode.ClearColor,
            AmbientLightSource=Godot.Environment.AmbientSource.Color, AmbientLightColor=Colors.White,AmbientLightEnergy=0.65f,
            TonemapMode=Godot.Environment.ToneMapper.Linear } });
        _scene.AddChild(new DirectionalLight3D { RotationDegrees=new Vector3(-25,-30,0),LightEnergy=1.3f });
        VisibilityChanged+=Refresh;
    }
    public override void _Ready() { ApplyPortraitMask();Plugin.Kit.Game.Inventory.Changed+=Refresh; Refresh(); }
    private void ApplyPortraitMask()
    {
        var source=Plugin.Kit.Layout("{nation}_inventory_us");
        var top=source.Children.First(n=>n.IsImage && n.W==362 && n.H==275);
        using var artwork=Plugin.Kit.Texture(top.Texture!)!.GetImage();
        using var mask=Image.CreateEmpty(_viewport.Size.X,_viewport.Size.Y,false,Image.Format.Rgba8);
        mask.Fill(Colors.White);
        for(int y=0;y<64;y++)
        {
            int sourceY=top.SrcY+(int)Position.Y+y-(top.Y-source.Y),edge=0;
            for(int x=0;x<80;x++)
                if(artwork.GetPixel(top.SrcX+x,sourceY).A>.35f) {edge=x+7;break;}
            int cut=Math.Max(0,edge+top.X-source.X-(int)Position.X);
            for(int x=0;x<Math.Min(cut,mask.GetWidth());x++) mask.SetPixel(x,y,Colors.Transparent);
        }
        Material=new ShaderMaterial {Shader=new Shader {Code="shader_type canvas_item; uniform sampler2D portrait_mask : filter_nearest, repeat_disable; void fragment(){ COLOR=texture(TEXTURE,UV)*COLOR; COLOR.a*=texture(portrait_mask,UV).a; }"}};
        ((ShaderMaterial)Material).SetShaderParameter("portrait_mask",ImageTexture.CreateFromImage(mask));
    }
    public override void _ExitTree() { Plugin.Kit.Game.Inventory.Changed-=Refresh; }
    private void Refresh()
    {
        bool visible=IsInsideTree() && IsVisibleInTree();
        _viewport.ProcessMode=visible?ProcessModeEnum.Inherit:ProcessModeEnum.Disabled;
        _viewport.RenderTargetUpdateMode=visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if(!visible || !_scene.IsInsideTree() || !Plugin.Kit.Game.Available || !Godot.FileAccess.FileExists("res://assets/characters/index.json")) return;
        var info=Net.I.LastEnter;
        int[] gear=InventoryConstants.VisualSlots.Select(s=>Plugin.Kit.Game.Inventory.At(s).ItemId).ToArray();
        string signature=$"{info.Race}:{info.Face}:{info.Hair}:"+string.Join(',',gear);
        if(signature==_signature) return;
        _signature=signature;
        if(_model!=null) { _scene.RemoveChild(_model); _model.QueueFree(); }
        _model=CharacterPreview.Build(info.Race,info.Face,gear,info.Hair,enableShine:true);
        _framePoints=null;
        if(_model==null) return;
        _scene.AddChild(_model);
        _model.RotationDegrees=new Vector3(0,0,0);
        _fitFrames=3;
    }
    public override void _Process(double delta)
    {
        if(_fitFrames>0 && --_fitFrames==0) FitCamera();
    }
    private void FitCamera()
    {
        if(_model==null) return;
        _framePoints??=BuildFramePoints(_model);
        Aabb bounds=default;bool found=false;
        var modelTransform=_model.GlobalTransform;
        foreach(var local in _framePoints)
        {
            var point=modelTransform*local;
            if(!found) {bounds=new Aabb(point,Vector3.Zero);found=true;}
            else bounds=bounds.Expand(point);
        }
        var center=found?bounds.GetCenter():new Vector3(0,1.25f,0);
        float tan=Mathf.Tan(Mathf.DegToRad(_camera.Fov/2));
        float aspect=(float)_viewport.Size.X/_viewport.Size.Y;
        float distance=found?Math.Max(bounds.Size.Y,bounds.Size.X/aspect)/(2*tan)*1.12f+bounds.Size.Z/2:6.4f;
        _camera.LookAtFromPosition(center+new Vector3(0,0,Math.Max(2.5f,distance)),center,Vector3.Up);
    }
    private static Vector3[] BuildFramePoints(Node3D model)
    {
        var points=new List<Vector3>();
        var inverse=model.GlobalTransform.AffineInverse();
        void Walk(Node node)
        {
            if(node is BoneAttachment3D) return;
            if(node is MeshInstance3D {Mesh:not null} mesh && mesh.IsVisibleInTree() && node is not FxMesh)
            {
                Aabb box=default;bool found=false;
                foreach(var point in FramingPoints(mesh))
                {
                    var local=inverse*point;
                    if(!found) {box=new Aabb(local,Vector3.Zero);found=true;}
                    else box=box.Expand(local);
                }
                if(found) for(int i=0;i<8;i++) points.Add(box.GetEndpoint(i));
            }
            foreach(var child in node.GetChildren()) Walk(child);
        }
        Walk(model);
        return points.ToArray();
    }
    public static IEnumerable<Vector3> FramingPoints(MeshInstance3D mesh)
    {
        // Fit the posed skin once; bind-pose boxes include outstretched arms and oversized rig bounds.
        var skin=mesh.Skin;
        var skeleton=mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
        if(skin==null || skeleton==null)
        {
            for(int i=0;i<8;i++) yield return mesh.GlobalTransform*mesh.GetAabb().GetEndpoint(i);
            yield break;
        }
        var transforms=new Transform3D[skin.GetBindCount()];
        for(int bind=0;bind<transforms.Length;bind++)
        {
            var name=skin.GetBindName(bind);
            int bone=name.IsEmpty?skin.GetBindBone(bind):skeleton.FindBone(name);
            transforms[bind]=bone>=0?skeleton.GlobalTransform*skeleton.GetBoneGlobalPose(bone)*skin.GetBindPose(bind):mesh.GlobalTransform;
        }
        for(int surface=0;surface<mesh.Mesh!.GetSurfaceCount();surface++)
        {
            using var arrays=mesh.Mesh.SurfaceGetArrays(surface);
            var positions=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            int influences=positions.Length>0?bones.Length/positions.Length:0;
            for(int i=0;i<positions.Length;i++)
            {
                var point=Vector3.Zero;float total=0;
                for(int j=0;j<influences;j++)
                {
                    int at=i*influences+j,bind=bones[at];float weight=weights[at];
                    if(weight<=0 || bind>=transforms.Length) continue;
                    point+=(transforms[bind]*positions[i])*weight;total+=weight;
                }
                yield return total>0?point:mesh.GlobalTransform*positions[i];
            }
        }
    }
    public override void _GuiInput(InputEvent ev)
    {
        if(ev is InputEventMouseButton { ButtonIndex:MouseButton.Left } button) { _rotating=button.Pressed; AcceptEvent(); }
        if(ev is InputEventMouseMotion motion && _rotating && _model!=null) { _model.RotateY(motion.Relative.X*0.012f); AcceptEvent(); }
    }
}
