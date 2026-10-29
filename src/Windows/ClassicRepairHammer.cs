using Godot;
using LibreKO;
using Access = KnightOnlineUiClassic.Native.Native;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The hammer swing of the original repair mode. The retail cursor shows the raised hammer
/// (cursorrepair01) while repairing and the struck hammer (cursorrepair02) from the moment a repair is
/// sent until the server answers. A local server answers within a frame, so the struck frame is held
/// for at least <see cref="StrikeSeconds"/> to keep the swing visible, and every repair sent by
/// Repair All swings once. The client's cursor state is left untouched: the struck art is laid over
/// it and the client's own cursor is restored afterwards.
/// </summary>
public partial class ClassicRepairHammer : Node
{
    /// <summary>How long the struck hammer stays down for one repair.</summary>
    public const double StrikeSeconds = 0.18;
    /// <summary>How long the hammer is raised again between two queued swings.</summary>
    public const double RaiseSeconds = 0.10;
    /// <summary>At most this many swings are queued (Repair All of a full set).</summary>
    public const int MaxQueued = 6;

    private const string ReadyStem = "repair";
    private const string StruckStem = "repair_alt";
    private static readonly Input.CursorShape[] Shapes = { Input.CursorShape.Arrow, Input.CursorShape.PointingHand };

    private readonly Func<bool> _active;
    private int _queued;
    private ulong _downUntil, _raisedUntil;
    private bool _overlaid;
    private string? _seen;

    private static ulong Now => Time.GetTicksMsec();
    private static ulong Later(double seconds) => Now + (ulong)(seconds * 1000);

    public ClassicRepairHammer() => _active = () => false;

    public ClassicRepairHammer(Func<bool> active)
    {
        _active = active;
        // After the client's cursor update, so the struck frame wins within the same frame.
        ProcessPriority = int.MaxValue;
    }

    /// <summary>True while the struck hammer is shown.</summary>
    public bool Struck => Now < _downUntil;

    /// <summary>The cursor art currently shown by the repair mode (for the preview's cursor overlay).</summary>
    public Texture2D? Current =>
        Art(Struck || Access.Get<string>(typeof(GameCursor), "applied") == StruckStem ? StruckStem : ReadyStem);

    /// <summary>One repair was sent: swing the hammer.</summary>
    public void Strike()
    {
        if (!_active()) return;
        if (Now >= _downUntil && Now >= _raisedUntil) _downUntil = Later(StrikeSeconds);
        else _queued = Math.Min(_queued + 1, MaxQueued);
    }

    /// <summary>Repair mode ended: drop queued swings and give the cursor back to the client.</summary>
    public void Stop()
    {
        _queued = 0;
        _downUntil = _raisedUntil = 0;
        Release();
    }

    public override void _Process(double delta)
    {
        if (_downUntil == 0 && _raisedUntil == 0 && _queued == 0) { Release(); return; }
        if (!_active()) { Stop(); return; }
        ulong now = Now;
        if (_downUntil != 0 && now >= _downUntil)
        {
            _raisedUntil = _queued > 0 ? Later(RaiseSeconds) : 0;
            _downUntil = 0;
        }
        if (_downUntil == 0 && _raisedUntil != 0 && now >= _raisedUntil)
        {
            _queued--;
            _downUntil = Later(StrikeSeconds);
            _raisedUntil = 0;
        }
        if (Struck) Overlay(); else Release();
    }

    public override void _ExitTree() => Stop();

    private void Overlay()
    {
        if (!Access.Get<bool>(typeof(GameCursor), "enabled")) return;
        string? applied = Access.Get<string>(typeof(GameCursor), "applied");
        // Nothing to do while the client already shows the struck hammer (request in flight).
        if (applied == StruckStem) { _overlaid = false; _seen = applied; return; }
        if (_overlaid && applied == _seen) return;
        if (Art(StruckStem) is not { } art) return;
        foreach (var shape in Shapes) Input.SetCustomMouseCursor(art, shape, Vector2.Zero);
        _overlaid = true;
        _seen = applied;
    }

    private void Release()
    {
        if (!_overlaid) return;
        _overlaid = false;
        GameCursor.Refresh();
    }

    private static Texture2D? Art(string stem) => Access.Call(typeof(GameCursor), "Peek", stem) as Texture2D;
}
