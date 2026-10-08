using Godot;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic;

/// <summary>Render an appearance once, then retain a bounded cache of ordinary 2D textures.</summary>
public static class NpcPortraitCache
{
    public const int Resolution=256;
    public const int Capacity=64;
    private sealed class Entry
    {
        public required Task<Texture2D?> Texture;
        public long Access;
    }
    private static readonly Dictionary<string,Entry> Entries=new();
    private static readonly System.Threading.SemaphoreSlim CaptureGate=new(1,1);
    private static Node? _owner;
    private static long _access;
    private static int _generation;
    public static int RenderRequests { get; private set; }
    public static int CacheHits { get; private set; }
    public static int EntryCount => Entries.Count;
    public static int Evictions { get; private set; }
    public static long CachedTextureBytes => Entries.Values.Where(e=>e.Texture.IsCompletedSuccessfully && e.Texture.Result!=null)
        .Sum(e=>(long)e.Texture.Result!.GetWidth()*e.Texture.Result.GetHeight()*4);
    public static int RetainedViewportCount => _owner?.GetChildren().OfType<SubViewport>().Count()??0;
    public static int ActiveRenderers => _owner?.GetChildren().OfType<SubViewport>().Count(v=>v.RenderTargetUpdateMode!=SubViewport.UpdateMode.Disabled)??0;
    public static int SceneNodeCount => _owner?.GetChildren().OfType<SubViewport>().Sum(v=>v.GetChildren().Count)??0;
    public static double LastCaptureMilliseconds { get; private set; }
    public static double LastModelBuildMilliseconds { get; private set; }
    public static double LastRenderWaitMilliseconds { get; private set; }
    public static string LastPose { get; private set; }="";
    public static Vector3 LastTarget { get; private set; }
    public static string LastFraming { get; private set; }="";
    public static Task<Texture2D?> Get(Control requester,GameNpcPortrait npc)
    {
        string key="bust-v2:"+npc.AppearanceKey;
        if(Entries.TryGetValue(key,out var entry)) { entry.Access=++_access;CacheHits++;return entry.Texture; }
        if(_owner==null || !GodotObject.IsInstanceValid(_owner))
        {
            _owner=new Node { Name="classic_npc_portrait_cache",ProcessMode=Node.ProcessModeEnum.Disabled };
            requester.GetTree().Root.AddChild(_owner);
        }
        var capture=Capture(npc,_generation);Entries.Add(key,new Entry { Texture=capture,Access=++_access });
        Trim();return capture;
    }
    private static void Trim()
    {
        while(Entries.Count>Capacity)
        {
            var oldest=Entries.Where(e=>e.Value.Texture.IsCompleted).OrderBy(e=>e.Value.Access).FirstOrDefault();
            if(oldest.Key==null) break;
            Entries.Remove(oldest.Key);Evictions++;
        }
    }
    private static async Task<Texture2D?> Capture(GameNpcPortrait npc,int generation)
    {
        await CaptureGate.WaitAsync();
        ulong started=Time.GetTicksUsec();
        SubViewport? viewport=null;
        Node3D? model=null;
        try
        {
            if(generation!=_generation) return null;
            model=npc.BuildModel();
            LastModelBuildMilliseconds=(Time.GetTicksUsec()-started)/1000.0;
            if(model==null) return null;
            viewport=new SubViewport { Name="portrait",Size=new Vector2I(Resolution,Resolution),
                OwnWorld3D=true,TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled,
                Msaa3D=Viewport.Msaa.Msaa4X,RenderTargetClearMode=SubViewport.ClearMode.Always };
            _owner!.AddChild(viewport);
            var stage=new Node3D { ProcessMode=Node.ProcessModeEnum.Disabled };viewport.AddChild(stage);stage.AddChild(model);
            var camera=new Camera3D { Current=true,Projection=Camera3D.ProjectionType.Orthogonal,Near=.01f,Far=100 };
            stage.AddChild(camera);
            stage.AddChild(new WorldEnvironment { Environment=new Godot.Environment { BackgroundMode=Godot.Environment.BGMode.ClearColor,
                AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("e3e7ed"),AmbientLightEnergy=.65f,
                TonemapMode=Godot.Environment.ToneMapper.Linear } });
            stage.AddChild(new DirectionalLight3D { RotationDegrees=new Vector3(-25,-30,0),LightEnergy=1.1f,ShadowEnabled=false });
            FreezePose(model);
            var meshes=Tree(model).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null && !IsEquipment(m,model)).ToArray();
            if(meshes.Length==0) { viewport.QueueFree();return null; }
            var bounds=meshes.Select(PosedBounds).Aggregate((a,b)=>a.Merge(b));
            float height=bounds.Size.Y;
            if(height<=.001f) { viewport.QueueFree();return null; }
            // Unnamed familiar rigs may put ears, wings or weapons above the face. Preserve the existing upright NPC bust fallback.
            bool uprightNpc=!npc.AppearanceKey.StartsWith("familiar:",StringComparison.Ordinal)
                && height>Math.Max(bounds.Size.X,bounds.Size.Z)*.6f;
            LastTarget=uprightNpc?new Vector3(bounds.GetCenter().X,bounds.Position.Y+height*.85f,bounds.GetCenter().Z):bounds.GetCenter();
            camera.Size=uprightNpc?height*.36f:Math.Max(height,bounds.Size.X)*1.15f;
            LastFraming=uprightNpc?"upper posed body bounds":"whole posed model bounds";
            if(HeadBounds(model) is {} head && head.End.Y>bounds.Position.Y+height*.35f)
            {
                float bodyHeight=head.End.Y-bounds.Position.Y;
                camera.Size=Math.Max(head.Size.Y*1.7f,bodyHeight*.36f);
                LastTarget=head.GetCenter()-Vector3.Up*head.Size.Y*.08f;LastFraming="posed head geometry";
            }
            camera.LookAtFromPosition(LastTarget+new Vector3(height*.025f,height*.015f,height*2),LastTarget,Vector3.Up);
            foreach(var geometry in Tree(model).OfType<GeometryInstance3D>())
            { geometry.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;geometry.VisibilityRangeEnd=0; }
            RenderRequests++;
            ulong renderStarted=Time.GetTicksUsec();
            viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
            await viewport.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(generation!=_generation || !GodotObject.IsInstanceValid(viewport)) return null;
            LastRenderWaitMilliseconds=(Time.GetTicksUsec()-renderStarted)/1000.0;
            viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;
            using var image=viewport.GetTexture().GetImage();
            Texture2D texture=ImageTexture.CreateFromImage(image);
            // The cached portrait owns no viewport, world, meshes, skeletons or animations.
            viewport.Free();viewport=null;
            LastCaptureMilliseconds=(Time.GetTicksUsec()-started)/1000.0;
            return texture;
        }
        catch(Exception error)
        {
            if(viewport!=null && GodotObject.IsInstanceValid(viewport)) viewport.QueueFree();
            else if(model!=null && GodotObject.IsInstanceValid(model)) model.Free();
            Godot.GD.PushWarning("NPC portrait capture failed: "+error.Message);return null;
        }
        finally { CaptureGate.Release();Trim(); }
    }
    private static bool IsEquipment(Node node,Node root)
    {
        for(Node? parent=node;parent!=null && parent!=root;parent=parent.GetParent())
            if(parent.Name.ToString().StartsWith("weapon_",StringComparison.Ordinal)) return true;
        return false;
    }
    private static Aabb PosedBounds(MeshInstance3D mesh)
    {
        if(mesh.Skin==null || mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) is not {} skeleton)
            return mesh.GlobalTransform*mesh.GetAabb();
        var skin=mesh.Skin;var transforms=new Transform3D[skin.GetBindCount()];var valid=new bool[skin.GetBindCount()];
        for(int bind=0;bind<skin.GetBindCount();bind++)
        {
            int bone=skin.GetBindBone(bind);
            if(bone<0) bone=skeleton.FindBone(skin.GetBindName(bind));
            if(bone<0 || bone>=skeleton.GetBoneCount()) continue;
            valid[bind]=true;transforms[bind]=skeleton.GlobalTransform*skeleton.GetBoneGlobalPose(bone)*skin.GetBindPose(bind);
        }
        Aabb? bounds=null;
        for(int surface=0;surface<mesh.Mesh!.GetSurfaceCount();surface++)
        {
            var arrays=mesh.Mesh.SurfaceGetArrays(surface);
            if(arrays[(int)Mesh.ArrayType.Bones].VariantType==Variant.Type.Nil || arrays[(int)Mesh.ArrayType.Weights].VariantType==Variant.Type.Nil) continue;
            var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            if(vertices.Length==0 || bones.Length!=weights.Length || bones.Length%vertices.Length!=0) continue;
            int stride=bones.Length/vertices.Length;
            for(int vertex=0;vertex<vertices.Length;vertex++)
            {
                var point=Vector3.Zero;float total=0;
                for(int weight=0;weight<stride;weight++)
                {
                    int index=vertex*stride+weight,bind=bones[index];
                    if(bind<0 || bind>=transforms.Length || !valid[bind] || weights[index]<=0) continue;
                    point+=(transforms[bind]*vertices[vertex])*weights[index];total+=weights[index];
                }
                if(total<=.001f) continue;
                point/=total;bounds=bounds.HasValue?bounds.Value.Expand(point):new Aabb(point,Vector3.Zero);
            }
        }
        return bounds??mesh.GlobalTransform*mesh.GetAabb();
    }
    private static Aabb? HeadBounds(Node model)
    {
        Aabb? result=null;
        foreach(var skeleton in Tree(model).OfType<Skeleton3D>())
        {
            int head=-1;
            for(int i=0;i<skeleton.GetBoneCount();i++)
            {
                string name=skeleton.GetBoneName(i).Replace(" ","").Replace("_","").ToLowerInvariant();
                if(name is "head" or "head01" or "bip01head") { head=i;break; }
            }
            if(head<0) continue;
            foreach(var mesh in Tree(model).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null && m.Skin!=null && !IsEquipment(m,model)))
            {
                if(mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton)!=skeleton) continue;
                var skin=mesh.Skin!;var headBinds=new bool[skin.GetBindCount()];var transforms=new Transform3D[skin.GetBindCount()];
                for(int bind=0;bind<skin.GetBindCount();bind++)
                {
                    int bone=skin.GetBindBone(bind);
                    if(bone<0) bone=skeleton.FindBone(skin.GetBindName(bind));
                    if(bone<0) continue;
                    for(int parent=bone;parent>=0;parent=skeleton.GetBoneParent(parent))
                        if(parent==head) { headBinds[bind]=true;break; }
                    transforms[bind]=skeleton.GlobalTransform*skeleton.GetBoneGlobalPose(bone)*skin.GetBindPose(bind);
                }
                for(int surface=0;surface<mesh.Mesh!.GetSurfaceCount();surface++)
                {
                    var arrays=mesh.Mesh.SurfaceGetArrays(surface);
                    if(arrays[(int)Mesh.ArrayType.Bones].VariantType==Variant.Type.Nil || arrays[(int)Mesh.ArrayType.Weights].VariantType==Variant.Type.Nil) continue;
                    var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    if(vertices.Length==0 || bones.Length!=weights.Length) continue;
                    int stride=bones.Length/vertices.Length;
                    for(int vertex=0;vertex<vertices.Length;vertex++)
                    {
                        float headWeight=0;
                        for(int weight=0;weight<stride;weight++)
                        {
                            int index=vertex*stride+weight,bind=bones[index];
                            if(bind>=0 && bind<headBinds.Length && headBinds[bind]) headWeight+=weights[index];
                        }
                        if(headWeight<.5f) continue;
                        var point=Vector3.Zero;
                        for(int weight=0;weight<stride;weight++)
                        {
                            int index=vertex*stride+weight,bind=bones[index];
                            if(bind>=0 && bind<transforms.Length) point+=(transforms[bind]*vertices[vertex])*weights[index];
                        }
                        result=result.HasValue?result.Value.Expand(point):new Aabb(point,Vector3.Zero);
                    }
                }
            }
        }
        return result;
    }
    private static void FreezePose(Node3D model)
    {
        foreach(var animation in Tree(model).OfType<AnimationPlayer>())
        {
            animation.Stop();
            string[] candidates={"basic","breath","base","stand","wait","idle"};
            string? clip=animation.GetAnimationList().FirstOrDefault(name=>candidates.Any(c=>name.Equals(c,StringComparison.OrdinalIgnoreCase)));
            clip??=animation.GetAnimationList().FirstOrDefault(name=>name!="RESET");
            if(clip!=null) { animation.Play(clip);animation.Seek(Math.Min(.1,animation.GetAnimation(clip).Length),true);animation.Pause();LastPose=clip; }
            animation.ProcessMode=Node.ProcessModeEnum.Disabled;
        }
    }
    private static IEnumerable<Node> Tree(Node node)
    {
        yield return node;
        foreach(var child in node.GetChildren()) foreach(var item in Tree(child)) yield return item;
    }
    public static void Clear()
    {
        Entries.Clear();
        _generation++;
        if(_owner!=null && GodotObject.IsInstanceValid(_owner)) _owner.QueueFree();
        _owner=null;RenderRequests=CacheHits=Evictions=0;_access=0;
        LastCaptureMilliseconds=LastModelBuildMilliseconds=LastRenderWaitMilliseconds=0;LastPose="";LastTarget=Vector3.Zero;
        LastFraming="";
    }
}
