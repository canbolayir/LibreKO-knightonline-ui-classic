using System.Runtime.CompilerServices;
using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Attaches the client's window layout behaviour to a plugin window and adds the Classic resize grip
/// and edge snapping on top of it.
/// </summary>
public static partial class NativeLayout
{
    /// <summary>Distance at which a resized edge joins the matching edge of its snap peer.</summary>
    public const float ResizeSnapDistance = 12f;

    private const string ContentOpenAnchor = "content_open_anchor";
    private const string AnchorKept = "native_content_anchor";

    private static readonly List<Snap> _snaps = new();
    private static readonly ConditionalWeakTable<HudLayout, Snap> _byLayout = new();
    private static bool _watchingWindows;

    public static HudLayout Attach(
        Control target,
        string id,
        Control? dragHandle,
        Func<Vector2>? defaultPosition,
        bool resizable = false,
        Vector2 defaultSize = default,
        Vector2 minimumSize = default,
        bool persist = true,
        HudLayout.Corner resizeCorner = HudLayout.Corner.BottomRight,
        HudLayout.Corner moveCorner = HudLayout.Corner.TopLeft,
        bool moveGripAlwaysVisible = false,
        Vector2 resizeGripOffset = default,
        Action<float>? backgroundOpacityChanged = null,
        bool legacyResizeGrip = false,
        string? resizeSnapPeerId = null)
    {
        var layout = HudLayout.Attach(target, id, dragHandle, defaultPosition, resizable, defaultSize, minimumSize, persist,
            resizeCorner, moveCorner, moveGripAlwaysVisible, resizeGripOffset, backgroundOpacityChanged);
        if (resizable && legacyResizeGrip) layout.AddChild(new LegacyGripInstaller(layout, resizeCorner) { Name = "legacy_resize_grip" });
        if (resizeSnapPeerId != null)
        {
            var snap = new Snap(layout, target, id, resizeSnapPeerId, resizeCorner is HudLayout.Corner.TopLeft or HudLayout.Corner.BottomLeft);
            _byLayout.AddOrUpdate(layout, snap);
            layout.AddChild(snap);
        }
        return layout;
    }

    /// <summary>The window currently joined to <paramref name="layout"/> along a resize seam, if any.</summary>
    public static HudLayout? ResizePartner(HudLayout layout) =>
        _byLayout.TryGetValue(layout, out var snap) ? snap.Partner?.Layout : null;

    /// <summary>
    /// Makes every <see cref="HudWindow"/> keep its position while it carries the <c>content_open_anchor</c>
    /// meta: the client's default placement and settling recentre then leave the window where it was opened.
    /// </summary>
    public static void WatchContentAnchors()
    {
        if (_watchingWindows || Engine.GetMainLoop() is not SceneTree tree) return;
        _watchingWindows = true;
        tree.NodeAdded += node => { if (node is HudWindow window) KeepContentAnchor(window); };
    }

    public static void KeepContentAnchor(HudWindow window)
    {
        var layout = window.Layout;
        if (!GodotObject.IsInstanceValid(layout) || layout.HasMeta(AnchorKept)) return;
        if (Native.Get<Func<Vector2>>(layout, "_defaultPosition") is not { } placement) return;
        if (Native.Set(layout, "_defaultPosition", (Func<Vector2>)(() => window.HasMeta(ContentOpenAnchor) ? window.Position : placement())))
            layout.SetMeta(AnchorKept, true);
    }

    /// <summary>Replaces the client's gold resize corner with the original grey three-stroke grip.</summary>
    private sealed partial class LegacyGripInstaller : Node
    {
        private static readonly Vector2 GripSize = new(20, 20);
        private readonly HudLayout _layout;
        private readonly HudLayout.Corner _corner;

        public LegacyGripInstaller() { _layout = null!; }

        public LegacyGripInstaller(HudLayout layout, HudLayout.Corner corner)
        {
            _layout = layout;
            _corner = corner;
        }

        public override void _Process(double delta)
        {
            if (Native.Get<Control>(_layout, "_corner") is not { } corner) return;
            SetProcess(false);
            corner.SelfModulate = Colors.Transparent;
            corner.CustomMinimumSize = GripSize;
            corner.Size = GripSize;
            corner.AddChild(new LegacyGrip(_corner) { Size = GripSize, MouseFilter = Control.MouseFilterEnum.Ignore });
        }
    }

    private sealed partial class LegacyGrip : Control
    {
        private static readonly Color Stroke = new(0.78f, 0.80f, 0.82f, 0.90f);
        private readonly HudLayout.Corner _placement;

        public LegacyGrip() { }

        public LegacyGrip(HudLayout.Corner placement) => _placement = placement;

        public override void _Draw()
        {
            Vector2 Mirror(Vector2 p) => new(
                _placement is HudLayout.Corner.TopRight or HudLayout.Corner.BottomRight ? p.X : Size.X - p.X,
                _placement is HudLayout.Corner.BottomLeft or HudLayout.Corner.BottomRight ? p.Y : Size.Y - p.Y);
            DrawLine(Mirror(new Vector2(7, 18)), Mirror(new Vector2(18, 7)), Stroke, 2);
            DrawLine(Mirror(new Vector2(12, 18)), Mirror(new Vector2(18, 12)), Stroke, 2);
            DrawLine(Mirror(new Vector2(17, 18)), Mirror(new Vector2(18, 17)), Stroke, 2);
        }
    }

    /// <summary>
    /// Resize-seam snapping between two windows: a resized edge that comes within
    /// <see cref="ResizeSnapDistance"/> of the peer's opposite edge joins it, and while joined both windows
    /// share the seam, the bottom edge and the height. Dragging either window detaches the pair.
    /// </summary>
    private sealed partial class Snap : Node
    {
        public readonly HudLayout Layout;
        private readonly Control _target;
        private readonly string _id, _peerId;
        private readonly bool _fromLeft;
        public Snap? Partner;
        private Rect2 _partnerOrigin;
        private (Vector2, Vector2, Vector2)? _session;
        private bool _applying;

        public Snap() { Layout = null!; _target = null!; _id = _peerId = ""; }

        public Snap(HudLayout layout, Control target, string id, string peerId, bool fromLeft)
        {
            Layout = layout;
            _target = target;
            _id = id;
            _peerId = peerId;
            _fromLeft = fromLeft;
            ProcessPriority = -1;
        }

        public override void _Ready()
        {
            _snaps.Add(this);
            _target.Resized += OnResized;
            _target.ItemRectChanged += OnMoved;
        }

        public override void _ExitTree()
        {
            Detach();
            _snaps.Remove(this);
            if (GodotObject.IsInstanceValid(_target))
            {
                _target.Resized -= OnResized;
                _target.ItemRectChanged -= OnMoved;
            }
        }

        private bool Resizing => Native.Get<bool>(Layout, "_resizing");

        private bool Frozen => Native.Get<bool>(Layout, "Frozen");

        private Vector2 MinimumSize => Native.Get<Vector2>(Layout, "MinimumSize");

        private bool Available(Snap peer) => GodotObject.IsInstanceValid(peer) && GodotObject.IsInstanceValid(peer._target)
            && peer._target.IsVisibleInTree() && !peer.Frozen && peer.GetViewport() == GetViewport();

        private void Detach()
        {
            if (Partner == null) return;
            if (GodotObject.IsInstanceValid(Partner)) Partner.Partner = null;
            Partner = null;
        }

        private void OnMoved()
        {
            if (Partner != null && Native.Get<bool>(Layout, "_dragging") && Native.Get<bool>(Layout, "_dragMoved")) Detach();
        }

        public override void _Process(double delta)
        {
            if (_session == null || Resizing) return;
            _session = null;
            if (Partner == null || !GodotObject.IsInstanceValid(Partner.Layout)) return;
            var partner = Partner._target;
            Native.Call(Partner.Layout, "Place", partner.Position, partner.Size);
        }

        private void OnResized()
        {
            if (_applying || !Resizing) return;
            var key = (Native.Get<Vector2>(Layout, "_resizeOriginMouse"), Native.Get<Vector2>(Layout, "_resizeOriginPosition"),
                Native.Get<Vector2>(Layout, "_resizeOriginSize"));
            if (_session != key)
            {
                _session = key;
                if (Partner != null)
                {
                    if (!Available(Partner)) Detach();
                    else _partnerOrigin = Partner._target.GetGlobalRect();
                }
            }
            _applying = true;
            try { SnapAndResizePartner(); }
            finally { _applying = false; }
        }

        private void SnapAndResizePartner()
        {
            if (Partner != null && !Available(Partner)) Detach();
            bool joined = false;
            if (Partner == null)
            {
                var rect = _target.GetGlobalRect();
                foreach (var peer in _snaps)
                {
                    if (peer == this || peer._id != _peerId || !Available(peer)) continue;
                    var other = peer._target.GetGlobalRect();
                    float edge = _fromLeft ? rect.Position.X : rect.End.X;
                    float otherEdge = _fromLeft ? other.End.X : other.Position.X;
                    if (Mathf.Abs(edge - otherEdge) > ResizeSnapDistance
                        || Mathf.Max(rect.Position.Y, other.Position.Y) > Mathf.Min(rect.End.Y, other.End.Y) + ResizeSnapDistance) continue;
                    peer.Detach();
                    Partner = peer;
                    peer.Partner = this;
                    _partnerOrigin = other;
                    joined = true;
                    break;
                }
            }
            if (Partner == null) return;
            var partner = Partner._target;
            var current = _target.GetGlobalRect();
            var origin = _partnerOrigin;
            float left = _fromLeft ? origin.Position.X : current.Position.X;
            float right = _fromLeft ? current.End.X : origin.End.X;
            float minimum = Mathf.Max(MinimumSize.X, _target.GetCombinedMinimumSize().X);
            float partnerMinimum = Mathf.Max(Partner.MinimumSize.X, partner.GetCombinedMinimumSize().X);
            if (right - left < minimum + partnerMinimum) { Detach(); return; }
            float seam = joined ? (_fromLeft ? origin.End.X : origin.Position.X)
                : (_fromLeft ? current.Position.X : current.End.X);
            seam = Mathf.Round(Mathf.Clamp(seam, left + (_fromLeft ? partnerMinimum : minimum),
                right - (_fromLeft ? minimum : partnerMinimum)));
            float bottom = Mathf.Round(current.End.Y);
            float minHeight = Mathf.Max(Mathf.Max(MinimumSize.Y, _target.GetCombinedMinimumSize().Y),
                Mathf.Max(Partner.MinimumSize.Y, partner.GetCombinedMinimumSize().Y));
            if (bottom < minHeight) { Detach(); return; }
            float height = Mathf.Round(Mathf.Clamp(current.Size.Y, minHeight, bottom));
            _target.Size = new Vector2(_fromLeft ? right - seam : seam - left, height);
            _target.GlobalPosition = new Vector2(_fromLeft ? seam : left, bottom - height);
            partner.Size = new Vector2(_fromLeft ? seam - left : right - seam, height);
            partner.GlobalPosition = new Vector2(_fromLeft ? left : seam, bottom - height);
        }
    }
}
