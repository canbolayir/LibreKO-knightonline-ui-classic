using System.Collections;
using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Classic framing for the client's <see cref="LookPreview"/>: the camera is fitted to the posed model
/// for the complete orbit instead of scaling the model, the preview clips its viewport, a replaced model
/// leaves the scene in the same frame, and the cape service shows a rear view framed on the body.
/// </summary>
public static class LookFraming
{
    private const float CameraDistance = 4f;
    private const float CameraHeight = 1.08f;
    private const float CameraAim = 1.08f;
    private const float KurianHeight = 1.35f;
    private const float KurianDistance = 5.6f;
    private const float FramingMargin = 12f;
    private const float HeadRoom = .03f;
    private static readonly int[] KurianRaces = { 6, 14 };

    private sealed class Framing
    {
        public LookPreview Preview = null!;
        public Camera3D Camera = null!;
        public Node3D Pivot = null!;
        public Func<int> Race = () => 0;
        public float AimHeight = CameraAim, MinimumDistance = CameraDistance;
        public bool Cape;
        /// <summary>The model this plugin built and framed itself; it may enter the tree later than it was added.</summary>
        public Node3D? Expected;
    }

    private static readonly ConditionalWeakTable<LookPreview, Framing> _framings = new();

    /// <summary>Takes over the camera of a native preview; <paramref name="race"/> names the shown body.</summary>
    public static void Attach(LookPreview preview, Func<int> race)
    {
        if (_framings.TryGetValue(preview, out _)) return;
        if (Native.Get<Node3D>(preview, "_pivot") is not { } pivot || preview.GetChildCount() == 0
            || preview.GetChild(0) is not SubViewport viewport || viewport.GetChildren().OfType<Camera3D>().FirstOrDefault() is not { } camera) return;
        var framing = new Framing { Preview = preview, Camera = camera, Pivot = pivot, Race = race };
        _framings.AddOrUpdate(preview, framing);
        preview.ClipContents = true;
        Place(framing, CameraHeight, CameraDistance, CameraAim);
        pivot.ChildEnteredTree += node => Entered(framing, node);
        NativeLookEditor.Entering(pivot);
        if (Model(preview) is { } model && model.IsInsideTree()) Entered(framing, model);
    }

    public static Node3D? Model(LookPreview preview) => Native.Get<Node3D>(preview, "_model");

    /// <summary>Builds a model with equipment; the native preview only shows a bare body.</summary>
    public static void Show(LookPreview preview, int race, int face, int hairStyle, Color hairColour, int[] gear)
    {
        if (!_framings.TryGetValue(preview, out var framing)) return;
        Clear(preview);
        framing.Cape = false;
        Default(framing, race);
        var model = CharacterPreview.Build(race, face, gear, hairStyle, hairColour);
        if (model == null) return;
        Native.Set(preview, "_model", model);
        framing.Expected = model;
        framing.Pivot.AddChild(model);
    }

    /// <summary>The cape service view: the back of the character, framed on its body without the cape.</summary>
    public static void ShowCape(LookPreview preview, int race, int face, int hairStyle, Color hairColour, int[] gear)
    {
        if (!_framings.TryGetValue(preview, out var framing)) return;
        Show(preview, race, face, hairStyle, hairColour, gear);
        framing.Cape = true;
        framing.MinimumDistance = 0;
        framing.Pivot.Rotation = new Vector3(0, Mathf.Pi, 0);
    }

    public static void SetCape(LookPreview preview, int capeId, Color dye, int race)
    {
        if (Model(preview) is not { } model) return;
        var cape = model.GetNodeOrNull<Cape>("Cape");
        if (!Cape.IsRenderable(capeId)) { cape?.QueueFree(); return; }
        if (cape is { } existing && !existing.IsQueuedForDeletion()) existing.SetCape(capeId, dye);
        else
        {
            if (cape != null) model.RemoveChild(cape);
            Cape.Attach(model, capeId, dye, race, highDetail: true);
        }
    }

    /// <summary>Removes the model at once, so a replacement in the same frame never shares the preview.</summary>
    public static void Clear(LookPreview preview)
    {
        if (Model(preview) is { } model)
        {
            model.GetParent()?.RemoveChild(model);
            model.QueueFree();
        }
        Native.Set(preview, "_model", null);
    }

    private static void Entered(Framing framing, Node node)
    {
        NativeLookEditor.DropReplaced(framing.Pivot, node);
        if (node != framing.Expected)
        {
            framing.Cape = false;
            Default(framing, framing.Race());
        }
        Callable.From(() => Fit(framing)).CallDeferred();
    }

    private static void Default(Framing framing, int race)
    {
        bool kurian = KurianRaces.Contains(race);
        Place(framing, kurian ? KurianHeight : CameraHeight, kurian ? KurianDistance : CameraDistance, kurian ? KurianHeight : CameraAim);
    }

    private static void Place(Framing framing, float height, float distance, float aim)
    {
        var eye = new Vector3(0, height, distance);
        var target = new Vector3(0, aim, 0);
        framing.Camera.Transform = new Transform3D(Basis.LookingAt(target - eye, Vector3.Up), eye);
        framing.AimHeight = aim;
        framing.MinimumDistance = distance;
    }

    private static void Fit(Framing framing)
    {
        if (!GodotObject.IsInstanceValid(framing.Preview) || Model(framing.Preview) is not { } model || !model.IsInsideTree()) return;
        var pivot = framing.Pivot.GlobalPosition;
        if (framing.Cape)
        {
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            void Height(Node node)
            {
                if (node is Cape) return;
                if (node is MeshInstance3D { Mesh: not null } mesh && mesh.IsVisibleInTree())
                    foreach (var point in PosedPoints(mesh))
                    { float y = point.Y - pivot.Y; low = Mathf.Min(low, y); high = Mathf.Max(high, y); }
                foreach (var child in node.GetChildren()) Height(child);
            }
            Height(model);
            if (float.IsFinite(low) && float.IsFinite(high)) framing.AimHeight = (low + high) * .5f;
        }
        float tangent = Mathf.Tan(Mathf.DegToRad(framing.Camera.Fov * .5f));
        var size = framing.Preview.GetChild<SubViewport>(0).Size;
        float horizontal = 1 / (tangent * size.X / size.Y * (1 - FramingMargin / size.X));
        float vertical = 1 / (tangent * (1 - FramingMargin / size.Y));
        float distance = framing.MinimumDistance;
        void Visit(Node node)
        {
            if (framing.Cape && node is Cape) return;
            if (node is MeshInstance3D { Mesh: not null } mesh && mesh.IsVisibleInTree())
                foreach (var point in PosedPoints(mesh))
                {
                    var local = point - pivot;
                    float radius = new Vector2(local.X, local.Z).Length();
                    // Bound the complete orbit once, so turning the model never changes the zoom.
                    distance = Mathf.Max(distance, radius * Mathf.Sqrt(1 + horizontal * horizontal));
                    distance = Mathf.Max(distance, radius + (Mathf.Abs(local.Y - framing.AimHeight) + HeadRoom) * vertical);
                }
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(model);
        var eye = new Vector3(0, framing.AimHeight, distance);
        framing.Camera.Transform = new Transform3D(Basis.LookingAt(new Vector3(0, framing.AimHeight, 0) - eye, Vector3.Up), eye);
    }

    /// <summary>World-space vertices of the displayed skin; bind-pose boxes overstate a posed character.</summary>
    public static IEnumerable<Vector3> PosedPoints(MeshInstance3D mesh)
    {
        var skin = mesh.Skin;
        var skeleton = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
        if (skin == null || skeleton == null)
        {
            for (int i = 0; i < 8; i++) yield return mesh.GlobalTransform * mesh.GetAabb().GetEndpoint(i);
            yield break;
        }
        var transforms = new Transform3D[skin.GetBindCount()];
        for (int bind = 0; bind < transforms.Length; bind++)
        {
            var name = skin.GetBindName(bind);
            int bone = name.IsEmpty ? skin.GetBindBone(bind) : skeleton.FindBone(name);
            transforms[bind] = bone >= 0 ? skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone) * skin.GetBindPose(bind) : mesh.GlobalTransform;
        }
        for (int surface = 0; surface < mesh.Mesh!.GetSurfaceCount(); surface++)
        {
            using var arrays = mesh.Mesh.SurfaceGetArrays(surface);
            var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            int influences = positions.Length > 0 ? bones.Length / positions.Length : 0;
            for (int i = 0; i < positions.Length; i++)
            {
                var point = Vector3.Zero; float total = 0;
                for (int j = 0; j < influences; j++)
                {
                    int at = i * influences + j, bind = bones[at]; float weight = weights[at];
                    if (weight <= 0 || bind < 0 || bind >= transforms.Length) continue;
                    point += (transforms[bind] * positions[i]) * weight; total += weight;
                }
                yield return total > 0 ? point : mesh.GlobalTransform * positions[i];
            }
        }
    }
}

/// <summary>
/// Classic additions to the client's <see cref="LookEditor"/>: stable control names, 1-based face and hair
/// numbers ("-" when the race has none), a colour control that can be marked unavailable, Escape closing
/// only the colour popup, and race rows that leave the list in the same frame they are replaced.
/// </summary>
public static class NativeLookEditor
{
    private sealed class Adapter
    {
        public LookEditor Editor = null!;
        public Label Face = null!, Hair = null!;
        public ColorPickerButton Colour = null!;
        public VBoxContainer Races = null!;
        public Func<bool>? ColourAvailable;
    }

    private static readonly ConditionalWeakTable<LookEditor, Adapter> _adapters = new();

    public static void Attach(LookEditor editor, Func<bool>? colourAvailable = null)
    {
        if (_adapters.TryGetValue(editor, out _)) return;
        var races = Native.Get<VBoxContainer>(editor, "_races");
        var face = Native.Get<Label>(editor, "_faceLbl");
        var hair = Native.Get<Label>(editor, "_hairLbl");
        var colour = Native.Get<ColorPickerButton>(editor, "_colour");
        if (races == null || face == null || hair == null || colour == null) return;
        var adapter = new Adapter { Editor = editor, Face = face, Hair = hair, Colour = colour, Races = races, ColourAvailable = colourAvailable };
        _adapters.AddOrUpdate(editor, adapter);

        races.Name = "look_races";
        foreach (var title in editor.GetChildren().OfType<Label>())
            title.Name = title.Text switch { "Race" => "look_race_heading", "Appearance" => "look_appearance_heading", _ => title.Name };
        foreach (var (part, value, field) in new[] { ("face", face, "_faceSteps"), ("hair", hair, "_hairSteps") })
        {
            string id = "look_" + part;
            value.Name = id + "_value";
            value.GetParent().Name = id + "_row";
            var steps = Native.Get<Button[]>(editor, field) ?? value.GetParent().GetChildren().OfType<Button>().ToArray();
            if (steps.Length == 2) { steps[0].Name = id + "_previous"; steps[1].Name = id + "_next"; }
        }
        colour.Name = "look_colour";
        colour.GetParent().Name = "look_colour_row";
        colour.GetPopup().AddChild(new LookColourPopupInput(colour.GetPopup()));

        // The editor writes 0-based numbers into these; the visible labels show the original 1-based ones.
        Native.Set(editor, "_faceLbl", new Label());
        Native.Set(editor, "_hairLbl", new Label());
        races.ChildEnteredTree += node => RaceAdded(adapter, node);
        Entering(races);
        NameRaces(adapter);
        editor.Changed += () => Refresh(editor);
        editor.AddChild(new NativeProcess(() => Refresh(editor)) { Name = "look_editor_sync" });
        Refresh(editor);
    }

    /// <summary>Refreshes the Classic numbering and colour availability after the editor changed.</summary>
    public static void Refresh(LookEditor editor)
    {
        if (!_adapters.TryGetValue(editor, out var adapter)) return;
        int faces = CharacterPreview.FaceCount(editor.Race), hairs = CharacterPreview.HairCount(editor.Race);
        adapter.Face.Text = faces == 0 ? "-" : (editor.Face + 1).ToString();
        adapter.Hair.Text = hairs == 0 ? "-" : (editor.HairStyle + 1).ToString();
        bool available = adapter.ColourAvailable?.Invoke() ?? true;
        if (!available)
        {
            if (adapter.Colour.GetPopup().Visible) adapter.Colour.GetPopup().Hide();
            adapter.Colour.Disabled = true;
        }
        adapter.Colour.Modulate = available ? Colors.White : new Color(.45f, .45f, .45f, 1);
    }

    public static void SetLocked(LookEditor editor, bool locked)
    {
        if (!Native.TryCall(editor, "SetLocked", out _, locked)) foreach (var button in Native.Descendants(editor).OfType<BaseButton>()) button.Disabled = locked;
        if (locked) CloseColourPicker(editor);
        Refresh(editor);
    }

    public static bool Locked(LookEditor editor) => Native.Get<bool>(editor, "_locked");

    public static void CloseColourPicker(LookEditor editor)
    {
        if (Native.Get<ColorPickerButton>(editor, "_colour") is { } colour) colour.GetPopup().Hide();
    }

    private static void RaceAdded(Adapter adapter, Node node)
    {
        DropReplaced(adapter.Races, node);
        Callable.From(() => NameRaces(adapter)).CallDeferred();
    }

    /// <summary>
    /// Takes children that are already queued for deletion out of <paramref name="parent"/> when a replacement
    /// enters it. While the parent itself is entering the tree it cannot change its children, so that case
    /// waits until the end of the frame.
    /// </summary>
    internal static void DropReplaced(Node parent, Node added)
    {
        void Drop()
        {
            if (!GodotObject.IsInstanceValid(parent)) return;
            foreach (var child in parent.GetChildren())
                if (child != added && child.IsQueuedForDeletion()) parent.RemoveChild(child);
        }
        if (parent.IsNodeReady() && !Entering(parent)) Drop(); else Callable.From(Drop).CallDeferred();
    }

    private sealed class EnterState { public bool Entering; }
    private static readonly ConditionalWeakTable<Node, EnterState> _entering = new();

    /// <summary>Whether the parent is re-entering the tree, when its children enter one by one and it cannot change them.</summary>
    internal static bool Entering(Node parent)
    {
        if (_entering.TryGetValue(parent, out var state)) return state.Entering;
        state = new EnterState();
        _entering.AddOrUpdate(parent, state);
        parent.TreeEntered += () =>
        {
            state.Entering = true;
            Callable.From(() => state.Entering = false).CallDeferred();
        };
        return false;
    }

    private static void NameRaces(Adapter adapter)
    {
        if (!GodotObject.IsInstanceValid(adapter.Editor) || Native.Get<IList>(adapter.Editor, "_raceButtons") is not { } buttons) return;
        foreach (var entry in buttons)
            if (entry is ITuple { Length: 2 } pair && pair[0] is int race && pair[1] is Button button && GodotObject.IsInstanceValid(button))
                button.Name = "look_race_" + race;
    }
}

/// <summary>Escape closes the hair colour popup without reaching the window behind it.</summary>
public partial class LookColourPopupInput : Node
{
    private readonly PopupPanel? _popup;

    public LookColourPopupInput() { }

    public LookColourPopupInput(PopupPanel popup) => _popup = popup;

    public override void _Input(InputEvent ev)
    {
        if (_popup == null || !_popup.Visible || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        _popup.Hide();
        GetViewport().SetInputAsHandled();
    }
}

/// <summary>Runs an action every frame before the Classic skins draw.</summary>
public partial class NativeProcess : Node
{
    private readonly Action? _process;

    public NativeProcess() { }

    public NativeProcess(Action process)
    {
        _process = process;
        ProcessPriority = int.MinValue;
    }

    public override void _Process(double delta) => _process?.Invoke();
}
